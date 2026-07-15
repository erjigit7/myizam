using Microsoft.EntityFrameworkCore;
using Myizam.Data;
using Myizam.Data.Embeddings;

namespace Myizam.Ingestion.Pipeline;

/// <summary>
/// Команда `search "вопрос"` — ручная проверка векторного поиска (гейт фазы 1, ТЗ v2.1 §1.2):
/// эмбеддинг вопроса тем же провайдером → pgvector top-K → печать закон/статья/similarity.
/// </summary>
public static class SearchCommand
{
    public static async Task<int> RunAsync(string question, int topK, string lang, CancellationToken ct = default)
    {
        var cs = Environment.GetEnvironmentVariable("DATABASE_URL")
                 ?? "Host=localhost;Port=5432;Database=myizam;Username=myizam;Password=myizam";
        var options = new DbContextOptionsBuilder<MyizamDbContext>()
            .UseNpgsql(cs, o => o.UseVector())
            .Options;
        await using var db = new MyizamDbContext(options);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        IEmbeddingProvider provider =
            (Environment.GetEnvironmentVariable("EMBEDDING_PROVIDER") ?? "ollama") switch
            {
                "openai" => new OpenAiEmbeddingProvider(http),
                _ => new OllamaEmbeddingProvider(http),
            };

        var vec = (await provider.EmbedBatchAsync(new[] { question }, ct))[0];
        var hits = await new ChunkRepository(db).SearchAsync(vec, topK, lang, ct);

        Console.WriteLine($"Вопрос: {question}");
        Console.WriteLine($"{"sim",-6} {"закон",-8} {"статья",-8} заголовок");
        foreach (var h in hits)
            Console.WriteLine($"{h.Similarity:F3}  {h.LawCode,-8} {h.ArticleNumber ?? "преамб",-8} {Truncate(h.Header, 100)}");
        return 0;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
