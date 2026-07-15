using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Myizam.Data.Embeddings;

/// <summary>Ollama /api/embed (bge-m3 и совместимые). Вариант B — бесплатная разработка на локальной GPU.</summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public string Name => "ollama";
    public string Model { get; }
    public int Dimension { get; }

    public OllamaEmbeddingProvider(HttpClient http, string? baseUrl = null, string? model = null, int? dimension = null)
    {
        _http = http;
        _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434").TrimEnd('/');
        Model = model ?? Environment.GetEnvironmentVariable("EMBEDDING_MODEL") ?? "bge-m3";
        Dimension = dimension ?? MyizamDbContext.EmbeddingDim;
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"{_baseUrl}/api/embed",
            new OllamaRequest(Model, texts), ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("Ollama: пустой ответ");
        if (body.Embeddings is null || body.Embeddings.Count != texts.Count)
            throw new InvalidOperationException($"Ollama: ожидали {texts.Count} векторов, получили {body.Embeddings?.Count ?? 0}");
        foreach (var e in body.Embeddings)
            if (e.Length != Dimension)
                throw new InvalidOperationException($"Ollama: размерность {e.Length} != EMBEDDING_DIM {Dimension} — проверь модель/конфиг");
        return body.Embeddings;
    }

    private sealed record OllamaRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record OllamaResponse(
        [property: JsonPropertyName("embeddings")] List<float[]>? Embeddings);
}
