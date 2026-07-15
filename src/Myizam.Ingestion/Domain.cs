using System.Text;

namespace Myizam.Ingestion;

/// <summary>Результат парсинга одной редакции закона.</summary>
public sealed class ParsedLaw
{
    public string? Preamble { get; set; }
    public List<ArticleNode> Articles { get; } = new();
    public List<string> SectionHeadings { get; } = new();
    public List<string> ChapterHeadings { get; } = new();

    /// <summary>Длина всего нормализованного текста body — знаменатель покрытия (§7.3).</summary>
    public int TotalNormalizedLength { get; set; }

    /// <summary>Суммарная длина блоков, попавших в извлечение, — числитель покрытия.</summary>
    public int CapturedLength { get; set; }

    /// <summary>Номера статей из якорей Word-экспорта (&lt;a name=st_21_1&gt; → "21-1").
    /// ВНИМАНИЕ: в старых документах якоря НЕПОЛНЫЕ (ГК-I: 80 якорей на 440 статей) — только для предупреждений.</summary>
    public List<string> AnchorArticleNumbers { get; } = new();

    public int AnchorArticleCount => AnchorArticleNumbers.Count;

    /// <summary>Заметки парсера о нештатных ситуациях (дубли заголовков и т.п.).</summary>
    public List<string> ParseNotes { get; } = new();
}

public sealed class ArticleNode
{
    public required string Number { get; init; }   // "82", "82-1", "82-1-1"
    public string? Title { get; set; }
    public string? SectionHeading { get; init; }   // "РАЗДЕЛ I. ОБЩИЕ ПОЛОЖЕНИЯ"
    public string? ChapterHeading { get; init; }   // "Глава 1. Основные положения"
    public List<string> Paragraphs { get; } = new();

    public string Text => string.Join("\n", Paragraphs);

    /// <summary>Целые части номера: "82-1" → [82, 1] — для проверки непрерывности.</summary>
    public int[] NumberParts()
    {
        var parts = Number.Split('-');
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            result[i] = int.Parse(parts[i]);
        return result;
    }

    public override string ToString()
    {
        var sb = new StringBuilder("Статья ").Append(Number);
        if (!string.IsNullOrEmpty(Title)) sb.Append(". ").Append(Title);
        return sb.ToString();
    }
}

/// <summary>Один чанк для индексации (1 статья = 1 чанк; длинные делятся на части).</summary>
public sealed record Chunk(
    string LawCode,
    string LawTitle,
    string Status,
    string StatusCode,
    string EditionDate,      // yyyy-MM-dd
    long EditionId,
    string Lang,
    string? ArticleNumber,   // null для преамбулы
    int Part,                // 1..N (N=1 если статья не делилась)
    int PartCount,
    string Header,           // контекстный заголовок чанка
    string Text,
    string ContentHash,
    // Метаданные для панели источников (ТЗ v2.0 §5/§9): раздельно от header
    string? ArticleTitle = null,
    string? Chapter = null,
    string? Section = null);
