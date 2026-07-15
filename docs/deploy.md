# Деплой (фаза 6): пошагово

Состояние: код и Docker-образы готовы и проходят CI; демо не развёрнуто.
Всё ниже — действия в ВАШИХ аккаунтах (создание аккаунтов/оплата — руками).

## Решение №1: LLM и эмбеддинги в проде

| Вариант | Что нужно | Цена | Качество ky |
|---|---|---|---|
| **A. OpenAI** (рекомендация ТЗ) | OPENAI_API_KEY, баланс $5 | ~$0.08 переиндексация + копейки/запрос (gpt-4o-mini) | хорошее |
| B. Хостить Ollama | инстанс с 8+ GB RAM или GPU | от ~$25–50/мес | qwen пишет по-кыргызски плохо — нужен другой чекпойнт |
| C. Демо без генерации | — | $0 | показывать только retrieval (без ответов) — сильно слабее |

Вариант A: в проде задать `EMBEDDING_PROVIDER=openai`, `EMBEDDING_MODEL=text-embedding-3-small`,
`EMBEDDING_DIM=1536` (⚠ новая миграция: размерность зашита в схему → пересоздать БД или
`dotnet ef migrations add`, затем `ingest --from-cache && embed` на прод-БД — минуты),
`CHAT_BASE_URL=https://api.openai.com`, `CHAT_MODEL=gpt-4o-mini`, `OPENAI_API_KEY`.

## Решение №2: guard-веса

`ragguard-kyrgyz-v1` (~1.1 GB) не лежит в git. Варианты доставки в прод:
выложить на HuggingFace Hub (рекомендую: `erjigit7/ragguard-kyrgyz-v1`, публично) и
в ml/Dockerfile прогреть `snapshot_download`, либо примонтировать диск и залить вручную.

## Backend: Render (готов render.yaml) или Railway

1. render.com → New → Blueprint → выбрать репо `erjigit7/myizam` → применить `render.yaml`.
2. Задать секреты: `CLIENT_HASH_SALT` (случайная строка), `OPENAI_API_KEY` (вариант A).
3. ⚠ `DATABASE_URL` Render отдаёт как `postgresql://user:pass@host/db` — приложение ждёт
   Npgsql-формат. Задать вручную: `Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<pass>;SSL Mode=Require`.
4. Прогнать ingest+embed на прод-БД (локально, указав прод-DATABASE_URL в env).
5. Проверить `https://myizam-api.onrender.com/api/health` — три «ok».

## Frontend: Vercel / Cloudflare Pages

1. Импортировать репо, root = `frontend/myizam-web`, build = `npx ng build`,
   output = `dist/myizam-web/browser`.
2. Прокси `/api` → URL API: на Vercel добавить `vercel.json` с rewrite
   `{"source": "/api/:path*", "destination": "https://myizam-api.onrender.com/api/:path*"}`.
3. В API задать `CORS_ORIGINS=https://<прод-домен>`.

## Прод-smoke (§6.6)

5 вопросов с телефона не из домашней сети; счётчик лимита убывает; `/api/health` зелёный;
`SELECT sum(cost_usd) FROM query_log WHERE created_at > now() - interval '1 day'` < $0.30.
