namespace Myizam.Ingestion;

// Модели ответов API cbd.minjust.gov.kg (см. docs/ingestion-spec.md §4).
// Десериализация: System.Text.Json, PropertyNameCaseInsensitive = true.

public sealed record GetDocumentResponse(
    DocumentStatus? Status,
    string? DateAdopted,
    string? DateOfEntry,
    string? NameRus,
    string? NameKyr,
    List<EditionInfo>? Editions);

public sealed record DocumentStatus(string Code, string NameRus, string? NameKyr);
// Действует: Code == "10", NameRus == "Действует"

public sealed record EditionInfo(
    long Id,
    int EditionCode,
    string NameRus,          // начинается с даты редакции "dd.MM.yyyy";
                             // бывает хвост: "14.11.2025 № 257" — дату извлекать regex-ом из начала
    string? TextRusType,     // ".docx" — исходник Word, отсюда и HTML-каша
    string? TextKyrType);

public sealed record GetEditionResponse(
    long Id,
    string? NameRus,
    string? ContentRu,       // HTML; ⚠ ЛЕГАСИ-ЛОВУШКА: при lang=kg здесь КЫРГЫЗСКИЙ текст.
                             // Язык определяется ПАРАМЕТРОМ запроса, не полем ответа.
    string? ContentKg,       // легаси, всегда null — не использовать
    string? DateOfFutureEntry);
