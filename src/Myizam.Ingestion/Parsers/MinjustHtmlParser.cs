using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace Myizam.Ingestion.Parsers;

/// <summary>
/// Парсер Word-HTML из GetEdition.contentRu → дерево статей.
/// Структура вычленяется по ТЕКСТОВЫМ маркерам («Раздел/Глава/Статья») на
/// нормализованном тексте абзацев; жирность/центрирование не используются
/// (Word кодирует их непредсказуемо). См. docs/ingestion-spec.md §6.
/// </summary>
public sealed partial class MinjustHtmlParser
{
    // Якорь ^ обязателен: отсекает «...в соответствии со статьей 15» внутри текста.
    // Заглавные варианты — реальность экспорта: в ТК-2025 разделы идут «РАЗДЕЛ I. …».
    // Регистр остальных букв не ослабляем: «статья» со строчной в начале абзаца —
    // это перенос строки внутри предложения, не заголовок.
    // У СТАТЬИ после номера ОБЯЗАТЕЛЬНА точка либо конец абзаца: отсекает
    // «Статья 21 настоящего Кодекса при этом не применяется…» в начале абзаца.
    // Цена: заголовок без точки («Статья 82 Заголовок») будет пропущен — это
    // поймает кросс-проверка якорей st_*.
    // Номер может содержать пробелы вокруг дефиса («233 -1» после конвертации
    // надстрочного индекса) — строгая форма получается в NormalizeNumber.
    [GeneratedRegex(@"^(?:Статья|СТАТЬЯ)\s+(\d+(?:\s*-\s*\d+)*)\s*(?:\.\s*(.*))?$")]
    private static partial Regex ArticleRxRu();

    // Для Главы/Раздела точка НЕобязательна: в ГК-I главы идут «Глава 10-1
    // Право собственности…» без точки, и строгое правило теряло всю структуру.
    [GeneratedRegex(@"^(?:Глава|ГЛАВА)\s+(\d+(?:\s*-\s*\d+)*)\s*\.?\s*(.*)$")]
    private static partial Regex ChapterRxRu();

    [GeneratedRegex(@"^(?:Раздел|РАЗДЕЛ)\s+([IVXLC]+|\d+(?:\s*-\s*\d+)*)\s*\.?\s*(.*)$")]
    private static partial Regex SectionRxRu();

    // Кыргызские маркеры — проверено на реальном ТК (lang=kg, 15.07.2026):
    // статьи «1-берене.» (строчная), главы «1-глава.» (строчная!),
    // разделы «I БӨЛҮМ. ЖАЛПЫ ЖОБОЛОР» (номер ПЕРЕД словом, БЕЗ дефиса).
    // См. docs/kg_notes.md.
    [GeneratedRegex(@"^(?:(?:Статья|СТАТЬЯ|Берене|БЕРЕНЕ)\s+(\d+(?:-\d+)*)|(\d+(?:-\d+)*)\s*-\s*(?:берене|Берене|БЕРЕНЕ))\s*(?:\.\s*(.*))?$")]
    private static partial Regex ArticleRxKg();

    [GeneratedRegex(@"^(?:(?:Глава|ГЛАВА|Бап|БАП)\s+(\d+(?:-\d+)*)|(\d+(?:-\d+)*)\s*-\s*(?:глава|Глава|ГЛАВА|бап|Бап|БАП))\s*(?:\.\s*(.*))?$")]
    private static partial Regex ChapterRxKg();

    [GeneratedRegex(@"^(?:(?:Раздел|РАЗДЕЛ|Бөлүм|БӨЛҮМ)\s+([IVXLC]+|\d+)|([IVXLC]+|\d+)\s*-?\s*(?:бөлүм|Бөлүм|БӨЛҮМ|БӨЛYМ))\s*\.?\s*(.*)$")]
    private static partial Regex SectionRxKg();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRx();

    /// <summary>Максимальная длина абзаца-кандидата в заголовок статьи (§6.2).</summary>
    private const int MaxDetachedTitleLength = 200;

    public ParsedLaw Parse(string html, string lang = "ru")
    {
        var (articleRx, chapterRx, sectionRx) = lang == "kg"
            ? (ArticleRxKg(), ChapterRxKg(), SectionRxKg())
            : (ArticleRxRu(), ChapterRxRu(), SectionRxRu());

        var body = ParseBody(html);
        var law = new ParsedLaw();
        // Якоря Word-экспорта <a name=st_21_1> → "21-1" — эталон для кросс-проверки
        foreach (var a in body.QuerySelectorAll("a"))
        {
            var name = a.GetAttribute("name");
            if (name?.StartsWith("st_", StringComparison.Ordinal) == true)
                law.AnchorArticleNumbers.Add(name[3..].Replace('_', '-'));
        }

        var blocks = ExtractBlocks(body);
        // Знаменатель покрытия — ВЕСЬ текст body (после чистки), а не сумма блоков:
        // так ловится текст, потерянный самим извлечением блоков (li, голые div и т.п.)
        law.TotalNormalizedLength = Normalize(body.TextContent).Length;
        law.CapturedLength = blocks.Sum(b => b.Length);

        // Конечный автомат сборки дерева (§6.3)
        string? section = null, chapter = null;
        ArticleNode? article = null;
        var preamble = new List<string>();

        for (var i = 0; i < blocks.Count; i++)
        {
            var text = blocks[i];

            var m = sectionRx.Match(text);
            if (m.Success)
            {
                CloseArticle(law, ref article);
                section = text;
                chapter = null;
                law.SectionHeadings.Add(text);
                continue;
            }

            m = chapterRx.Match(text);
            if (m.Success)
            {
                CloseArticle(law, ref article);
                chapter = text;
                law.ChapterHeadings.Add(text);
                continue;
            }

            m = articleRx.Match(text);
            if (m.Success)
            {
                var number = NormalizeNumber(FirstNonEmptyGroup(m, out var title));

                // Дубль заголовка статьи в самом документе (реальный случай:
                // статья 386 НК дважды целиком) — побеждает последняя копия
                if (article is not null && article.Number == number)
                {
                    law.ParseNotes.Add($"Статья {number}: повторный заголовок — начата заново, побеждает последняя копия");
                    article = null;   // текущую копию выбрасываем
                }
                else
                {
                    CloseArticle(law, ref article);
                }

                article = new ArticleNode
                {
                    Number = number,
                    Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                    SectionHeading = section,
                    ChapterHeading = chapter,
                };

                // Заголовок может быть отдельным следующим абзацем (§6.2):
                // следующий блок не матчится ни одним Rx, короче 200 символов
                // и не выглядит как обычное предложение (не кончается на . ; :)
                if (article.Title is null && i + 1 < blocks.Count)
                {
                    var next = blocks[i + 1];
                    if (next.Length < MaxDetachedTitleLength
                        && !articleRx.IsMatch(next) && !chapterRx.IsMatch(next) && !sectionRx.IsMatch(next)
                        && !next.EndsWith('.') && !next.EndsWith(';') && !next.EndsWith(':'))
                    {
                        article.Title = next;
                        i++;
                    }
                }
                continue;
            }

            if (article is not null)
                article.Paragraphs.Add(text);
            else
                preamble.Add(text);   // до первой статьи — преамбула
        }

        CloseArticle(law, ref article);
        if (preamble.Count > 0)
            law.Preamble = string.Join("\n", preamble);
        return law;
    }

    private static void CloseArticle(ParsedLaw law, ref ArticleNode? article)
    {
        if (article is not null)
        {
            law.Articles.Add(article);
            article = null;
        }
    }

    /// <summary>«233 - 1» → «233-1»: убрать пробелы внутри составного номера.</summary>
    private static string NormalizeNumber(string raw) =>
        raw.Replace(" ", "").Replace("\t", "");

    /// <summary>
    /// В ru-регулярке номер — группа 1; в kg-альтернативах номер может попасть
    /// в группу 1 или 2, заголовок — последняя группа.
    /// </summary>
    private static string FirstNonEmptyGroup(Match m, out string title)
    {
        title = m.Groups[^1].Value;
        for (var g = 1; g < m.Groups.Count - 1; g++)
            if (m.Groups[g].Success && m.Groups[g].Value.Length > 0)
                return m.Groups[g].Value;
        return m.Groups[1].Value;
    }

    [GeneratedRegex(@"<meta[^>]*charset[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex CharsetMetaRx();

    private static IElement ParseBody(string html)
    {
        // contentRu — УЖЕ декодированная строка из JSON, но внутри неё сидит
        // <meta charset=windows-1251> от Word-экспорта. Если его оставить,
        // AngleSharp перекодирует документ по этому meta и текст превращается
        // в кашу. Вырезаем charset-meta до парсинга.
        html = CharsetMetaRx().Replace(html, "");

        var context = BrowsingContext.New(Configuration.Default);
        var doc = context.OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
        var body = doc.Body ?? throw new InvalidOperationException("HTML без <body>");

        // Предобработка (§6.1): убрать стили, комментарии, картинки, mso-огрызки
        foreach (var el in body.QuerySelectorAll("style, script, img, o\\:p").ToList())
            el.Remove();
        foreach (var node in body.Descendants().OfType<IComment>().ToList())
            node.Remove();

        // Надстрочные индексы: «Статья 21<sup>1</sup>» — это вставная статья 21-1,
        // «частью 2<sup>1</sup>» — часть 2-1. Без замены TextContent даёт «211»,
        // что сталкивается с настоящей статьёй 211 (реальный случай в УК 3-38).
        // Точка бывает ВНУТРИ sup: «Статья 317<sup>1.</sup>» (реальный случай КоП 3-36).
        foreach (var sup in body.QuerySelectorAll("sup").ToList())
        {
            var t = sup.TextContent.Trim();
            var digits = t.TrimEnd('.');
            if (digits.Length > 0 && digits.All(char.IsDigit))
                sup.TextContent = "-" + t;
        }
        return body;
    }

    /// <summary>
    /// Блочные элементы в порядке документа. Таблицы конвертируются построчно,
    /// ячейки через « | » (§6.1.4) — тарифные таблицы должны попасть в текст статьи.
    /// </summary>
    private static List<string> ExtractBlocks(IElement body)
    {
        var blocks = new List<string>();
        foreach (var el in body.QuerySelectorAll("p, h1, h2, h3, h4, h5, h6, li, table"))
        {
            var insideTable = el.Ancestors().OfType<IElement>()
                .Any(a => a.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase));

            if (el.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
            {
                if (insideTable) continue;   // вложенные таблицы уже в тексте внешней
                foreach (var row in el.QuerySelectorAll("tr"))
                {
                    var cells = row.QuerySelectorAll("td, th")
                        .Select(c => Normalize(c.TextContent))
                        .Where(c => c.Length > 0);
                    var line = string.Join(" | ", cells);
                    if (line.Length > 0) blocks.Add(line);
                }
                continue;
            }

            if (insideTable) continue;   // текст ячеек уже собран построчно

            // li с вложенными p не берём целиком — эти p извлекутся сами (без дублей)
            if (el.TagName.Equals("LI", StringComparison.OrdinalIgnoreCase)
                && el.QuerySelector("p") is not null)
                continue;

            var text = Normalize(el.TextContent);
            if (text.Length > 0) blocks.Add(text);
        }
        return blocks;
    }

    /// <summary>Нормализация текста блока (§6.1.3): nbsp → пробел, мягкие переносы — удалить, схлопнуть пробелы.</summary>
    public static string Normalize(string raw)
    {
        var s = raw.Replace('\u00A0', ' ').Replace("\u00AD", "");
        // \u042E\u043D\u0438\u043A\u043E\u0434\u043D\u044B\u0435 \u043D\u0430\u0434\u0441\u0442\u0440\u043E\u0447\u043D\u044B\u0435 \u0446\u0438\u0444\u0440\u044B \u043F\u043E\u0441\u043B\u0435 \u0446\u0438\u0444\u0440\u044B: \u00AB\u0421\u0442\u0430\u0442\u044C\u044F 240\u00B9\u00BB (\u0441\u0443\u0449\u043D\u043E\u0441\u0442\u044C &sup1;
        // \u0432 \u0423\u041A 3-38) \u2192 \u00AB240-1\u00BB. \u041A\u043E\u043D\u0442\u0435\u043A\u0441\u0442 \u00AB\u043F\u043E\u0441\u043B\u0435 \u0446\u0438\u0444\u0440\u044B\u00BB \u0437\u0430\u0449\u0438\u0449\u0430\u0435\u0442 \u00AB\u043C\u00B2\u00BB \u0432 \u0442\u0430\u0431\u043B\u0438\u0446\u0430\u0445.
        s = SuperscriptDigitsRx().Replace(s,
            m => "-" + string.Concat(m.Value.Select(ch => SuperscriptMap.GetValueOrDefault(ch, ch))));
        s = MultiSpaceRx().Replace(s.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), " ");
        s = s.Trim();
        // \u041A\u0438\u0440\u0438\u043B\u043B\u0438\u0446\u0430 \u0432\u043C\u0435\u0441\u0442\u043E \u0446\u0438\u0444\u0440 \u0432 \u043D\u043E\u043C\u0435\u0440\u0435 \u0441\u0442\u0430\u0442\u044C\u0438: \u00AB\u0421\u0442\u0430\u0442\u044C\u044F \u041709-3\u00BB \u0441 \u0431\u0443\u043A\u0432\u043E\u0439 \u00AB\u0417\u00BB
        // (\u0440\u0435\u0430\u043B\u044C\u043D\u0430\u044F \u043E\u043F\u0435\u0447\u0430\u0442\u043A\u0430 \u0432 \u041A\u043E\u041F 3-36). \u041F\u0440\u0430\u0432\u0438\u043C \u0422\u041E\u041B\u042C\u041A\u041E \u0442\u043E\u043A\u0435\u043D \u043D\u043E\u043C\u0435\u0440\u0430 \u043F\u043E\u0441\u043B\u0435 \u00AB\u0421\u0442\u0430\u0442\u044C\u044F\u00BB.
        s = ConfusableNumberRx().Replace(s,
            m => m.Groups[1].Value + m.Groups[2].Value
                .Replace('\u0417', '3').Replace('\u0437', '3').Replace('\u041E', '0').Replace('\u043E', '0'));
        return s;
    }

    [GeneratedRegex(@"(?<=\d)[\u00B9\u00B2\u00B3\u2070\u2074\u2075\u2076\u2077\u2078\u2079]+")]
    private static partial Regex SuperscriptDigitsRx();

    private static readonly Dictionary<char, char> SuperscriptMap = new()
    {
        ['\u00B9'] = '1', ['\u00B2'] = '2', ['\u00B3'] = '3', ['\u2070'] = '0', ['\u2074'] = '4',
        ['\u2075'] = '5', ['\u2076'] = '6', ['\u2077'] = '7', ['\u2078'] = '8', ['\u2079'] = '9',
    };

    [GeneratedRegex(@"^((?:\u0421\u0442\u0430\u0442\u044C\u044F|\u0421\u0422\u0410\u0422\u042C\u042F)\s+)([\d\u0417\u0437\u041E\u043E]+(?:\s*-\s*[\d\u0417\u0437\u041E\u043E]+)*)")]
    private static partial Regex ConfusableNumberRx();
}
