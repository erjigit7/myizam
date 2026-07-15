using Myizam.Core.Interfaces;
using Myizam.Core.Models;
using Myizam.Core.Services;
using Xunit;

namespace Myizam.Ingestion.Tests;

public class LanguageServiceTests
{
    private static readonly LanguageService Svc = new();

    [Theory]
    [InlineData("Могут ли меня уволить на больничном?", "ru")]
    [InlineData("Can I be fired while on sick leave?", "en")]
    [InlineData("Ажырашууда балдар ким менен калат?", "ru")]   // короткая кыргызская фраза без ө/ү/ң → эвристика даёт ru, финал за LLM (§7)
    [InlineData("Эмгек өргүүсү канча күн?", "ky")]              // ө/ү → ky
    [InlineData("Балдарга алимент ким төлөйт?", "ky")]
    [InlineData("привет hello", "ru")]                          // смесь: кириллица есть → ru
    public void Detect_heuristic(string question, string expected) =>
        Assert.Equal(expected, Svc.Detect(question));
}

public class PromptBuilderTests
{
    private static RetrievedChunk Chunk(long id, string header, string text) =>
        new(id, "3-45", "ТК", "1", null, header, text, null, 0.7);

    [Fact]
    public void Fragments_are_numbered_in_order()
    {
        var pb = new PromptBuilder();
        var (prompt, count) = pb.BuildGenerationPrompt(new[]
        {
            Chunk(1, "Заголовок А", "Текст А"),
            Chunk(2, "Заголовок Б", "Текст Б"),
        });

        Assert.Equal(2, count);
        Assert.Contains("[1] Заголовок А", prompt);
        Assert.Contains("[2] Заголовок Б", prompt);
        Assert.True(prompt.IndexOf("[1]") < prompt.IndexOf("[2]"));
    }

    [Fact]
    public void Token_budget_drops_fragments_from_the_end()
    {
        var pb = new PromptBuilder { MaxContextChars = 300 };
        var big = new string('я', 200);
        var (prompt, count) = pb.BuildGenerationPrompt(new[]
        {
            Chunk(1, "Первый", big),
            Chunk(2, "Второй", big),
            Chunk(3, "Третий", big),
        });

        Assert.Equal(1, count);                       // отброшены с конца, не с начала (§10)
        Assert.Contains("[1] Первый", prompt);
        Assert.DoesNotContain("Третий", prompt);
    }

    [Fact]
    public void Dangling_markers_are_stripped()
    {
        var (cleaned, dangling) = PromptBuilder.StripDanglingMarkers(
            "Отпуск 28 дней [1], а стаж [7] не важен [2].", sourceCount: 2);

        Assert.Equal("Отпуск 28 дней [1], а стаж  не важен [2].", cleaned);
        Assert.Equal(new[] { 7 }, dangling);
    }

    [Fact]
    public void Llm_marker_variations_are_normalized()
    {
        // qwen2.5 пишет [N1] вместо [1] — реальный кейс с локальной моделью
        var (cleaned, dangling) = PromptBuilder.StripDanglingMarkers("Отпуск 28 дней [N1], и [Н2].", 2);
        Assert.Equal("Отпуск 28 дней [1], и [2].", cleaned);
        Assert.Empty(dangling);
    }

    [Fact]
    public void Used_markers_are_extracted_distinct_sorted()
    {
        var used = PromptBuilder.UsedMarkers("Так [2], и ещё раз [2], и [1].", 5);
        Assert.Equal(new[] { 1, 2 }, used);
    }
}

public class AskServiceTests
{
    private sealed class StubEmbedder : IQuestionEmbedder
    {
        public Task<float[]> EmbedQuestionAsync(string q, CancellationToken ct = default) =>
            Task.FromResult(new float[4]);
    }

    private sealed class StubSearcher(double similarity) : IChunkSearcher
    {
        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(float[] e, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RetrievedChunk>>(Enumerable.Range(1, topK)
                .Select(i => new RetrievedChunk(i, "3-45", "Трудовой кодекс", i.ToString(), $"Статья {i}",
                    $"ТК. Статья {i}", $"Текст статьи {i}", "https://cbd.example/3-45", similarity - i * 0.01))
                .ToList());
    }

    private sealed class StubReranker(bool available) : IRerankerClient
    {
        public Task<IReadOnlyList<RerankResult>?> RerankAsync(string q,
            IReadOnlyList<(long Id, string Text)> c, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RerankResult>?>(available
                ? c.Take(topK).Reverse().Select(x => new RerankResult(x.Id, 5.0)).ToList()
                : null);
    }

    private sealed class StubGuard : IGuardClient
    {
        public Task<GuardVerdict?> CheckAsync(string c, string a, string l, CancellationToken ct = default) =>
            Task.FromResult<GuardVerdict?>(new GuardVerdict(true, 0.95, "ragguard-kg"));
    }

    private sealed class StubChat : IChatProvider
    {
        public Task<ChatResult> CompleteAsync(string sys, string user, double t, int m, CancellationToken ct = default) =>
            Task.FromResult(new ChatResult("Отпуск составляет 28 дней [1]. А также [9].", new ChatUsage(100, 50)));
    }

    private sealed class NoCache : IAnswerCache
    {
        public Task<AskResponse?> GetAsync(string k, CancellationToken ct = default) => Task.FromResult<AskResponse?>(null);
        public Task SetAsync(string k, AskResponse r, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ListLogger : IQueryLogger
    {
        public List<QueryLogRecord> Records { get; } = new();
        public Task LogAsync(QueryLogRecord r, CancellationToken ct = default)
        {
            Records.Add(r);
            return Task.CompletedTask;
        }
    }

    private static AskService Build(double similarity, bool rerankerUp, ListLogger log, double minSimilarity = 0.35) =>
        new(new LanguageService(), new PromptBuilder(), new StubEmbedder(), new StubSearcher(similarity),
            new StubReranker(rerankerUp), new StubGuard(), new StubChat(), new NoCache(), log,
            new AskOptions { MinSimilarity = minSimilarity });

    [Fact]
    public async Task Below_threshold_refuses_without_llm_call()
    {
        var log = new ListLogger();
        var svc = Build(similarity: 0.2, rerankerUp: true, log);

        var resp = await svc.AskAsync("Какой штраф за парковку в Алматы?", "hash", default);

        Assert.True(resp.Refused);
        Assert.Empty(resp.Sources);
        var rec = Assert.Single(log.Records);
        Assert.True(rec.Refused);
        Assert.Equal(0, rec.TokensOut);   // LLM не вызывался
    }

    [Fact]
    public async Task Reranker_down_degrades_to_cosine_top5()
    {
        var log = new ListLogger();
        var svc = Build(similarity: 0.8, rerankerUp: false, log);

        var resp = await svc.AskAsync("Сколько дней отпуск?", "hash", default);

        Assert.False(resp.Refused);
        Assert.NotEmpty(resp.Sources);   // ответ получен несмотря на упавший sidecar
    }

    [Fact]
    public async Task Dangling_marker_is_removed_and_sources_match_used()
    {
        var log = new ListLogger();
        var svc = Build(similarity: 0.8, rerankerUp: true, log);

        var resp = await svc.AskAsync("Сколько дней отпуск?", "hash", default);

        Assert.DoesNotContain("[9]", resp.Answer);   // висячий маркер вырезан
        Assert.Contains("[1]", resp.Answer);
        Assert.Equal(1, Assert.Single(resp.Sources).Marker);
    }

    [Fact]
    public async Task Guard_shadow_logs_score_but_hides_from_response()
    {
        var log = new ListLogger();
        var svc = Build(similarity: 0.8, rerankerUp: true, log);

        var resp = await svc.AskAsync("Сколько дней отпуск?", "hash", default);

        Assert.Null(resp.Guard);                          // shadow: не показывается (§8)
        Assert.Equal(0.95, log.Records.Single().GuardScore);   // но логируется
    }

    [Theory]
    [InlineData("В предоставленных статьях нет ответа на этот вопрос.", true)]
    [InlineData("Фрагменты не содержат ответа.", true)]
    // реальный кейс qwen: отказ + пояснение про Алматы/Казахстан, без маркеров → отказ
    [InlineData("В предоставленных статьях нет ответа на этот вопрос. Обратите внимание: указанные нормы касаются только Кыргызской Республики, а не Алматы (Казахстан).", true)]
    // частичный ответ с маркером → НЕ отказ
    [InlineData("Отпуск составляет 28 дней [1]. В предоставленных статьях нет ответа на вопрос о стаже.", false)]
    public void Llm_refusal_phrase_is_detected(string answer, bool expected) =>
        Assert.Equal(expected, AskService.IsLlmRefusal(answer));

    [Fact]
    public void Bridge_json_parses_with_markdown_fence()
    {
        var (ru, lang) = AskService.ParseBridgeJson("```json\n{\"ru\": \"Сколько отпуск?\", \"detected_lang\": \"ky\"}\n```");
        Assert.Equal("Сколько отпуск?", ru);
        Assert.Equal("ky", lang);
    }

    [Fact]
    public void Bridge_broken_json_falls_back_to_regex()
    {
        // реальный кейс qwen на кыргызском вопросе: неэкранированная кавычка ломает JSON
        var (ru, lang) = AskService.ParseBridgeJson(
            "{\"ru\": \"Какое наказание за кражу?\" уурулук, \"detected_lang\": \"ky\"}");
        Assert.Equal("Какое наказание за кражу?", ru);
        Assert.Equal("ky", lang);
    }
}
