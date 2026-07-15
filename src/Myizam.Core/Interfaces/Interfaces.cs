using Myizam.Core.Models;

namespace Myizam.Core.Interfaces;

public interface IQuestionEmbedder
{
    /// <summary>Эмбеддинг русского вопроса; кэш по sha256(model:normalized) — попадание = 0 токенов (§7 шаг 3).</summary>
    Task<float[]> EmbedQuestionAsync(string questionRu, CancellationToken ct = default);
}

public interface IChunkSearcher
{
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(float[] queryEmbedding, int topK, CancellationToken ct = default);
}

public interface IChatProvider
{
    /// <summary>Один вызов чат-модели (system + user), температура и лимит — параметрами.</summary>
    Task<ChatResult> CompleteAsync(string systemPrompt, string userMessage,
        double temperature, int maxTokens, CancellationToken ct = default);
}

/// <summary>
/// Перевод в пайплайне /api/ask: вопрос → русский (шаг 2) и ответ → язык вопроса (шаг 8).
/// Реализации: LLM-мост по промптам §10 (по умолчанию) или дообученный
/// KyrgyzLLM-переводчик (kazllm-legal-translate) для кыргызского направления.
/// </summary>
public interface ITranslator
{
    /// <summary>DetectedLang — если реализация умеет определять язык (LLM-мост); null = оставить эвристику.</summary>
    Task<(string Ru, string? DetectedLang, ChatUsage Usage)> ToRussianAsync(
        string question, string heuristicLang, CancellationToken ct = default);

    /// <summary>Маркеры [N] обязаны сохраниться.</summary>
    Task<(string Text, ChatUsage Usage)> FromRussianAsync(
        string answerRu, string targetLang, CancellationToken ct = default);
}

public interface IRerankerClient
{
    /// <summary>POST /rerank sidecar-а; null = sidecar недоступен (деградация — top-K по cosine, §7 шаг 5).</summary>
    Task<IReadOnlyList<RerankResult>?> RerankAsync(string query,
        IReadOnlyList<(long Id, string Text)> candidates, int topK, CancellationToken ct = default);
}

public interface IGuardClient
{
    /// <summary>POST /guard/check; null = sidecar недоступен (guard_model='skipped', §7 шаг 7).</summary>
    Task<GuardVerdict?> CheckAsync(string context, string answer, string lang, CancellationToken ct = default);
}

public interface IAnswerCache
{
    Task<AskResponse?> GetAsync(string questionRuNormalized, CancellationToken ct = default);
    Task SetAsync(string questionRuNormalized, AskResponse response, CancellationToken ct = default);
}

public sealed record QueryLogRecord(
    string Question, string QuestionLang, string? QuestionRu,
    string? Answer, string? AnswerLang, long[]? CitedChunkIds,
    double? TopSimilarity, double? GuardScore, bool? GuardGrounded, string? GuardModel,
    bool Refused, int LatencyMs, int TokensIn, int TokensOut, decimal CostUsd,
    string ClientHash, bool CacheHit, string? Error);

public interface IQueryLogger
{
    Task LogAsync(QueryLogRecord record, CancellationToken ct = default);
}
