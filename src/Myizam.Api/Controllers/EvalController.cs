using Microsoft.AspNetCore.Mvc;
using Myizam.Core.Interfaces;
using Myizam.Core.Services;

namespace Myizam.Api.Controllers;

/// <summary>
/// Внутренний эндпоинт для eval-скрипта (фаза 4): raw-выдача retrieval до и после
/// rerank тем же кодом, что и боевой пайплайн. Включается только EVAL_ENDPOINTS=true —
/// в проде выключен.
/// </summary>
[ApiController]
[Route("api/eval")]
public sealed class EvalController : ControllerBase
{
    private readonly LanguageService _language;
    private readonly ITranslator _translator;
    private readonly IQuestionEmbedder _embedder;
    private readonly IChunkSearcher _searcher;
    private readonly IRerankerClient _reranker;

    public EvalController(LanguageService language, ITranslator translator, IQuestionEmbedder embedder,
        IChunkSearcher searcher, IRerankerClient reranker)
    {
        _language = language;
        _translator = translator;
        _embedder = embedder;
        _searcher = searcher;
        _reranker = reranker;
    }

    public sealed record RetrievalRequest(string Question);

    [HttpPost("retrieval")]
    public async Task<IActionResult> Retrieval([FromBody] RetrievalRequest req, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("EVAL_ENDPOINTS") != "true")
            return NotFound();

        // Шаги 1–5 пайплайна — тем же кодом
        var lang = _language.Detect(req.Question);
        var questionRu = req.Question;
        if (lang != "ru")
            questionRu = (await _translator.ToRussianAsync(req.Question, lang, ct)).Ru;

        var embedding = await _embedder.EmbedQuestionAsync(questionRu, ct);
        var top20 = await _searcher.SearchAsync(embedding, 20, ct);

        var reranked = await _reranker.RerankAsync(questionRu,
            top20.Select(h => (h.ChunkId, h.Header + "\n" + h.Text)).ToList(), 5, ct);
        var byId = top20.ToDictionary(h => h.ChunkId);
        var top5 = reranked is null
            ? top20.Take(5).ToList()
            : reranked.Where(r => byId.ContainsKey(r.Id)).Select(r => byId[r.Id]).ToList();

        return Ok(new
        {
            lang,
            questionRu,
            rerankerUsed = reranked is not null,
            top20 = top20.Select(h => new { h.ChunkId, h.LawCode, article = h.ArticleNumber, h.Similarity }),
            top5 = top5.Select(h => new { h.ChunkId, h.LawCode, article = h.ArticleNumber }),
        });
    }
}
