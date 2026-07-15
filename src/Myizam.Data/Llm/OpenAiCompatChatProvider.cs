using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Myizam.Core.Interfaces;
using Myizam.Core.Models;

namespace Myizam.Data.Llm;

/// <summary>
/// Чат-провайдер через OpenAI-совместимый API (§7, план 3.3):
/// прод — api.openai.com + gpt-4o-mini; разработка — Ollama (http://localhost:11434/v1)
/// с локальным qwen без ключа. Один класс, переключение через env:
/// CHAT_BASE_URL, CHAT_MODEL, OPENAI_API_KEY (для Ollama не нужен).
/// </summary>
public sealed class OpenAiCompatChatProvider : IChatProvider
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OpenAiCompatChatProvider(HttpClient http, string? baseUrl = null, string? model = null, string? apiKey = null)
    {
        _http = http;
        var url = (baseUrl ?? Environment.GetEnvironmentVariable("CHAT_BASE_URL") ?? "https://api.openai.com").TrimEnd('/');
        _http.BaseAddress = new Uri(url);
        _model = model ?? Environment.GetEnvironmentVariable("CHAT_MODEL") ?? "gpt-4o-mini";
        var key = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(key))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    public async Task<ChatResult> CompleteAsync(string systemPrompt, string userMessage,
        double temperature, int maxTokens, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = _model,
            temperature,
            max_tokens = maxTokens,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage },
            },
        }, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<ChatCompletion>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("LLM: пустой ответ");
        var text = body.Choices?.FirstOrDefault()?.Message?.Content
                   ?? throw new InvalidOperationException("LLM: нет choices");
        return new ChatResult(text.Trim(),
            new ChatUsage(body.Usage?.PromptTokens ?? 0, body.Usage?.CompletionTokens ?? 0));
    }

    private sealed record ChatCompletion(
        [property: JsonPropertyName("choices")] List<Choice>? Choices,
        [property: JsonPropertyName("usage")] Usage? Usage);
    private sealed record Choice([property: JsonPropertyName("message")] Message? Message);
    private sealed record Message([property: JsonPropertyName("content")] string? Content);
    private sealed record Usage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
