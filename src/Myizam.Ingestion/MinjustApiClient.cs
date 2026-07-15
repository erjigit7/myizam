using System.Text.Json;

namespace Myizam.Ingestion;

/// <summary>
/// Клиент API cbd.minjust.gov.kg.
/// Факты, установленные разведкой (июль 2026), которые НЕЛЬЗЯ «чинить»:
///  - WAF отдаёт 403 Forbidden на нестандартный User-Agent (в т.ч. «вежливый»
///    Myizam-Ingestion/1.0) — работает только браузерный UA;
///  - лимит ~20 запросов за 5 секунд, при превышении тело ответа —
///    ПЛОСКИЙ ТЕКСТ «API calls quota exceeded!…» с кодом 200, не JSON;
///  - при lang=kg кыргызский текст приходит в поле contentRu (contentKg — легаси, null).
/// </summary>
public sealed class MinjustApiClient : IDisposable
{
    private const string BaseUrl = "https://cbd.minjust.gov.kg/api/v1/";

    // Кастомный UA заблокирован WAF-ом — используем браузерный (проверено 15.07.2026)
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private static readonly TimeSpan DelayBetweenRequests = TimeSpan.FromSeconds(1.5);
    private const int MaxAttempts = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public MinjustApiClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<GetDocumentResponse> GetDocumentAsync(string documentCode, CancellationToken ct = default)
    {
        var json = await GetJsonAsync($"GetDocument?DocumentCode={Uri.EscapeDataString(documentCode)}", ct);
        var doc = JsonSerializer.Deserialize<GetDocumentResponse>(json, JsonOptions)
                  ?? throw new InvalidOperationException($"GetDocument({documentCode}): пустой ответ");
        if (doc.Status is null)
            throw new InvalidOperationException($"GetDocument({documentCode}): нет поля status");
        if (doc.Editions is null || doc.Editions.Count == 0)
            throw new InvalidOperationException($"GetDocument({documentCode}): пустой список editions");
        return doc;
    }

    public async Task<GetEditionResponse> GetEditionAsync(long editionId, string lang, CancellationToken ct = default)
    {
        if (lang is not ("ru" or "kg"))
            throw new ArgumentException($"lang должен быть ru или kg, получено: {lang}", nameof(lang));
        var json = await GetJsonAsync($"GetEdition?editionId={editionId}&lang={lang}&exact=false", ct);
        return JsonSerializer.Deserialize<GetEditionResponse>(json, JsonOptions)
               ?? throw new InvalidOperationException($"GetEdition({editionId}, {lang}): пустой ответ");
    }

    private async Task<string> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await ThrottleAsync(ct);
            try
            {
                var resp = await _http.GetAsync(BaseUrl + relativeUrl, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                if (resp.IsSuccessStatusCode && body.TrimStart().StartsWith('{'))
                    return body;

                // 200 + плоский текст = превышение квоты; 403/429/5xx — тоже ретраим
                last = new HttpRequestException(
                    $"{relativeUrl}: HTTP {(int)resp.StatusCode}, тело: {Truncate(body, 120)}");
            }
            catch (HttpRequestException ex) { last = ex; }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { last = ex; }

            if (attempt < MaxAttempts)
            {
                var backoff = TimeSpan.FromSeconds(3 * attempt);
                Console.Error.WriteLine($"  ! попытка {attempt}/{MaxAttempts} не удалась ({Truncate(last!.Message, 100)}), пауза {backoff.TotalSeconds:0}с");
                await Task.Delay(backoff, ct);
            }
        }
        throw new InvalidOperationException($"API недоступен после {MaxAttempts} попыток: {relativeUrl}", last);
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        var sinceLast = DateTimeOffset.UtcNow - _lastRequestAt;
        if (sinceLast < DelayBetweenRequests)
            await Task.Delay(DelayBetweenRequests - sinceLast, ct);
        _lastRequestAt = DateTimeOffset.UtcNow;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => _http.Dispose();
}
