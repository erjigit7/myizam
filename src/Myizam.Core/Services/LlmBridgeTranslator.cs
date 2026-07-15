using Myizam.Core.Interfaces;
using Myizam.Core.Models;

namespace Myizam.Core.Services;

/// <summary>Перевод через чат-LLM по промптам §10 — поведение по умолчанию (шаги 2 и 8 §7).</summary>
public sealed class LlmBridgeTranslator : ITranslator
{
    private readonly IChatProvider _chat;

    public LlmBridgeTranslator(IChatProvider chat) => _chat = chat;

    public async Task<(string Ru, string? DetectedLang, ChatUsage Usage)> ToRussianAsync(
        string question, string heuristicLang, CancellationToken ct = default)
    {
        var result = await _chat.CompleteAsync(PromptBuilder.BridgeSystemPrompt, question,
            temperature: 0, maxTokens: 300, ct);
        var parsed = AskService.ParseBridgeJson(result.Text);
        return (parsed.Ru, parsed.DetectedLang, result.Usage);
    }

    public async Task<(string Text, ChatUsage Usage)> FromRussianAsync(
        string answerRu, string targetLang, CancellationToken ct = default)
    {
        var target = targetLang == "ky" ? "кыргызский" : "английский";
        var result = await _chat.CompleteAsync(PromptBuilder.TranslateAnswerSystemPrompt(target), answerRu,
            temperature: 0, maxTokens: 1000, ct);
        return (result.Text, result.Usage);
    }
}
