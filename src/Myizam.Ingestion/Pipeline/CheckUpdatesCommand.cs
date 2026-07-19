using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Myizam.Data;

namespace Myizam.Ingestion.Pipeline;

/// <summary>
/// Команда `check-updates`: для каждого закона из БД спрашивает у API свежую
/// редакцию и сравнивает с проиндексированной. Законы КР правятся часто
/// (у ТК-2025 за год 6 редакций) — это ручной предвестник мониторинга фазы 2.
/// Выход 0 — всё актуально; 2 — есть обновления (удобно для cron/CI).
/// </summary>
public static partial class CheckUpdatesCommand
{
    [GeneratedRegex(@"^(\d{1,2})\s*[./,]\s*(\d{1,2})\s*[./,]\s*(\d{4})")]
    private static partial Regex LeadingDateRx();

    public static async Task<int> RunAsync(CancellationToken ct = default)
    {
        var cs = Environment.GetEnvironmentVariable("DATABASE_URL")
                 ?? "Host=localhost;Port=5432;Database=myizam;Username=myizam;Password=myizam";
        var options = new DbContextOptionsBuilder<MyizamDbContext>().UseNpgsql(cs, o => o.UseVector()).Options;
        await using var db = new MyizamDbContext(options);
        var laws = await db.Laws.OrderBy(l => l.DocumentCode).ToListAsync(ct);
        if (laws.Count == 0)
        {
            Console.Error.WriteLine("В БД нет законов — сначала ingest + embed");
            return 2;
        }

        using var api = new MinjustApiClient();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var outdated = 0;

        foreach (var law in laws)
        {
            var doc = await api.GetDocumentAsync(law.DocumentCode, ct);
            var latest = doc.Editions!
                .Select(e => (Ed: e, Date: ParseDate(e.NameRus)))
                .Where(x => x.Date is not null && x.Date <= today)
                .MaxBy(x => (x.Date, x.Ed.EditionCode));

            var status = doc.Status!.NameRus;
            if (status != "Действует")
            {
                Console.WriteLine($"‼ {law.DocumentCode} {law.Title}: статус изменился — «{status}»!");
                outdated++;
            }
            else if (latest.Ed is null)
            {
                Console.WriteLine($"? {law.DocumentCode} {law.Title}: не удалось определить свежую редакцию");
            }
            else if (latest.Ed.Id != law.EditionId)
            {
                Console.WriteLine($"● {law.DocumentCode} {law.Title}: НОВАЯ редакция {latest.Date:dd.MM.yyyy} (id={latest.Ed.Id}), в базе — {law.EditionDate:dd.MM.yyyy} (id={law.EditionId})");
                outdated++;
            }
            else
            {
                Console.WriteLine($"✓ {law.DocumentCode} {law.Title}: актуально ({law.EditionDate:dd.MM.yyyy})");
            }
        }

        Console.WriteLine(outdated == 0
            ? "\nВсе законы актуальны."
            : $"\nОбновлений: {outdated}. Прогнать: ingest && embed (идемпотентно — пересчитаются только изменённые статьи).");
        return outdated == 0 ? 0 : 2;
    }

    private static DateOnly? ParseDate(string? nameRus)
    {
        var m = LeadingDateRx().Match(nameRus ?? "");
        if (!m.Success) return null;
        return new DateOnly(
            int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
    }
}
