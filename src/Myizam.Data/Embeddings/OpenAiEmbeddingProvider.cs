using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Myizam.Data.Embeddings;

/// <summary>OpenAI /v1/embeddings (text-embedding-3-small, 1536). Вариант A — прод.</summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public string Name => "openai";
    public string Model { get; }
    public int Dimension { get; }

    public OpenAiEmbeddingProvider(HttpClient http, string? apiKey = null, string? model = null, int? dimension = null, string? baseUrl = null)
    {
        _http = http;
        var key = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                  ?? throw new InvalidOperationException("OPENAI_API_KEY не задан");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        _baseUrl = (baseUrl ?? "https://api.openai.com").TrimEnd('/');
        Model = model ?? Environment.GetEnvironmentVariable("EMBEDDING_MODEL") ?? "text-embedding-3-small";
        Dimension = dimension ?? MyizamDbContext.EmbeddingDim;
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"{_baseUrl}/v1/embeddings",
            new OpenAiRequest(Model, texts, Dimension), ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("OpenAI: пустой ответ");
        if (body.Data is null || body.Data.Count != texts.Count)
            throw new InvalidOperationException($"OpenAI: ожидали {texts.Count} векторов, получили {body.Data?.Count ?? 0}");
        // API гарантирует порядок, но index в ответе есть — сортируем на всякий случай
        return body.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();
    }

    private sealed record OpenAiRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("dimensions")] int Dimensions);

    private sealed record OpenAiResponse(
        [property: JsonPropertyName("data")] List<OpenAiDatum>? Data);

    private sealed record OpenAiDatum(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
