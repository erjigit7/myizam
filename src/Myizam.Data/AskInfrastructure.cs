using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Myizam.Core.Interfaces;
using Myizam.Core.Models;
using Myizam.Data.Embeddings;
using Pgvector;

namespace Myizam.Data;

/// <summary>Эмбеддинг вопроса с кэшем в Postgres (§7 шаг 3): попадание = 0 токенов.</summary>
public sealed class CachedQuestionEmbedder : IQuestionEmbedder
{
    private readonly MyizamDbContext _db;
    private readonly IEmbeddingProvider _provider;

    public CachedQuestionEmbedder(MyizamDbContext db, IEmbeddingProvider provider)
    {
        _db = db;
        _provider = provider;
    }

    public async Task<float[]> EmbedQuestionAsync(string questionRu, CancellationToken ct = default)
    {
        var normalized = questionRu.Trim().ToLowerInvariant();
        var key = Sha256($"{_provider.Model}:{normalized}");

        var cached = await _db.EmbeddingCache.FindAsync(new object[] { key }, ct);
        if (cached is not null) return cached.Embedding.ToArray();

        var vec = (await _provider.EmbedBatchAsync(new[] { normalized }, ct))[0];
        _db.EmbeddingCache.Add(new EmbeddingCacheEntity
        {
            Key = key,
            Provider = _provider.Name,
            Model = _provider.Model,
            Embedding = new Vector(vec),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return vec;
    }

    internal static string Sha256(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}

/// <summary>Адаптер ChunkRepository → домен (RetrievedChunk).</summary>
public sealed class PgChunkSearcher : IChunkSearcher
{
    private readonly ChunkRepository _repo;

    public PgChunkSearcher(ChunkRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(float[] queryEmbedding, int topK, CancellationToken ct = default)
    {
        var hits = await _repo.SearchAsync(queryEmbedding, topK, "ru", ct);
        return hits.Select(h => new RetrievedChunk(
            h.ChunkId, h.LawCode, h.LawTitle, h.ArticleNumber, h.ArticleTitle,
            h.Header, h.Text, h.SourceUrl, h.Similarity)).ToList();
    }
}

/// <summary>Кэш готовых ответов: Postgres + TTL 7 дней (§12). IMemoryCache сверху добавляет Api-слой.</summary>
public sealed class PgAnswerCache : IAnswerCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
    private readonly MyizamDbContext _db;

    public PgAnswerCache(MyizamDbContext db) => _db = db;

    public async Task<AskResponse?> GetAsync(string questionKey, CancellationToken ct = default)
    {
        var key = CachedQuestionEmbedder.Sha256(questionKey);
        var row = await _db.AnswerCache.FindAsync(new object[] { key }, ct);
        if (row is null) return null;
        if (DateTimeOffset.UtcNow - row.CreatedAt > Ttl)
        {
            _db.AnswerCache.Remove(row);
            await _db.SaveChangesAsync(ct);
            return null;
        }
        return JsonSerializer.Deserialize<AskResponse>(row.AnswerJson);
    }

    public async Task SetAsync(string questionKey, AskResponse response, CancellationToken ct = default)
    {
        var key = CachedQuestionEmbedder.Sha256(questionKey);
        var row = await _db.AnswerCache.FindAsync(new object[] { key }, ct);
        var json = JsonSerializer.Serialize(response);
        if (row is null)
            _db.AnswerCache.Add(new AnswerCacheEntity { Key = key, AnswerJson = json, CreatedAt = DateTimeOffset.UtcNow });
        else
        {
            row.AnswerJson = json;
            row.CreatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class PgQueryLogger : IQueryLogger
{
    private readonly MyizamDbContext _db;

    public PgQueryLogger(MyizamDbContext db) => _db = db;

    public async Task LogAsync(QueryLogRecord r, CancellationToken ct = default)
    {
        _db.QueryLog.Add(new QueryLogEntity
        {
            Question = r.Question,
            QuestionLang = r.QuestionLang,
            QuestionRu = r.QuestionRu,
            Answer = r.Answer,
            AnswerLang = r.AnswerLang,
            CitedChunkIds = r.CitedChunkIds,
            TopSimilarity = r.TopSimilarity,
            GuardScore = r.GuardScore,
            GuardGrounded = r.GuardGrounded,
            GuardModel = r.GuardModel,
            Refused = r.Refused,
            LatencyMs = r.LatencyMs,
            TokensIn = r.TokensIn,
            TokensOut = r.TokensOut,
            CostUsd = r.CostUsd,
            ClientHash = r.ClientHash,
            CacheHit = r.CacheHit,
            Error = r.Error,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
    }
}
