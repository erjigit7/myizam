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
    public string? SourceUrl { get; set; }              // https://cbd.minjust.gov.kg/{code}/edition/{editionId}/ru
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
    // Метаданные для панели источников (§5/§9)
    public string? ArticleTitle { get; set; }
    public string? Chapter { get; set; }
    public string? Section { get; set; }
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

/// <summary>Лог запросов /api/ask — точная схема ТЗ v2.0 §5 (+ cache_hit/error сверх спеки).</summary>
public sealed class QueryLogEntity
{
    public long Id { get; set; }
    public required string Question { get; set; }
    public required string QuestionLang { get; set; }   // 'ky' | 'ru' | 'en'
    public string? QuestionRu { get; set; }             // после перевода-моста (= question если ru)
    public string? Answer { get; set; }
    public string? AnswerLang { get; set; }
    public long[]? CitedChunkIds { get; set; }
    public double? TopSimilarity { get; set; }          // max cosine similarity до reranker
    public double? GuardScore { get; set; }
    public bool? GuardGrounded { get; set; }
    public string? GuardModel { get; set; }             // 'en' | 'kg' | 'skipped'
    public bool Refused { get; set; }                   // система ответила «не знаю»
    public int LatencyMs { get; set; }
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public decimal CostUsd { get; set; }
    public required string ClientHash { get; set; }     // sha256(IP + соль), сырой IP не хранится
    public bool CacheHit { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Кэш готовых ответов (§12): sha256(нормализованный question_ru) → ответ, TTL 7 дней.</summary>
public sealed class AnswerCacheEntity
{
    public required string Key { get; set; }            // sha256(lower(trim(question_ru)))
    public required string AnswerJson { get; set; }     // сериализованный AskResponse (русская версия)
    public DateTimeOffset CreatedAt { get; set; }
}
