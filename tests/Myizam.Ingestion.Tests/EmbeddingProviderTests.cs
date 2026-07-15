using System.Net;
using System.Text;
using System.Text.Json;
using Myizam.Data.Embeddings;
using Xunit;

namespace Myizam.Ingestion.Tests;

/// <summary>Тесты формирования запросов провайдеров (HTTP замокан) — без живых сервисов.</summary>
public class EmbeddingProviderTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public string? LastUrl;
        public string? LastBody;
        public Func<string, HttpResponseMessage> Respond = _ => new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri!.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return Respond(LastBody ?? "");
        }
    }

    private static HttpResponseMessage Json(object o) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task Ollama_sends_model_and_inputs_and_validates_dimension()
    {
        var handler = new FakeHandler
        {
            Respond = _ => Json(new { embeddings = new[] { new float[4], new float[4] } }),
        };
        var provider = new OllamaEmbeddingProvider(new HttpClient(handler),
            baseUrl: "http://fake:11434", model: "bge-m3", dimension: 4);

        var result = await provider.EmbedBatchAsync(new[] { "текст 1", "текст 2" });

        Assert.Equal(2, result.Count);
        Assert.Equal("http://fake:11434/api/embed", handler.LastUrl);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("bge-m3", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public async Task Ollama_throws_on_dimension_mismatch()
    {
        var handler = new FakeHandler
        {
            Respond = _ => Json(new { embeddings = new[] { new float[1024] } }),
        };
        var provider = new OllamaEmbeddingProvider(new HttpClient(handler),
            baseUrl: "http://fake:11434", model: "bge-m3", dimension: 1536);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedBatchAsync(new[] { "текст" }));
        Assert.Contains("размерность", ex.Message);
    }

    [Fact]
    public async Task Ollama_throws_on_count_mismatch()
    {
        var handler = new FakeHandler
        {
            Respond = _ => Json(new { embeddings = new[] { new float[4] } }),
        };
        var provider = new OllamaEmbeddingProvider(new HttpClient(handler),
            baseUrl: "http://fake:11434", model: "bge-m3", dimension: 4);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedBatchAsync(new[] { "а", "б" }));
    }

    [Fact]
    public async Task OpenAi_sends_dimensions_and_restores_order_by_index()
    {
        var handler = new FakeHandler
        {
            Respond = _ => Json(new
            {
                data = new object[]
                {
                    new { index = 1, embedding = new float[] { 2, 2 } },
                    new { index = 0, embedding = new float[] { 1, 1 } },
                },
            }),
        };
        var provider = new OpenAiEmbeddingProvider(new HttpClient(handler),
            apiKey: "test-key", model: "text-embedding-3-small", dimension: 2);

        var result = await provider.EmbedBatchAsync(new[] { "первый", "второй" });

        Assert.Equal(new float[] { 1, 1 }, result[0]);
        Assert.Equal(new float[] { 2, 2 }, result[1]);
        Assert.Contains("/v1/embeddings", handler.LastUrl);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(2, body.RootElement.GetProperty("dimensions").GetInt32());
    }

    [Fact]
    public void Embedding_input_is_header_plus_text()
    {
        Assert.Equal("Заголовок\nТекст", ChunkEmbedder.BuildEmbeddingInput("Заголовок", "Текст"));
    }

    [Fact]
    public void Batch_size_limit_is_100()
    {
        Assert.Equal(100, ChunkEmbedder.MaxBatchSize);
    }
}
