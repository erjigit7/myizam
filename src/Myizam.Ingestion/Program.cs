using System.Text.Json;
using Myizam.Ingestion;
using Myizam.Ingestion.Pipeline;

// CLI: myizam-ingest [ingest|embed] [documentCode...] [--dry-run] [--from-cache] [--lang ru|kg]
// ingest без кодов = прогнать весь config/laws.json (ТЗ §4.6)
// embed — data/chunks/*.jsonl → Postgres + векторизация (ТЗ v2.1 §1.2)

Console.OutputEncoding = System.Text.Encoding.UTF8;

var codes = new List<string>();
bool dryRun = false, fromCache = false, embedMode = false;
var lang = "ru";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "ingest": break;
        case "embed": embedMode = true; break;
        case "--dry-run": dryRun = true; break;
        case "--from-cache": fromCache = true; break;
        case "--lang":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--lang требует значение ru|kg"); return 2; }
            lang = args[++i];
            break;
        case "--help" or "-h":
            Console.WriteLine("Использование: ingest [documentCode...] [--dry-run] [--from-cache] [--lang ru|kg]");
            Console.WriteLine("               embed [--lang ru|kg]   (env: DATABASE_URL, EMBEDDING_PROVIDER=ollama|openai, EMBEDDING_MODEL, EMBEDDING_DIM)");
            return 0;
        default:
            if (args[i].StartsWith('-')) { Console.Error.WriteLine($"Неизвестный флаг {args[i]}"); return 2; }
            codes.Add(args[i]);
            break;
    }
}

var repoRoot = FindRepoRoot();
if (repoRoot is null)
{
    Console.Error.WriteLine("Не найден config/laws.json — запускать из корня репозитория myizam");
    return 2;
}

if (embedMode)
    return await EmbedCommand.RunAsync(repoRoot, lang);

var configPath = Path.Combine(repoRoot, "config", "laws.json");
var allLaws = JsonSerializer.Deserialize<List<LawConfigEntry>>(
    await File.ReadAllTextAsync(configPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

var laws = codes.Count == 0
    ? allLaws
    : allLaws.Where(l => codes.Contains(l.DocumentCode)).ToList();

if (laws.Count == 0)
{
    Console.Error.WriteLine(codes.Count == 0
        ? "config/laws.json пуст"
        : $"Коды [{string.Join(", ", codes)}] не найдены в config/laws.json");
    return 2;
}

Console.WriteLine($"Корпус: {laws.Count} закон(ов), lang={lang}, dry-run={dryRun}, from-cache={fromCache}");

using var api = new MinjustApiClient();
var pipeline = new IngestPipeline(api, new IngestOptions(dryRun, fromCache, lang, repoRoot));

var failed = new List<string>();
foreach (var law in laws)
    if (!await pipeline.IngestAsync(law))
        failed.Add(law.DocumentCode);

Console.WriteLine($"\n=== ИТОГ: {laws.Count - failed.Count}/{laws.Count} успешно ===");
if (failed.Count > 0)
{
    Console.WriteLine($"Провалились: {string.Join(", ", failed)}");
    return 1;
}
return 0;

static string? FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "config", "laws.json")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}
