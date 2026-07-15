namespace Myizam.Core.Models;

public sealed record AskRequest(string Question);

/// <summary>Ответ /api/ask — сборка шага 9 (ТЗ v2.0 §7).</summary>
public sealed record AskResponse(
    string Answer,
    string AnswerLang,               // 'ky' | 'ru' | 'en'
    IReadOnlyList<SourceRef> Sources,
    GuardInfo? Guard,                // null в shadow-режиме (не показывается)
    string Disclaimer,
    bool Refused);

public sealed record SourceRef(
    int Marker,                      // [N] в тексте ответа
    long ChunkId,
    string Law,                      // «Трудовой кодекс Кыргызской Республики»
    string? Article,                 // «68»
    string? ArticleTitle,
    string Excerpt,                  // ≤ 400 символов
    string? Url);                    // cbd.minjust.gov.kg/…#st_N

public sealed record GuardInfo(bool Grounded, double Score);

public sealed record GuardVerdict(bool Grounded, double Score, string Model);

public sealed record RerankResult(long Id, double Score);

/// <summary>Кандидат retrieval, идущий через rerank в контекст LLM.</summary>
public sealed record RetrievedChunk(
    long ChunkId,
    string LawCode,
    string LawTitle,
    string? ArticleNumber,
    string? ArticleTitle,
    string Header,
    string Text,
    string? SourceUrl,
    double Similarity);

public sealed record ChatUsage(int TokensIn, int TokensOut);

public sealed record ChatResult(string Text, ChatUsage Usage);
