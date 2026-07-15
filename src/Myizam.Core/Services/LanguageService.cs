namespace Myizam.Core.Services;

/// <summary>
/// Эвристика определения языка вопроса (ТЗ v2.0 §7 шаг 1):
/// латиница без кириллицы → en; кыргызские ө/ү/ң → ky; иначе ru.
/// Для коротких кыргызских фраз без специфичных букв эвристика неточна —
/// финальную валидацию делает LLM на шаге 2 (detected_lang), приоритет у LLM.
/// </summary>
public sealed class LanguageService
{
    private static readonly char[] KyrgyzLetters = { 'ө', 'ү', 'ң', 'Ө', 'Ү', 'Ң' };

    public string Detect(string question)
    {
        var hasCyrillic = false;
        var hasLatin = false;
        foreach (var ch in question)
        {
            if (ch is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё') hasCyrillic = true;
            else if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z') hasLatin = true;
            if (KyrgyzLetters.Contains(ch)) return "ky";
        }
        if (hasLatin && !hasCyrillic) return "en";
        return "ru";
    }
}
