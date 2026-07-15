namespace Myizam.Data.Embeddings;

/// <summary>
/// Провайдер эмбеддингов (ТЗ v2.1 §1.1): A = OpenAI text-embedding-3-small (1536),
/// B = Ollama + bge-m3 (1024). Выбор — env EMBEDDING_PROVIDER=openai|ollama;
/// размерность должна совпадать с EMBEDDING_DIM, зашитой в миграцию.
/// </summary>
public interface IEmbeddingProvider
{
    string Name { get; }
    string Model { get; }
    int Dimension { get; }

    /// <summary>Эмбеддинги батча текстов (вызывающий отвечает за размер батча ≤100).</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
