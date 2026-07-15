using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Myizam.Core.Interfaces;
using Myizam.Core.Models;

namespace Myizam.Data.Llm;

/// <summary>
/// Кыргызское направление — через дообученный KyrgyzLLM-переводчик
/// (kazllm-legal-translate: QLoRA на параллельном корпусе кодексов КР).
/// Модель обучена на СТРОГОМ completion-формате «Тапшырма: … Текст: … Котормо:»
/// — поэтому Ollama /api/generate с raw-промптом, НЕ chat-шаблон.
/// Английский и прочие языки уходят в fallback (LLM-мост §10).
/// </summary>
public sealed class KyrgyzLlmTranslator : ITranslator
{
    // Формат 1:1 из legal_translate_train.py — менять только синхронно с обучением
    private const string PromptKy2Ru = "Тапшырма: Төмөнкү юридикалык текстти орус тилине так которуу.\nТекст: {0}\nКотормо:";
    private const string PromptRu2Ky = "Тапшырма: Төмөнкү юридикалык текстти кыргыз тилине так которуу.\nТекст: {0}\nКотормо:";

    private readonly HttpClient _http;
    private readonly ITranslator _fallback;
    private readonly string _model;

    public KyrgyzLlmTranslator(HttpClient http, ITranslator fallback, string? model = null)
    {
        _http = http;   // BaseAddress = Ollama, задаётся в DI
        _fallback = fallback;
        _model = model ?? Environment.GetEnvironmentVariable("TRANSLATE_MODEL") ?? "myizam-translator";
    }

    public async Task<(string Ru, string? DetectedLang, ChatUsage Usage)> ToRussianAsync(
        string question, string heuristicLang, CancellationToken ct = default)
    {
        if (heuristicLang != "ky")
            return await _fallback.ToRussianAsync(question, heuristicLang, ct);

        var (text, usage) = await GenerateAsync(string.Format(PromptKy2Ru, question), ct);
        // язык не переопределяем: эвристика уже сказала ky, модель язык не детектит
        return (text, null, usage);
    }

    public async Task<(string Text, ChatUsage Usage)> FromRussianAsync(
        string answerRu, string targetLang, CancellationToken ct = default)
    {
        if (targetLang != "ky")
            return await _fallback.FromRussianAsync(answerRu, targetLang, ct);
        return await GenerateAsync(string.Format(PromptRu2Ky, answerRu), ct);
    }

    private async Task<(string Text, ChatUsage Usage)> GenerateAsync(string prompt, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/generate", new
        {
            model = _model,
            prompt,
            raw = true,          // без chat-шаблона: модель ждёт ровно обучающий формат
            stream = false,
            options = new { temperature = 0.0, num_predict = 1200 },
        }, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("Переводчик: пустой ответ Ollama");
        var text = (body.Response ?? "").Trim();
        if (text.Length == 0)
            throw new InvalidOperationException("Переводчик вернул пустой перевод");
        return (text, new ChatUsage(body.PromptEvalCount ?? 0, body.EvalCount ?? 0));
    }

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);
}
