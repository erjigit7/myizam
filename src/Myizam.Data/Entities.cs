using Pgvector;

namespace Myizam.Data;

// Схема по ТЗ v2.0 §5 с правками Parser Spec §10:
// laws: +status, +status_code, +edition_date; chunks: +lang default 'ru'.
// ТЗ v2.0 целиком в репозитории отсутствует — поля, не описанные в доступных
// документах, спроектированы по контрактам пайплайна (§7) и помечены комментарием.

public sealed class LawEntity
{
    public required string DocumentCode { get; set; }   // "3-45" — PK
    public required string Title { get; set; }          // короткое название
    public required string OfficialName { get; set; }   // полное nameRus
    public required string Status { get; set; }         // "Действует"
    public required string StatusCode { get; set; }     // "10"
    public DateOnly EditionDate { get; set; }
    public long EditionId { get; set; }
    public int ArticleCount { get; set; }
    public DateTimeOffset IngestedAt { get; set; }

    public List<ChunkEntity> Chunks { get; set; } = new();
}

public sealed class ChunkEntity
{
    public long Id { get; set; }
    public required string LawCode { get; set; }
    public string? ArticleNumber { get; set; }          // null — преамбула
    public int Part { get; set; }
    public int PartCount { get; set; }
    public required string Header { get; set; }         // контекстный заголовок
    public required string Text { get; set; }
    public required string ContentHash { get; set; }    // sha256(header+text) — идемпотентность эмбеддингов
    public string Lang { get; set; } = "ru";
    public Vector? Embedding { get; set; }              // null до прогона embed
    public DateTimeOffset CreatedAt { get; set; }

    public LawEntity? Law { get; set; }
}

/// <summary>Кэш эмбеддингов ВОПРОСОВ пользователей (§7 шаг 3) — чанки кэшируются самим полем chunks.embedding.</summary>
public sealed class EmbeddingCacheEntity
{
    public required string Key { get; set; }            // sha256(provider|model|normalized_text)
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public required Vector Embedding { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Лог запросов /api/ask (§5) — поля по шагам пайплайна §7; cost_usd обязателен.</summary>
public sealed class QueryLogEntity
{
    public long Id { get; set; }
    public DateTimeOffset Ts { get; set; }
    public required string ClientHash { get; set; }     // sha256(IP+соль)
    public string? LangDetected { get; set; }           // ru|ky|en
    public required string Question { get; set; }
    public string? QuestionRu { get; set; }             // после моста на русский
    public string? Answer { get; set; }
    public string? SourcesJson { get; set; }            // [{law, article, similarity}] — jsonb
    public double? TopSimilarity { get; set; }
    public double? GuardScore { get; set; }
    public string? GuardMode { get; set; }              // shadow|warn|block
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public decimal CostUsd { get; set; }
    public int LatencyMs { get; set; }
    public bool CacheHit { get; set; }
    public bool Refused { get; set; }                   // отказ по порогу похожести
    public string? Error { get; set; }
}
