using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Myizam.Data;
using Myizam.Data.Embeddings;

namespace Myizam.Ingestion.Pipeline;

/// <summary>
/// Команда `embed` (ТЗ v2.1 §1.2): data/chunks/*.jsonl → upsert laws+chunks в Postgres
/// → векторизация всех чанков с embedding IS NULL (батчи ≤100, ретраи, идемпотентность).
/// </summary>
public static class EmbedCommand
{
    public static async Task<int> RunAsync(string repoRoot, string lang, CancellationToken ct = default)
    {
        var chunksDir = Path.Combine(repoRoot, "data", "chunks");
        var files = Directory.Exists(chunksDir)
            ? Directory.GetFiles(chunksDir, $"*_{lang}.jsonl")
            : Array.Empty<string>();
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"Нет файлов чанков в data/chunks/*_{lang}.jsonl — сначала прогнать ingest");
            return 2;
        }

        var cs = Environment.GetEnvironmentVariable("DATABASE_URL")
                 ?? "Host=localhost;Port=5432;Database=myizam;Username=myizam;Password=myizam";
        var options = new DbContextOptionsBuilder<MyizamDbContext>()
            .UseNpgsql(cs, o => o.UseVector())
            .Options;
        await using var db = new MyizamDbContext(options);
        await db.Database.MigrateAsync(ct);

        var repo = new ChunkRepository(db);
        foreach (var file in files)
        {
            var rows = new List<Chunk>();
            await foreach (var line in File.ReadLinesAsync(file, ct))
                if (!string.IsNullOrWhiteSpace(line))
                    rows.Add(JsonSerializer.Deserialize<Chunk>(line)!);
            if (rows.Count == 0) continue;

            var first = rows[0];
            var law = new LawEntity
            {
                DocumentCode = first.LawCode,
                Title = first.LawTitle,
                OfficialName = ReadOfficialName(repoRoot, first.LawCode) ?? first.LawTitle,
                Status = first.Status,
                StatusCode = first.StatusCode,
                EditionDate = DateOnly.Parse(first.EditionDate),
                EditionId = first.EditionId,
                ArticleCount = rows.Where(r => r.ArticleNumber is not null).Select(r => r.ArticleNumber).Distinct().Count(),
                IngestedAt = DateTimeOffset.UtcNow,
            };
            var entities = rows.Select(r => new ChunkEntity
            {
                LawCode = r.LawCode,
                ArticleNumber = r.ArticleNumber,
                Part = r.Part,
                PartCount = r.PartCount,
                Header = r.Header,
                Text = r.Text,
                ContentHash = r.ContentHash,
                Lang = r.Lang,
                CreatedAt = DateTimeOffset.UtcNow,
            }).ToList();

            var (inserted, skipped) = await repo.UpsertLawChunksAsync(law, entities, ct);
            Console.WriteLine($"{first.LawCode} ({first.LawTitle}): новых чанков {inserted}, без изменений {skipped}");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        IEmbeddingProvider provider =
            (Environment.GetEnvironmentVariable("EMBEDDING_PROVIDER") ?? "ollama") switch
            {
                "openai" => new OpenAiEmbeddingProvider(http),
                "ollama" => new OllamaEmbeddingProvider(http),
                var p => throw new InvalidOperationException($"Неизвестный EMBEDDING_PROVIDER: {p}"),
            };
        Console.WriteLine($"Провайдер эмбеддингов: {provider.Name}/{provider.Model} (dim={provider.Dimension})");

        var embedder = new ChunkEmbedder(db, provider, Console.WriteLine);
        var count = await embedder.EmbedPendingAsync(ct: ct);
        var totalWithVectors = await db.Chunks.CountAsync(c => c.Embedding != null, ct);
        Console.WriteLine($"Векторизовано за прогон: {count}; всего чанков с векторами: {totalWithVectors}");
        return 0;
    }

    private static string? ReadOfficialName(string repoRoot, string lawCode)
    {
        var path = Path.Combine(repoRoot, "data", "raw", $"{lawCode}_document.json");
        if (!File.Exists(path)) return null;
        try
        {
            var doc = JsonSerializer.Deserialize<GetDocumentResponse>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return doc?.NameRus;
        }
        catch (JsonException) { return null; }
    }
}
