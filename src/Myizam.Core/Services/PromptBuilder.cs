using System.Text;
using Myizam.Core.Models;

namespace Myizam.Core.Services;

/// <summary>Три промпта ТЗ v2.0 §10 + подстановка фрагментов + обрезка по токен-бюджету.</summary>
public sealed class PromptBuilder
{
    /// <summary>Бюджет контекста в СИМВОЛАХ (~4 символа/токен для кириллицы; §10 «обрезка по бюджету»).
    /// Лишние фрагменты отбрасываются С КОНЦА (наименее релевантные после rerank).</summary>
    public int MaxContextChars { get; init; } = 24_000;   // ~6k токенов на фрагменты

    public const string GenerationSystemPromptHeader = """
        Ты — справочный ассистент по законодательству Кыргызской Республики.
        Твоя единственная база знаний — фрагменты законов ниже. Правила, без исключений:
        1. Используй ТОЛЬКО предоставленные фрагменты. Никаких знаний «по памяти».
        2. Каждое утверждение сопровождай маркером источника [N].
        3. Если фрагменты не содержат ответа — скажи прямо: «В предоставленных статьях нет ответа на этот вопрос». Не додумывай.
        4. Пиши просто, для человека без юридического образования. Термин — сразу пояснение в скобках.
        5. Не оценивай перспективы споров и не давай советов «как лучше поступить» — только что установлено законом.
        6. Отвечай на русском языке. Структура: краткий ответ (1–2 предложения) → подробности → если уместно, «Обратите внимание: …» об исключениях.

        Фрагменты:
        """;

    public const string BridgeSystemPrompt = """
        Переведи вопрос пользователя на русский язык для поиска по базе законов Кыргызской Республики.
        Сохрани юридический смысл максимально точно; бытовые формулировки не «канцеляризируй» сверх необходимого.
        Ответ строго в JSON: {"ru": "<перевод>", "detected_lang": "<ky|ru|en|other>"}
        """;

    public static string TranslateAnswerSystemPrompt(string targetLanguage) => $"""
        Переведи текст на {targetLanguage} язык. Это ответ юридического справочника.
        Требования: маркеры источников [1], [2]... остаются на своих местах без изменений;
        номера статей и названия законов не переводить творчески — точная передача;
        термины — с пояснением, если в целевом языке нет прямого аналога.
        """;

    /// <summary>Системный промпт генерации с фрагментами [1]..[N]. Возвращает промпт и число вошедших фрагментов.</summary>
    public (string SystemPrompt, int IncludedCount) BuildGenerationPrompt(IReadOnlyList<RetrievedChunk> chunks)
    {
        var sb = new StringBuilder(GenerationSystemPromptHeader);
        sb.AppendLine();
        var budget = MaxContextChars;
        var included = 0;
        foreach (var c in chunks)
        {
            var fragment = $"[{included + 1}] {c.Header}\n{c.Text}\n\n";
            if (fragment.Length > budget && included > 0) break;   // с конца, не с начала (§10)
            sb.Append(fragment);
            budget -= fragment.Length;
            included++;
            if (budget <= 0) break;
        }
        return (sb.ToString(), included);
    }

    /// <summary>
    /// Пост-валидация маркеров (§7 шаг 6): [N] вне диапазона 1..sourceCount — «висячие»,
    /// вырезаются из текста; возвращает очищенный текст и список удалённых маркеров.
    /// Сначала нормализуются LLM-вариации маркеров: qwen пишет «[N1]» вместо «[1]»
    /// (наблюдалось вживую), встречается и кириллическая «Н».
    /// </summary>
    public static (string Cleaned, List<int> Dangling) StripDanglingMarkers(string answer, int sourceCount)
    {
        answer = System.Text.RegularExpressions.Regex.Replace(answer, @"\[[NnНн](\d{1,3})\]", "[$1]");
        var dangling = new List<int>();
        var result = System.Text.RegularExpressions.Regex.Replace(answer, @"\[(\d{1,3})\]", m =>
        {
            var n = int.Parse(m.Groups[1].Value);
            if (n >= 1 && n <= sourceCount) return m.Value;
            dangling.Add(n);
            return "";
        });
        return (result, dangling);
    }

    /// <summary>Номера маркеров, реально использованных в ответе, — для панели источников.</summary>
    public static IReadOnlyList<int> UsedMarkers(string answer, int sourceCount) =>
        System.Text.RegularExpressions.Regex.Matches(answer, @"\[(\d{1,3})\]")
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(n => n >= 1 && n <= sourceCount)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
}
