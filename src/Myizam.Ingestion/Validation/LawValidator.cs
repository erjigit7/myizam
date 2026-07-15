using System.Text;
using System.Text.RegularExpressions;

namespace Myizam.Ingestion.Validation;

public sealed record ValidationResult(bool Ok, List<string> Errors, List<string> Warnings, string ReportMarkdown);

/// <summary>Проверки дерева статей после парсинга (docs/ingestion-spec.md §7).</summary>
public static partial class LawValidator
{
    /// <summary>Минимальная доля извлечённого текста от всего текста body (§7.3).</summary>
    private const double MinCoverage = 0.90;

    [GeneratedRegex(@"^\d+(-\d+)*$")]
    private static partial Regex ArticleAnchorRx();

    public static ValidationResult Validate(ParsedLaw law, string lawTitle, string documentCode, int minPlausibleArticles = 30)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        warnings.AddRange(law.ParseNotes);

        // §7.4: правдоподобное число статей
        if (law.Articles.Count < minPlausibleArticles)
            errors.Add($"Всего {law.Articles.Count} статей (порог {minPlausibleArticles}) — похоже, сломан парсер/regex");

        // §7.2: пустые статьи
        foreach (var a in law.Articles.Where(a => a.Text.Trim().Length == 0))
            errors.Add($"Статья {a.Number} пуста — почти наверняка баг классификации заголовков");

        // §7.1: непрерывность номеров — ПРЕДУПРЕЖДЕНИЯ, не ошибки: реальные кодексы
        // нарушают монотонность (в ГК-I Глава 10-1 со статьями 233-1…233-8 идёт
        // после статьи 244). Каждый случай — подтвердить глазами по сайту.
        for (var i = 1; i < law.Articles.Count; i++)
        {
            var prev = law.Articles[i - 1];
            var cur = law.Articles[i];
            int[] p, c;
            try { p = prev.NumberParts(); c = cur.NumberParts(); }
            catch (FormatException)
            {
                errors.Add($"Не разобрался номер статьи: «{prev.Number}» или «{cur.Number}»");
                continue;
            }

            if (Compare(p, c) == 0)
                errors.Add($"Дубль номера статьи: {prev.Number} (повторные заголовки должны были схлопнуться в парсере)");
            else if (Compare(p, c) > 0)
                warnings.Add($"Номер уменьшился: {prev.Number} → {cur.Number} (вставная глава? ложный матч? — подтвердить по сайту)");
            else if (c[0] - p[0] > 1)
                warnings.Add($"Пропуск в нумерации: {prev.Number} → {cur.Number} (норма для исключённых статей)");
        }

        // §7.3: покрытие — сколько текста body дошло до блоков
        var coverage = law.TotalNormalizedLength > 0 ? (double)law.CapturedLength / law.TotalNormalizedLength : 0;
        if (coverage < MinCoverage)
            errors.Add($"Извлечено {coverage:P1} текста body < {MinCoverage:P0} — парсер теряет разметку (li? голые div?)");

        // Кросс-проверка по якорям <a name=st_N>. Якоря в старых документах
        // НЕПОЛНЫЕ (ГК-I: 80 якорей на 440 статей), поэтому только предупреждения.
        // «Якорь без статьи» — самый ценный сигнал: там, где CBD ставил ссылку,
        // мы статью не распарсили.
        var anchorSet = law.AnchorArticleNumbers.Where(n => ArticleAnchorRx().IsMatch(n)).ToHashSet();
        if (anchorSet.Count > 0)
        {
            var parsedSet = law.Articles.Select(a => a.Number).ToHashSet();
            var missing = anchorSet.Except(parsedSet).ToList();
            if (missing.Count > 0)
                warnings.Add($"Якоря без распарсенной статьи ({missing.Count} шт.): {string.Join(", ", missing.Take(15))}{(missing.Count > 15 ? "…" : "")} — проверить каждую по сайту");
        }

        var report = BuildReport(law, lawTitle, documentCode, coverage, errors, warnings);
        return new ValidationResult(errors.Count == 0, errors, warnings, report);
    }

    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static string BuildReport(ParsedLaw law, string lawTitle, string documentCode,
        double coverage, List<string> errors, List<string> warnings)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Отчёт валидации: {lawTitle} ({documentCode})");
        sb.AppendLine();
        sb.AppendLine($"- Статей: **{law.Articles.Count}** (якорей st_* в HTML: {law.AnchorArticleCount})");
        sb.AppendLine($"- Разделов: {law.SectionHeadings.Count}, глав: {law.ChapterHeadings.Count}");
        sb.AppendLine($"- Преамбула: {(law.Preamble is null ? "нет" : $"{law.Preamble.Length} символов")}");
        sb.AppendLine($"- Извлечено текста: {coverage:P1}");
        sb.AppendLine($"- Итог: {(errors.Count == 0 ? "OK" : "ОШИБКИ")}");
        sb.AppendLine();
        if (errors.Count > 0)
        {
            sb.AppendLine("## Ошибки");
            foreach (var e in errors) sb.AppendLine($"- ❌ {e}");
            sb.AppendLine();
        }
        if (warnings.Count > 0)
        {
            sb.AppendLine("## Предупреждения (подтвердить глазами)");
            foreach (var w in warnings) sb.AppendLine($"- ⚠ {w}");
            sb.AppendLine();
        }
        sb.AppendLine("## Структура");
        foreach (var s in law.SectionHeadings) sb.AppendLine($"- {s}");
        return sb.ToString();
    }
}
