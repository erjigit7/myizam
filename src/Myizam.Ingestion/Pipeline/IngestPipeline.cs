using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Myizam.Ingestion.Chunking;
using Myizam.Ingestion.Parsers;
using Myizam.Ingestion.Validation;

namespace Myizam.Ingestion.Pipeline;

public sealed record LawConfigEntry(string DocumentCode, string ExpectedTitleContains);

public sealed record IngestOptions(bool DryRun, bool FromCache, string Lang, string RepoRoot);

/// <summary>Пайплайн ingest по одному закону (docs/ingestion-spec.md §5).</summary>
public sealed partial class IngestPipeline
{
    // Разделители дат в nameRus редакций гуляют: «23.06.2026», «20/06/2005»,
    // «21,06,2018», «02 .12.2024» — реальные случаи из Семейного и Гражданского
    [GeneratedRegex(@"^(\d{1,2})\s*[./,]\s*(\d{1,2})\s*[./,]\s*(\d{4})")]
    private static partial Regex LeadingDateRx();

    // Короткое название закона — в кавычках внутри официального nameRus:
    // Кодекс КР от 23.01.2025 № 23 "Трудовой кодекс Кыргызской Республики"
    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex QuotedTitleRx();

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly MinjustApiClient _api;
    private readonly MinjustHtmlParser _parser = new();
    private readonly IngestOptions _opts;

    public IngestPipeline(MinjustApiClient api, IngestOptions opts)
    {
        _api = api;
        _opts = opts;
    }

    private string RawDir => Path.Combine(_opts.RepoRoot, "data", "raw");
    private string ReportsDir => Path.Combine(_opts.RepoRoot, "data", "reports");
    private string ChunksDir => Path.Combine(_opts.RepoRoot, "data", "chunks");

    /// <returns>true — успех; false — закон не прошёл проверки (детали уже в консоли).</returns>
    public async Task<bool> IngestAsync(LawConfigEntry law, CancellationToken ct = default)
    {
        Console.WriteLine($"\n=== {law.DocumentCode} (ожидаем: {law.ExpectedTitleContains}) ===");
        try
        {
            // 1. Метаданные документа
            var doc = await GetDocumentCachedAsync(law.DocumentCode, ct);

            // 2. Проверки fail-fast
            var status = doc.Status!;
            if (status.NameRus != "Действует")
                throw new InvalidOperationException($"документ {law.DocumentCode}: статус «{status.NameRus}» (код {status.Code}) — не «Действует»");
            if (doc.NameRus is null || !doc.NameRus.Contains(law.ExpectedTitleContains, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"документ {law.DocumentCode}: название «{doc.NameRus}» не содержит «{law.ExpectedTitleContains}» — перепутан код?");

            // 3. Актуальная редакция
            var today = DateOnly.FromDateTime(DateTime.Now);
            var dated = new List<(EditionInfo Ed, DateOnly Date)>();
            foreach (var ed in doc.Editions!)
            {
                var m = LeadingDateRx().Match(ed.NameRus ?? "");
                if (!m.Success)
                    throw new InvalidOperationException($"редакция id={ed.Id}: nameRus «{ed.NameRus}» не начинается с даты dd.MM.yyyy");
                dated.Add((ed, new DateOnly(
                    int.Parse(m.Groups[3].Value),
                    int.Parse(m.Groups[2].Value),
                    int.Parse(m.Groups[1].Value))));
            }
            foreach (var (ed, date) in dated.Where(x => x.Date > today))
                Console.WriteLine($"  ⚠ будущая редакция id={ed.Id} от {date:dd.MM.yyyy} — пропущена");
            var current = dated.Where(x => x.Date <= today).MaxBy(x => (x.Date, x.Ed.EditionCode));
            if (current.Ed is null)
                throw new InvalidOperationException($"документ {law.DocumentCode}: нет ни одной вступившей редакции");
            Console.WriteLine($"  редакция: id={current.Ed.Id} от {current.Date:dd.MM.yyyy} (всего {doc.Editions.Count})");

            // 4-5. Текст редакции (+ дисковый кэш)
            var html = await GetEditionHtmlCachedAsync(law.DocumentCode, current.Ed.Id, ct);

            // 6. Парсинг
            var parsed = _parser.Parse(html, _opts.Lang);

            // 7. Валидация + отчёт
            var lawTitle = ExtractShortTitle(doc.NameRus);
            var validation = LawValidator.Validate(parsed, lawTitle, law.DocumentCode);
            Directory.CreateDirectory(ReportsDir);
            await File.WriteAllTextAsync(Path.Combine(ReportsDir, $"{law.DocumentCode}.md"), validation.ReportMarkdown, ct);

            foreach (var w in validation.Warnings) Console.WriteLine($"  ⚠ {w}");
            foreach (var e in validation.Errors) Console.WriteLine($"  ❌ {e}");
            Console.WriteLine($"  статей: {parsed.Articles.Count}, разделов: {parsed.SectionHeadings.Count}, глав: {parsed.ChapterHeadings.Count}");

            if (_opts.DryRun)
                PrintDryRun(parsed);

            if (!validation.Ok)
            {
                Console.WriteLine($"  СТОП: валидация не пройдена, отчёт: data/reports/{law.DocumentCode}.md");
                return false;
            }

            if (_opts.DryRun)
            {
                Console.WriteLine("  dry-run: чанки не пишем");
                return true;
            }

            // 8. Чанкинг → data/chunks/*.jsonl (шаг 9 — эмбеддинги+БД — отдельная фаза)
            var meta = new Chunker.LawMeta(
                law.DocumentCode, lawTitle, status.NameRus, status.Code,
                current.Date.ToString("yyyy-MM-dd"), current.Ed.Id, _opts.Lang);
            var chunks = Chunker.Build(parsed, meta);
            Directory.CreateDirectory(ChunksDir);
            var chunksPath = Path.Combine(ChunksDir, $"{law.DocumentCode}_{current.Ed.Id}_{_opts.Lang}.jsonl");
            await using (var sw = new StreamWriter(chunksPath))
                foreach (var c in chunks)
                    await sw.WriteLineAsync(JsonSerializer.Serialize(c, JsonOut));
            Console.WriteLine($"  чанков: {chunks.Count} → {Path.GetRelativePath(_opts.RepoRoot, chunksPath)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ ПРОВАЛ: {ex.Message}");
            return false;
        }
    }

    private async Task<GetDocumentResponse> GetDocumentCachedAsync(string code, CancellationToken ct)
    {
        Directory.CreateDirectory(RawDir);
        var cachePath = Path.Combine(RawDir, $"{code}_document.json");
        if (_opts.FromCache && File.Exists(cachePath))
        {
            Console.WriteLine("  метаданные: из кэша");
            return JsonSerializer.Deserialize<GetDocumentResponse>(
                       await File.ReadAllTextAsync(cachePath, ct),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException($"битый кэш {cachePath}");
        }
        var doc = await _api.GetDocumentAsync(code, ct);
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(doc, JsonOut), ct);
        return doc;
    }

    private async Task<string> GetEditionHtmlCachedAsync(string code, long editionId, CancellationToken ct)
    {
        Directory.CreateDirectory(RawDir);
        var cachePath = Path.Combine(RawDir, $"{code}_{editionId}_{_opts.Lang}.html");
        if (_opts.FromCache && File.Exists(cachePath))
        {
            Console.WriteLine("  HTML: из кэша");
            return await File.ReadAllTextAsync(cachePath, ct);
        }

        var edition = await _api.GetEditionAsync(editionId, _opts.Lang, ct);
        // ⚠ contentRu содержит текст запрошенного языка, включая lang=kg (легаси API)
        if (string.IsNullOrWhiteSpace(edition.ContentRu))
            throw new InvalidOperationException($"редакция {editionId}: contentRu пуст");

        if (!string.IsNullOrEmpty(edition.DateOfFutureEntry)
            && DateTime.TryParse(edition.DateOfFutureEntry, out var dfe)
            && dfe <= DateTime.Now)
            Console.WriteLine("  ⚠ dateOfFutureEntry уже наступила — возможно, есть более новая вступившая редакция, перепроверить GetDocument");

        await File.WriteAllTextAsync(cachePath, edition.ContentRu, ct);
        return edition.ContentRu;
    }

    internal static string ExtractShortTitle(string? officialName)
    {
        if (officialName is null) return "?";
        var m = QuotedTitleRx().Match(officialName);
        return m.Success ? m.Groups[1].Value : officialName;
    }

    private static void PrintDryRun(ParsedLaw parsed)
    {
        Console.WriteLine("  --- ДЕРЕВО (dry-run) ---");
        foreach (var s in parsed.SectionHeadings) Console.WriteLine($"  {s}");
        Console.WriteLine($"  глав: {parsed.ChapterHeadings.Count}");
        if (parsed.Articles.Count == 0) return;
        Console.WriteLine("  --- 3 СЛУЧАЙНЫЕ СТАТЬИ (обязательный ручной просмотр) ---");
        foreach (var a in parsed.Articles.OrderBy(_ => Random.Shared.Next()).Take(3))
        {
            Console.WriteLine($"\n  ### {a} [{a.SectionHeading} / {a.ChapterHeading}]");
            var text = a.Text;
            Console.WriteLine("  " + (text.Length <= 1500 ? text : text[..1500] + "…").Replace("\n", "\n  "));
        }
    }
}
