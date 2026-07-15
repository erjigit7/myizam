using System.Security.Cryptography;
using System.Text;

namespace Myizam.Ingestion.Chunking;

/// <summary>
/// Чанкинг: 1 статья = 1 чанк с контекстным заголовком; длинные статьи
/// делятся по абзацам на части (ТЗ §4.4). Преамбула — отдельный чанк.
/// </summary>
public static class Chunker
{
    /// <summary>Максимум символов текста чанка (~1.5–2 тыс. токенов для кириллицы).</summary>
    public const int MaxChunkChars = 6000;

    public sealed record LawMeta(
        string DocumentCode,
        string LawTitle,        // короткое название («Трудовой кодекс Кыргызской Республики»)
        string Status,
        string StatusCode,
        string EditionDate,     // yyyy-MM-dd
        long EditionId,
        string Lang);

    public static List<Chunk> Build(ParsedLaw law, LawMeta meta)
    {
        var chunks = new List<Chunk>();

        if (!string.IsNullOrWhiteSpace(law.Preamble))
            AddChunks(chunks, meta, articleNumber: null,
                header: $"{meta.LawTitle}. Преамбула",
                paragraphs: law.Preamble.Split('\n'));

        foreach (var a in law.Articles)
        {
            var header = new StringBuilder(meta.LawTitle);
            if (a.SectionHeading is not null) header.Append(". ").Append(a.SectionHeading);
            if (a.ChapterHeading is not null) header.Append(". ").Append(a.ChapterHeading);
            header.Append(". Статья ").Append(a.Number);
            if (a.Title is not null) header.Append(". ").Append(a.Title);

            AddChunks(chunks, meta, a.Number, header.ToString(), a.Paragraphs);
        }
        return chunks;
    }

    private static void AddChunks(List<Chunk> chunks, LawMeta meta, string? articleNumber,
        string header, IReadOnlyList<string> paragraphs)
    {
        var parts = SplitByParagraphs(paragraphs, MaxChunkChars);
        for (var i = 0; i < parts.Count; i++)
        {
            var partHeader = parts.Count == 1 ? header : $"{header} (часть {i + 1} из {parts.Count})";
            var text = parts[i];
            chunks.Add(new Chunk(
                meta.DocumentCode, meta.LawTitle, meta.Status, meta.StatusCode,
                meta.EditionDate, meta.EditionId, meta.Lang,
                articleNumber, i + 1, parts.Count, partHeader, text,
                Sha256Hex(partHeader + "\n" + text)));
        }
    }

    /// <summary>Жадная упаковка абзацев в части ≤ maxChars; сверхдлинный абзац режется жёстко.</summary>
    private static List<string> SplitByParagraphs(IReadOnlyList<string> paragraphs, int maxChars)
    {
        var parts = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
        }

        foreach (var p in paragraphs.Where(p => p.Length > 0))
        {
            if (p.Length > maxChars)
            {
                Flush();
                for (var off = 0; off < p.Length; off += maxChars)
                    parts.Add(p.Substring(off, Math.Min(maxChars, p.Length - off)));
                continue;
            }
            if (current.Length > 0 && current.Length + 1 + p.Length > maxChars)
                Flush();
            if (current.Length > 0) current.Append('\n');
            current.Append(p);
        }
        Flush();

        if (parts.Count == 0) parts.Add("");
        return parts;
    }

    /// <summary>Идемпотентность эмбеддингов — по хешу содержимого (ТЗ §4.4).</summary>
    public static string Sha256Hex(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
