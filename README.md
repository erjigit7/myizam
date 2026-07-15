# Мыйзам — Ingestion

Пайплайн загрузки законодательства КР из [cbd.minjust.gov.kg](https://cbd.minjust.gov.kg) для RAG-поиска: API → Word-HTML → дерево статей → валидация → чанки JSONL.

## Быстрый старт

```bash
dotnet test                                          # юнит-тесты парсера (fixtures из реального HTML)
dotnet run --project src/Myizam.Ingestion -- ingest --dry-run   # обязательный просмотр перед боевым прогоном
dotnet run --project src/Myizam.Ingestion -- ingest             # боевой прогон: data/chunks/*.jsonl
```

Флаги: `--dry-run` (дерево + 3 случайные статьи, без записи чанков), `--from-cache` (парсить из data/raw без запросов к API), `--lang ru|kg` (kg — фаза 2, не проверено), список кодов (`ingest 3-45 3-38`).

## Корпус (config/laws.json)

| Код | Закон | Статей |
|---|---|---|
| 3-45 | Трудовой кодекс (2025) | 265 |
| 3-21 | Семейный кодекс (2003) | 141 |
| 3-1  | Гражданский кодекс, часть I (1996) | 440 |
| 3-2  | Гражданский кодекс, часть II (1998) | 866 |
| 3-38 | Уголовный кодекс (2021) | 435 |
| 3-39 | Налоговый кодекс (2022) | 491 |
| 3-36 | Кодекс о правонарушениях (2021) | 674 |

Кандидат на добавление: 3-42 Жилищный кодекс (2013, действует).

## Структура

- `src/Myizam.Ingestion` — консольный пайплайн (`MinjustApiClient` → `MinjustHtmlParser` → `LawValidator` → `Chunker`)
- `tests/` — xunit + фикстуры реального Word-HTML
- `config/laws.json` — documentCode + ожидаемое название (защита от перепутанного кода)
- `data/raw/` — кэш ответов API (в .gitignore)
- `data/reports/{code}.md` — отчёты валидации
- `data/chunks/*.jsonl` — чанки: 1 статья = 1 чанк с контекстным заголовком и SHA-256 (идемпотентность эмбеддингов)

Спецификация: [docs/ingestion-spec.md](docs/ingestion-spec.md). Реальные повадки API и Word-HTML, найденные при интеграции: [docs/field-notes.md](docs/field-notes.md) — прочитать перед любыми правками парсера.

## Следующая фаза

Эмбеддинги (батчи ≤100, идемпотентность по ContentHash) и запись в БД (`laws.status`, `laws.edition_date`, `chunks.lang`) — по ТЗ v2.0 §4.4/§5. Вход готов: JSONL в data/chunks.
