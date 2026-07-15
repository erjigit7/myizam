using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace Myizam.Data;

public sealed record ChunkSearchHit(
    long ChunkId,
    string LawCode,
    string LawTitle,
    string? ArticleNumber,
    string Header,
    string Text,
    double Similarity);

public sealed class ChunkRepository
{
    private readonly MyizamDbContext _db;

    public ChunkRepository(MyizamDbContext db) => _db = db;

    /// <summary>
    /// Векторный top-K по косинусной близости (ТЗ v2.1 §1.2).
    /// `&lt;=&gt;` — cosine distance pgvector; similarity = 1 - distance.
    /// </summary>
    public async Task<List<ChunkSearchHit>> SearchAsync(
        float[] queryEmbedding, int topK = 20, string lang = "ru", CancellationToken ct = default)
    {
        var q = new Vector(queryEmbedding);
        return await _db.Database
            .SqlQuery<ChunkSearchHit>($"""
                SELECT c.id AS "ChunkId",
                       c.law_code AS "LawCode",
                       l.title AS "LawTitle",
                       c.article_number AS "ArticleNumber",
                       c.header AS "Header",
                       c.text AS "Text",
                       1 - (c.embedding <=> {q}) AS "Similarity"
                FROM chunks c
                JOIN laws l ON l.document_code = c.law_code
                WHERE c.embedding IS NOT NULL AND c.lang = {lang}
                ORDER BY c.embedding <=> {q}
                LIMIT {topK}
                """)
            .ToListAsync(ct);
    }

    /// <summary>Upsert закона и его чанков из результатов ingest; идемпотентно по content_hash.</summary>
    public async Task<(int Inserted, int Skipped)> UpsertLawChunksAsync(
        LawEntity law, IReadOnlyList<ChunkEntity> chunks, CancellationToken ct = default)
    {
        var existing = await _db.Laws.FindAsync(new object[] { law.DocumentCode }, ct);
        if (existing is null)
        {
            _db.Laws.Add(law);
        }
        else
        {
            existing.Title = law.Title;
            existing.OfficialName = law.OfficialName;
            existing.Status = law.Status;
            existing.StatusCode = law.StatusCode;
            existing.EditionDate = law.EditionDate;
            existing.EditionId = law.EditionId;
            existing.ArticleCount = law.ArticleCount;
            existing.IngestedAt = law.IngestedAt;
        }

        var hashes = chunks.Select(c => c.ContentHash).ToList();
        var known = (await _db.Chunks
            .Where(c => hashes.Contains(c.ContentHash))
            .Select(c => c.ContentHash)
            .ToListAsync(ct)).ToHashSet();

        // Новая редакция: чанки, которых больше нет (изменённые статьи), удаляем —
        // вместе с устаревшими векторами; неизменённые сохраняют embedding
        var stale = await _db.Chunks
            .Where(c => c.LawCode == law.DocumentCode && c.Lang == chunks[0].Lang && !hashes.Contains(c.ContentHash))
            .ExecuteDeleteAsync(ct);
        if (stale > 0) Console.WriteLine($"  устаревших чанков удалено: {stale}");

        var fresh = chunks.Where(c => !known.Contains(c.ContentHash)).ToList();
        _db.Chunks.AddRange(fresh);
        await _db.SaveChangesAsync(ct);
        return (fresh.Count, known.Count);
    }
}
