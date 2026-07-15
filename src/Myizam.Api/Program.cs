using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Myizam.Core.Interfaces;
using Myizam.Core.Services;
using Myizam.Data;
using Myizam.Data.Embeddings;
using Myizam.Data.Llm;
using Myizam.Data.MlSidecar;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog: JSON-логи, без сырых IP и ключей (§12)
builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Information()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) ?? fallback;

var dbConnection = Env("DATABASE_URL", "Host=localhost;Port=5432;Database=myizam;Username=myizam;Password=myizam");
var mlUrl = Env("ML_SIDECAR_URL", Env("ML_URL", "http://localhost:8000"));

// --- DI ---
builder.Services.AddDbContext<MyizamDbContext>(o => o.UseNpgsql(dbConnection, n => n.UseVector()));
builder.Services.AddScoped<ChunkRepository>();
builder.Services.AddScoped<IChunkSearcher, PgChunkSearcher>();
builder.Services.AddScoped<IQuestionEmbedder, CachedQuestionEmbedder>();
builder.Services.AddScoped<IAnswerCache, PgAnswerCache>();
builder.Services.AddScoped<IQueryLogger, PgQueryLogger>();
builder.Services.AddSingleton<LanguageService>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton(new AskOptions
{
    MinSimilarity = double.Parse(Env("MIN_SIMILARITY", "0.35"), System.Globalization.CultureInfo.InvariantCulture),
    GuardMode = Env("GUARD_MODE", "shadow"),
    CostPer1MTokensIn = decimal.Parse(Env("COST_PER_1M_TOKENS_IN", "0.15"), System.Globalization.CultureInfo.InvariantCulture),
    CostPer1MTokensOut = decimal.Parse(Env("COST_PER_1M_TOKENS_OUT", "0.60"), System.Globalization.CultureInfo.InvariantCulture),
});
builder.Services.AddScoped<AskService>();

builder.Services.AddSingleton<IEmbeddingProvider>(_ =>
{
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    return Env("EMBEDDING_PROVIDER", "ollama") == "openai"
        ? new OpenAiEmbeddingProvider(http)
        : new OllamaEmbeddingProvider(http);
});
builder.Services.AddHttpClient<IRerankerClient, HttpRerankerClient>(c =>
{
    c.BaseAddress = new Uri(mlUrl);
    c.Timeout = TimeSpan.FromSeconds(5);          // §7 шаг 5
});
builder.Services.AddHttpClient<IGuardClient, HttpGuardClient>(c =>
{
    c.BaseAddress = new Uri(mlUrl);
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<IChatProvider, OpenAiCompatChatProvider>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(int.Parse(Env("CHAT_TIMEOUT_SECONDS", "60")));   // локальный qwen медленнее gpt-4o-mini
});

// Переводчик (шаги 2 и 8): по умолчанию LLM-мост §10; TRANSLATOR=kyrgyzllm —
// дообученный kazllm-legal-translate для ky-направления (en остаётся на мосте)
if (Env("TRANSLATOR", "llm") == "kyrgyzllm")
{
    builder.Services.AddHttpClient<ITranslator, KyrgyzLlmTranslator>((sp, c) =>
    {
        c.BaseAddress = new Uri(Env("TRANSLATE_BASE_URL", Env("OLLAMA_URL", "http://localhost:11434")));
        c.Timeout = TimeSpan.FromSeconds(int.Parse(Env("CHAT_TIMEOUT_SECONDS", "60")));
    }).AddTypedClient<ITranslator>((http, sp) =>
        new KyrgyzLlmTranslator(http, new LlmBridgeTranslator(sp.GetRequiredService<IChatProvider>())));
}
else
{
    builder.Services.AddScoped<ITranslator>(sp => new LlmBridgeTranslator(sp.GetRequiredService<IChatProvider>()));
}

// --- Rate limit (§12): 5/сутки + 3/мин по client_hash ---
var salt = Env("CLIENT_HASH_SALT", "dev-salt");
string ClientHash(HttpContext ctx)
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip + salt))).ToLowerInvariant();
}

var dailyLimit = int.Parse(Env("RATE_LIMIT_DAILY", "5"));
var burstLimit = int.Parse(Env("RATE_LIMIT_PER_MINUTE", "3"));

// Суточный лимит — честным подсчётом по query_log (точные X-RateLimit-Remaining/Reset,
// см. AskController); встроенный лимитер — только анти-бёрст 3/мин
builder.Services.AddSingleton(new Myizam.Api.RateLimitConfig(dailyLimit));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("ask-burst", ctx => RateLimitPartition.GetFixedWindowLimiter(
        "burst:" + ClientHash(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = burstLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await ctx.HttpContext.Response.WriteAsync(
            """{"error":"Дневной лимит вопросов исчерпан. Попробуйте завтра. / Суроолордун күндүк лимити түгөндү. Эртең кайра аракет кылыңыз."}""", ct);
    };
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(Env("CORS_ORIGINS", "http://localhost:4200").Split(','))
    .AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders("X-RateLimit-Remaining", "X-RateLimit-Reset")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("health");

var app = builder.Build();

// Глобальный error-handler: наружу код+сообщение, стектрейс — в лог (§12)
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (BridgeUnavailableException ex)
    {
        Log.Warning(ex, "Мост недоступен");
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Необработанная ошибка {Path}", ctx.Request.Path);
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { error = "Внутренняя ошибка сервиса" });
    }
});

app.UseSerilogRequestLogging();
app.UseCors();
app.UseRateLimiter();

// client_hash в HttpContext.Items — контроллеру и логу (§12: сырой IP не хранится)
app.Use(async (ctx, next) =>
{
    ctx.Items["client_hash"] = ClientHash(ctx);
    await next();
});

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

// авто-миграция на старте (для MVP; в проде — осознанный шаг)
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<MyizamDbContext>().Database.MigrateAsync();

app.Run();

public partial class Program;   // для WebApplicationFactory в тестах

namespace Myizam.Api
{
    public sealed record RateLimitConfig(int DailyLimit);
}
