"""Замер RAM и латентности моделей sidecar на CPU (ТЗ v2.1 §2) → docs/perf_notes.md.

Запуск:  .venv/Scripts/python.exe scripts/measure_perf.py
Замеряется именно CPU-инференс: это данные для решения о деплое (§6.2) —
хватит ли минимального инстанса без GPU.
"""

import os
import sys
import time

import psutil

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

QUERY = "Могут ли меня уволить без предупреждения во время испытательного срока?"
CANDIDATE = (
    "Статья 41. Расторжение трудового договора по инициативе работодателя. "
    "Трудовой договор может быть расторгнут работодателем в случаях ликвидации организации, "
    "сокращения численности или штата работников, несоответствия работника занимаемой должности "
    "вследствие недостаточной квалификации, подтвержденной результатами аттестации." * 2
)
CONTEXT = CANDIDATE
ANSWER = "Работодатель может расторгнуть трудовой договор при сокращении штата с предупреждением работника."


def rss_mb() -> float:
    return psutil.Process().memory_info().rss / 1024 / 1024


def measure(label: str, fn, warmup: int = 1, runs: int = 5) -> float:
    for _ in range(warmup):
        fn()
    start = time.perf_counter()
    for _ in range(runs):
        fn()
    ms = (time.perf_counter() - start) / runs * 1000
    print(f"{label}: {ms:.0f} ms (среднее из {runs}, после {warmup} прогрева)")
    return ms


def main() -> None:
    os.environ.setdefault("GUARD_MODEL_PATH_KG", "C:/Users/Admin20/Projects/RagGuard/models/ragguard-kyrgyz-v1")
    base = rss_mb()
    print(f"RSS до загрузки: {base:.0f} MB")

    from app import models

    t = time.perf_counter()
    reranker = models.get_reranker()
    print(f"reranker загружен за {time.perf_counter() - t:.1f} с, RSS: {rss_mb():.0f} MB (+{rss_mb() - base:.0f})")
    after_rerank = rss_mb()

    pairs20 = [(QUERY, f"{CANDIDATE} вариант {i}") for i in range(20)]
    rerank_ms = measure("/rerank, 20 пар", lambda: reranker.predict(pairs20))

    t = time.perf_counter()
    _, guard = models.get_guard("kg")
    print(f"guard-kg загружен за {time.perf_counter() - t:.1f} с, RSS: {rss_mb():.0f} MB (+{rss_mb() - after_rerank:.0f})")

    guard_ms = measure("/guard/check, 1 пара", lambda: guard.predict([(CONTEXT, ANSWER)]))

    print("\n--- markdown для docs/perf_notes.md ---")
    print(f"| Метрика | Значение (CPU) |")
    print(f"|---|---|")
    print(f"| RSS процесса после reranker + guard-kg | {rss_mb():.0f} MB |")
    print(f"| /rerank: 20 кандидатов | {rerank_ms:.0f} ms |")
    print(f"| /guard/check: 1 пара | {guard_ms:.0f} ms |")


if __name__ == "__main__":
    main()
