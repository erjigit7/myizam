# Launch kit (фаза 8) — тексты для публикации

Черновики, публикуете вы сами. Ссылки на репо/демо подставить после пуша и деплоя.

## CV — раздел Projects (первая строка)

> **Myizam** — legal RAG assistant for Kyrgyzstan (.NET 8 + Python ML sidecar + Angular): 8 codes / 3,444 chunks, retrieval hit@5 89% (ru/en 100%), built-in hallucination check with the first Kyrgyz-language detector. github.com/erjigit7/myizam

Следом: ragguard (hallucination detection, 97.9% on Kyrgyz synthetic benchmark), marketplace-ai-assistant.

## Upwork — «Problems I solve», пятый пункт

> 🔹 **"We want AI features in our .NET app"** — RAG pipelines, LLM integration with grounding checks, pgvector search, graceful ML-service degradation. Built and shipped: Myizam, a trilingual legal assistant with a built-in hallucination detector (link).

Портфолио-кейс: скриншот чата + 3 предложения (проблема → решение → метрики) + live-ссылка.

## LinkedIn-пост (EN)

> There is no legal AI assistant that speaks Kyrgyz. So I built one.
>
> Myizam answers questions about Kyrgyz Republic law in Kyrgyz, Russian, or English — with references to the exact articles of the exact codes (8 codes, 3,400+ articles, current editions).
>
> The part I'm most proud of: every answer is checked by my own hallucination detector before it reaches the user — including the first hallucination detector for the Kyrgyz language (XLM-R, trained on a dataset I had to create from scratch).
>
> Honest engineering note: eval showed the detector currently acts as a digit-consistency checker on the legal domain (catches "28 days" → "45 days" perfectly, misses spelled-out numbers). That's why it ships in shadow mode — and that's the retraining roadmap.
>
> Stack: .NET 8 core + Python ML sidecar + Angular. Fully open source: [repo]
>
> #dotnet #rag #legaltech #kyrgyzstan

## LinkedIn-пост (RU)

> Юридического AI-ассистента на кыргызском языке не существовало. Я его построил.
>
> Мыйзам отвечает на вопросы о законах КР на кыргызском, русском и английском — со ссылками на конкретные статьи конкретных кодексов (8 кодексов, 3400+ статей, актуальные редакции).
>
> Главная фича: каждый ответ до показа проверяется собственным детектором галлюцинаций — включая первый детектор для кыргызского языка.
>
> Честная деталь: eval показал, что на юрдомене детектор пока работает как чекер цифровой консистентности (подмену «28 дней» → «45» ловит идеально, числа словами — нет). Поэтому shadow-режим и план дообучения — прямо в README.
>
> Стек: .NET 8 + Python ML sidecar + Angular. Полностью open source: [ссылка]
>
> #dotnet #rag #legaltech

## Телеграм-чаты разработчиков КР (короткая версия, RU)

> Сделал открытого ассистента по законам КР — Мыйзам: задаёшь вопрос на кыргызском/русском/английском, получаешь ответ простыми словами со ссылками на статьи кодексов (cbd.minjust.gov.kg, актуальные редакции). Каждый ответ проходит проверку детектором галлюцинаций — для кыргызского такой, похоже, первый. Бесплатно, код открыт: [ссылка]. Буду рад фидбеку и реальным вопросам — они помогут откалибровать проверку.

## djinni / hh — строка в профиль

> Пет-проект: Myizam — трёхъязычный legal-RAG для Кыргызстана (.NET 8, pgvector, Python ML sidecar, Angular) со встроенной проверкой галлюцинаций; retrieval hit@5 89%. Код: github.com/erjigit7/myizam

## Чек-лист перед публикацией

- [ ] Репо запушен, публичен, About/topics заполнены
- [ ] Демо задеплоено, ссылка живая с телефона не из домашней сети
- [ ] Скриншоты: чат с кыргызским вопросом, панель источников, /laws (в README и посты)
- [ ] ky-eval перепрогнан на прод-модели, цифры в README обновлены
- [ ] Через 1–2 недели живого трафика: query_log → калибровка порога → GUARD_MODE=warn
