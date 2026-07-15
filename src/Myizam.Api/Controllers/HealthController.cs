using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Myizam.Data;

namespace Myizam.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly MyizamDbContext _db;
    private readonly IHttpClientFactory _httpFactory;

    public HealthController(MyizamDbContext db, IHttpClientFactory httpFactory)
    {
        _db = db;
        _httpFactory = httpFactory;
    }

    /// <summary>Агрегированный health: db + ml + llm-ping (§12).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("health");
        http.Timeout = TimeSpan.FromSeconds(5);

        var db = await Probe(async () => await _db.Database.CanConnectAsync(ct));

        var mlUrl = Environment.GetEnvironmentVariable("ML_SIDECAR_URL")
                    ?? Environment.GetEnvironmentVariable("ML_URL") ?? "http://localhost:8000";
        var ml = await Probe(async () =>
            (await http.GetAsync($"{mlUrl.TrimEnd('/')}/health", ct)).IsSuccessStatusCode);

        var chatBase = (Environment.GetEnvironmentVariable("CHAT_BASE_URL") ?? "https://api.openai.com").TrimEnd('/');
        var llm = await Probe(async () =>
        {
            // лёгкий ping: список моделей, без генерации
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{chatBase}/v1/models");
            var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(key))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            return (await http.SendAsync(req, ct)).IsSuccessStatusCode;
        });

        var ok = db && ml && llm;
        return StatusCode(ok ? 200 : 503, new
        {
            status = ok ? "ok" : "degraded",
            db = db ? "ok" : "down",
            ml = ml ? "ok" : "down",
            llm = llm ? "ok" : "down",
        });
    }

    private static async Task<bool> Probe(Func<Task<bool>> probe)
    {
        try { return await probe(); }
        catch { return false; }
    }
}
