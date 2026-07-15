using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Myizam.Core.Interfaces;
using Myizam.Core.Models;

namespace Myizam.Data.MlSidecar;

/// <summary>
/// HTTP-клиенты ML-sidecar (§6). Ключевой контракт отказоустойчивости (§7):
/// любая ошибка/таймаут → null, вызывающий деградирует (top-K по cosine /
/// guard=skipped), пользователь ВСЕГДА получает ответ.
/// </summary>
public sealed class HttpRerankerClient : IRerankerClient
{
    private readonly HttpClient _http;

    public HttpRerankerClient(HttpClient http) => _http = http;   // Timeout=5s задаётся в DI

    public async Task<IReadOnlyList<RerankResult>?> RerankAsync(string query,
        IReadOnlyList<(long Id, string Text)> candidates, int topK, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/rerank", new
            {
                query,
                candidates = candidates.Select(c => new { id = c.Id, text = c.Text }),
                top_k = topK,
            }, ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken: ct);
            return body?.Results?.Select(r => new RerankResult(r.Id, r.Score)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;   // деградация: top-K по cosine (§7 шаг 5)
        }
    }

    private sealed record RerankResponse([property: JsonPropertyName("results")] List<RerankItem>? Results);
    private sealed record RerankItem(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("score")] double Score);
}

public sealed class HttpGuardClient : IGuardClient
{
    private readonly HttpClient _http;

    public HttpGuardClient(HttpClient http) => _http = http;

    public async Task<GuardVerdict?> CheckAsync(string context, string answer, string lang, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/guard/check", new { context, answer, lang }, ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<GuardResponse>(cancellationToken: ct);
            return body is null ? null : new GuardVerdict(body.Grounded, body.Score, body.Model ?? "unknown");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;   // guard_model='skipped', ответ уходит (§7 шаг 7)
        }
    }

    private sealed record GuardResponse(
        [property: JsonPropertyName("grounded")] bool Grounded,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("model")] string? Model);
}
