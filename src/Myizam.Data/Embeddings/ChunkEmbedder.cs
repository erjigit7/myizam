using Microsoft.EntityFrameworkCore;
using Pgvector;
using Polly;
using Polly.Retry;

namespace Myizam.Data.Embeddings;

/// <summary>
/// Векторизация чанков (ТЗ v2.1 §1.2): батчи ≤100, ретраи 1с/2с/4с,
/// идемпотентность — эмбеддится только чанк с embedding IS NULL
/// (content_hash уникален, повторный ingest не создаёт дублей).
/// </summary>
public sealed class ChunkEmbedder
{
    public const int MaxBatchSize = 100;

    private static readonly ResiliencePipeline Retry = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(
                TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber))),   // 1с, 2с, 4с
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>().Handle<TaskCanceledException>(),
        })
        .Build();

    private readonly MyizamDbContext _db;
    private readonly IEmbeddingProvider _provider;
    private readonly Action<string>? _log;

    public ChunkEmbedder(MyizamDbContext db, IEmbeddingProvider provider, Action<string>? log = null)
    {
        _db = db;
        _provider = provider;
        _log = log;
    }

    /// <returns>Число векторизованных чанков.</returns>
    public async Task<int> EmbedPendingAsync(int batchSize = MaxBatchSize, CancellationToken ct = default)
    {
        if (batchSize is < 1 or > MaxBatchSize)
            throw new ArgumentOutOfRangeException(nameof(batchSize), $"батч 1..{MaxBatchSize}");

        var total = 0;
        while (true)
        {
            var batch = await _db.Chunks
                .Where(c => c.Embedding == null)
                .OrderBy(c => c.Id)
                .Take(batchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            // Эмбеддится заголовок + текст: контекст закона/статьи улучшает retrieval
            var inputs = batch.Select(c => BuildEmbeddingInput(c.Header, c.Text)).ToList();
            var vectors = await Retry.ExecuteAsync(
                async token => await _provider.EmbedBatchAsync(inputs, token), ct);

            for (var i = 0; i < batch.Count; i++)
                batch[i].Embedding = new Vector(vectors[i]);
            await _db.SaveChangesAsync(ct);

            total += batch.Count;
            _log?.Invoke($"  векторизовано {total} (батч {batch.Count}, провайдер {_provider.Name}/{_provider.Model})");
        }
        return total;
    }

    /// <summary>Единый формат входа эмбеддинга — тот же для чанков и (в будущем) вопросов.</summary>
    public static string BuildEmbeddingInput(string header, string text) => header + "\n" + text;
}
