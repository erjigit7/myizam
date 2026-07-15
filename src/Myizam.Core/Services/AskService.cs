using System.Diagnostics;
using Myizam.Core.Interfaces;
using Myizam.Core.Models;

namespace Myizam.Core.Services;

public sealed class AskOptions
{
    public double MinSimilarity { get; set; } = 0.35;      // калибруется по golden set (§11)
    public string GuardMode { get; set; } = "shadow";      // shadow | warn | block (§8)
    public int RetrievalTopK { get; set; } = 20;
    public int RerankTopK { get; set; } = 5;
    public int MaxAnswerTokens { get; set; } = 700;
    // Цена за 1M токенов (gpt-4o-mini: 0.15/0.60); для локального qwen — нули
    public decimal CostPer1MTokensIn { get; set; }
    public decimal CostPer1MTokensOut { get; set; }
}

/// <summary>Исключение шага 2: мост недоступен → «сервис перегружен» (§7).</summary>
public sealed class BridgeUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Главный пайплайн /api/ask — 10 шагов ТЗ v2.0 §7.</summary>
public sealed class AskService
{
    private static readonly Dictionary<string, string> Disclaimers = new()
    {
        ["ru"] = "Это справочная информация, а не юридическая консультация. Проверяйте по первоисточнику.",
        ["ky"] = "Бул маалымдама гана, юридикалык консультация эмес. Түпнуска булактан текшериңиз.",
        ["en"] = "This is reference information, not legal advice. Verify against the original source.",
    };

    private static readonly Dictionary<string, string> RefusalTexts = new()
    {
        ["ru"] = "В моей базе законов нет ответа на этот вопрос. Я знаю только кодексы Кыргызской Республики из списка на странице «База знаний».",
        ["ky"] = "Менин мыйзамдар базамда бул суроого жооп жок. Мен «Билим базасы» барагындагы Кыргыз Республикасынын кодекстерин гана билем.",
        ["en"] = "My legal database has no answer to this question. I only know the codes of the Kyrgyz Republic listed on the Knowledge Base page.",
    };

    private readonly LanguageService _language;
    private readonly PromptBuilder _prompts;
    private readonly IQuestionEmbedder _embedder;
    private readonly IChunkSearcher _searcher;
    private readonly IRerankerClient _reranker;
    private readonly IGuardClient _guard;
    private readonly IChatProvider _chat;
    private readonly ITranslator _translator;
    private readonly IAnswerCache _cache;
    private readonly IQueryLogger _log;
    private readonly AskOptions _opts;

    public AskService(LanguageService language, PromptBuilder prompts, IQuestionEmbedder embedder,
        IChunkSearcher searcher, IRerankerClient reranker, IGuardClient guard,
        IChatProvider chat, ITranslator translator, IAnswerCache cache, IQueryLogger log, AskOptions opts)
    {
        _language = language;
        _prompts = prompts;
        _embedder = embedder;
        _searcher = searcher;
        _reranker = reranker;
        _guard = guard;
        _chat = chat;
        _translator = translator;
        _cache = cache;
        _log = log;
        _opts = opts;
    }

    public async Task<AskResponse> AskAsync(string question, string clientHash, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int tokensIn = 0, tokensOut = 0;

        // Шаг 1: язык — эвристика (приоритет у LLM на шаге 2)
        var lang = _language.Detect(question);

        // Шаг 2: перевод-мост (только если не русский)
        var questionRu = question;
        if (lang != "ru")
        {
            try
            {
                var (ru, detectedLang, usage) = await _translator.ToRussianAsync(question, lang, ct);
                tokensIn += usage.TokensIn;
                tokensOut += usage.TokensOut;
                questionRu = ru;
                if (detectedLang is "ky" or "ru" or "en" && detectedLang != lang)
                    lang = detectedLang;   // расхождение: приоритет у LLM (§7 шаг 1)
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Без перевода поиск по непереведённому вопросу даст мусор — честно отказываем
                throw new BridgeUnavailableException("Сервис перегружен, попробуйте позже", ex);
            }
        }

        var cacheKey = $"{lang}:{Normalize(questionRu)}";

        // Кэш готовых ответов «до генерации» (§12): популярные вопросы = 0 токенов
        var cached = await _cache.GetAsync(cacheKey, ct);
        if (cached is not null)
        {
            await _log.LogAsync(new QueryLogRecord(question, lang, questionRu,
                cached.Answer, cached.AnswerLang, cached.Sources.Select(s => s.ChunkId).ToArray(),
                null, null, null, null, cached.Refused, (int)sw.ElapsedMilliseconds,
                0, 0, 0, clientHash, CacheHit: true, Error: null), ct);
            return cached;
        }

        // Шаг 3: эмбеддинг русского вопроса (кэш внутри embedder-а)
        var embedding = await _embedder.EmbedQuestionAsync(questionRu, ct);

        // Шаг 4: pgvector top-20 + порог отсечки
        var hits = await _searcher.SearchAsync(embedding, _opts.RetrievalTopK, ct);
        var topSimilarity = hits.Count > 0 ? hits.Max(h => h.Similarity) : 0;
        if (hits.Count == 0 || topSimilarity < _opts.MinSimilarity)
        {
            var refusal = BuildRefusal(lang);
            await _cache.SetAsync(cacheKey, refusal, ct);
            await _log.LogAsync(new QueryLogRecord(question, lang, questionRu,
                refusal.Answer, lang, null, topSimilarity, null, null, null,
                Refused: true, (int)sw.ElapsedMilliseconds, tokensIn, tokensOut,
                Cost(tokensIn, tokensOut), clientHash, false, null), ct);
            return refusal;
        }

        // Шаг 5: rerank top-20 → top-5; деградация — top-5 по cosine
        var top5 = await RerankOrDegradeAsync(questionRu, hits, ct);

        // Шаг 6: генерация НА РУССКОМ + пост-валидация маркеров
        var (systemPrompt, includedCount) = _prompts.BuildGenerationPrompt(top5);
        var gen = await _chat.CompleteAsync(systemPrompt, questionRu,
            temperature: 0.2, maxTokens: _opts.MaxAnswerTokens, ct);
        tokensIn += gen.Usage.TokensIn;
        tokensOut += gen.Usage.TokensOut;
        var (answerRu, _) = PromptBuilder.StripDanglingMarkers(gen.Text, includedCount);

        // LLM-отказ по правилу 3 промпта («В предоставленных статьях нет ответа…») —
        // это тоже refused: без источников, честный отказ на языке вопроса
        if (IsLlmRefusal(answerRu))
        {
            var llmRefusal = BuildRefusal(lang);
            await _cache.SetAsync(cacheKey, llmRefusal, ct);
            await _log.LogAsync(new QueryLogRecord(question, lang, questionRu,
                llmRefusal.Answer, lang, null, topSimilarity, null, null, null,
                Refused: true, (int)sw.ElapsedMilliseconds, tokensIn, tokensOut,
                Cost(tokensIn, tokensOut), clientHash, false, null), ct);
            return llmRefusal;
        }

        // Шаг 7: guard (проверяется РУССКАЯ версия — решение зафиксировано в §7)
        var guardContext = string.Join("\n\n", top5.Take(includedCount).Select(c => c.Header + "\n" + c.Text));
        var verdict = await _guard.CheckAsync(guardContext, answerRu, "ru", ct);

        // Шаг 8: перевод ответа при необходимости
        var answer = answerRu;
        if (lang != "ru")
        {
            var (translated, trUsage) = await _translator.FromRussianAsync(answerRu, lang, ct);
            tokensIn += trUsage.TokensIn;
            tokensOut += trUsage.TokensOut;
            answer = translated;
        }

        // Шаг 9: сборка — источники по реально использованным маркерам
        var used = PromptBuilder.UsedMarkers(answer, includedCount);
        if (used.Count == 0) used = Enumerable.Range(1, Math.Min(3, includedCount)).ToList();
        var sources = used.Select(n =>
        {
            var c = top5[n - 1];
            return new SourceRef(n, c.ChunkId, c.LawTitle, c.ArticleNumber, c.ArticleTitle,
                Excerpt(c.Text), ArticleUrl(c));
        }).ToList();

        var guardInfo = _opts.GuardMode == "warn" && verdict is not null
            ? new GuardInfo(verdict.Grounded, verdict.Score)
            : null;   // shadow: score только в лог (§8)

        var response = new AskResponse(answer, lang, sources, guardInfo, Disclaimers[lang], Refused: false);

        await _cache.SetAsync(cacheKey, response, ct);

        // Шаг 10: полный лог
        await _log.LogAsync(new QueryLogRecord(question, lang, questionRu,
            answer, lang, sources.Select(s => s.ChunkId).ToArray(),
            topSimilarity, verdict?.Score, verdict?.Grounded, verdict?.Model ?? "skipped",
            false, (int)sw.ElapsedMilliseconds, tokensIn, tokensOut,
            Cost(tokensIn, tokensOut), clientHash, false, null), ct);

        return response;
    }

    private async Task<IReadOnlyList<RetrievedChunk>> RerankOrDegradeAsync(
        string questionRu, IReadOnlyList<RetrievedChunk> hits, CancellationToken ct)
    {
        var reranked = await _reranker.RerankAsync(questionRu,
            hits.Select(h => (h.ChunkId, h.Header + "\n" + h.Text)).ToList(), _opts.RerankTopK, ct);
        if (reranked is null)
            return hits.OrderByDescending(h => h.Similarity).Take(_opts.RerankTopK).ToList();

        var byId = hits.ToDictionary(h => h.ChunkId);
        return reranked.Where(r => byId.ContainsKey(r.Id)).Select(r => byId[r.Id]).ToList();
    }

    private AskResponse BuildRefusal(string lang) =>
        new(RefusalTexts[lang], lang, Array.Empty<SourceRef>(), null, Disclaimers[lang], Refused: true);

    private decimal Cost(int tokensIn, int tokensOut) =>
        (tokensIn * _opts.CostPer1MTokensIn + tokensOut * _opts.CostPer1MTokensOut) / 1_000_000m;

    internal static string Normalize(string s) => s.Trim().ToLowerInvariant();

    /// <summary>
    /// Ответ с фразой из правила 3 промпта БЕЗ единого маркера [N] = LLM не нашёл
    /// ответа во фрагментах (маркер есть → это частичный ответ, не отказ).
    /// </summary>
    internal static bool IsLlmRefusal(string answerRu)
    {
        var lower = answerRu.ToLowerInvariant();
        var hasRefusalPhrase = lower.Contains("нет ответа на этот вопрос") || lower.Contains("не содержат ответа");
        var hasMarkers = System.Text.RegularExpressions.Regex.IsMatch(answerRu, @"\[\d{1,3}\]");
        return hasRefusalPhrase && !hasMarkers;
    }

    private static string Excerpt(string text) =>
        text.Length <= 400 ? text : text[..400].TrimEnd() + "…";

    private static string? ArticleUrl(RetrievedChunk c) =>
        c.SourceUrl is null ? null
        : c.ArticleNumber is null ? c.SourceUrl
        : $"{c.SourceUrl}#st_{c.ArticleNumber.Replace('-', '_')}";

    public static (string Ru, string? DetectedLang) ParseBridgeJson(string llmText)
    {
        // LLM иногда оборачивает JSON в ```json … ``` — вырезаем содержимое между первой { и последней }
        var start = llmText.IndexOf('{');
        var end = llmText.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new FormatException($"Мост вернул не-JSON: {llmText[..Math.Min(80, llmText.Length)]}");
        var json = llmText[start..(end + 1)];
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var ru = doc.RootElement.GetProperty("ru").GetString()
                     ?? throw new FormatException("Мост: поле ru пустое");
            var detected = doc.RootElement.TryGetProperty("detected_lang", out var d) ? d.GetString() : null;
            return (ru, detected);
        }
        catch (System.Text.Json.JsonException)
        {
            // Слабые LLM (qwen на кыргызских вопросах) выдают битый JSON — например,
            // неэкранированную кавычку в значении. Достаём поле ru регуляркой.
            var ruMatch = System.Text.RegularExpressions.Regex.Match(json, "\"ru\"\\s*:\\s*\"([^\"\r\n]+)\"");
            if (!ruMatch.Success)
                throw new FormatException($"Мост вернул неразбираемый JSON: {json[..Math.Min(120, json.Length)]}");
            var langMatch = System.Text.RegularExpressions.Regex.Match(json, "\"detected_lang\"\\s*:\\s*\"(\\w+)\"");
            return (ruMatch.Groups[1].Value, langMatch.Success ? langMatch.Groups[1].Value : null);
        }
    }
}
