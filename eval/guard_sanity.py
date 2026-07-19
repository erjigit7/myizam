"""Guard sanity (ТЗ v2.1 фаза 4): 5 пар честный/испорченный ответ (подмена числа
или статьи — приём из ragguard) против /guard/check. Ожидание: score испорченных
заметно ниже честных.

Запуск: python eval/guard_sanity.py [--ml http://localhost:8000]
Контексты берутся из реальных чанков data/chunks/*.jsonl.
"""

import argparse
import json
import pathlib

import httpx

ROOT = pathlib.Path(__file__).parent.parent

# (law_file_glob, article, тип порчи, честный ответ, испорченный ответ)
# Два типа порчи: «digit» — подмена числа ЦИФРОЙ (близко к обучающим негативам
# ragguard-kg), «word» — подмена числа/единицы СЛОВОМ (вне обучающего распределения).
PAIRS = [
    ("3-45_*_ru.jsonl", "68", "digit",
     "Основной оплачиваемый ежегодный отпуск составляет 28 календарных дней.",
     "Основной оплачиваемый ежегодный отпуск составляет 45 календарных дней."),
    ("3-45_*_ru.jsonl", "148", "digit",
     "Отпуск по беременности и родам составляет 70 календарных дней до родов и 56 после родов.",
     "Отпуск по беременности и родам составляет 30 календарных дней до родов и 14 после родов."),
    ("3-45_*_ru.jsonl", "108", "digit",
     "Сверхурочная работа оплачивается за первые 2 часа не менее чем в полуторном размере.",
     "Сверхурочная работа оплачивается за первые 6 часов не менее чем в полуторном размере."),
    # честный ответ ЦИФРОЙ при контексте СЛОВАМИ («двенадцати кв.м») — ложная тревога детектора,
    # важный кейс для отчёта: цифра в ответе обязана буквально встречаться в контексте
    ("3-42_*_ru.jsonl", "45", "digit-vs-word-context",
     "Норма жилой площади не может быть менее 12 квадратных метров на одного человека.",
     "Норма жилой площади не может быть менее 25 квадратных метров на одного человека."),
    ("3-45_*_ru.jsonl", "24", "word",
     "Срок испытания при приеме на работу не может превышать трех месяцев.",
     "Срок испытания при приеме на работу не может превышать девяти месяцев."),
    ("3-21_*_ru.jsonl", "86", "word",
     "Алименты на одного ребенка взыскиваются судом в размере одной четверти заработка родителя.",
     "Алименты на одного ребенка взыскиваются судом в размере трех четвертей заработка родителя."),
    ("3-45_*_ru.jsonl", "94", "word",
     "Заработная плата выплачивается не реже одного раза в месяц.",
     "Заработная плата выплачивается не реже одного раза в квартал."),
]


def load_context(glob: str, article: str) -> str:
    file = next((ROOT / "data" / "chunks").glob(glob))
    for line in file.read_text(encoding="utf-8").splitlines():
        c = json.loads(line)
        if c["ArticleNumber"] == article:
            return c["Header"] + "\n" + c["Text"]
    raise LookupError(f"{glob} ст.{article}")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--ml", default="http://localhost:8000")
    args = ap.parse_args()
    client = httpx.Client(base_url=args.ml, timeout=120)

    print(f"{'статья':<12} {'тип':<22} {'честный':>8} {'испорченный':>12}  разница")
    by_kind: dict[str, list[float]] = {}
    for glob, article, kind, honest, corrupt in PAIRS:
        ctx = load_context(glob, article)
        s_honest = client.post("/guard/check", json={"context": ctx, "answer": honest, "lang": "ru"}).json()["score"]
        s_corrupt = client.post("/guard/check", json={"context": ctx, "answer": corrupt, "lang": "ru"}).json()["score"]
        by_kind.setdefault(kind, []).append(s_honest - s_corrupt)
        law = glob.split("_")[0]
        print(f"{law} ст.{article:<5} {kind:<22} {s_honest:>8.4f} {s_corrupt:>12.4f}  {s_honest - s_corrupt:+.4f}")

    for kind, deltas in by_kind.items():
        print(f"\n{kind}: средняя дельта честный−испорченный = {sum(deltas)/len(deltas):+.4f}")
    print("\nИнтерпретация: дельта ≈ +1 — порча уверенно ловится; ≈ 0 при высоких"
          "\nобоих скорах — слепота к порче; ≈ 0 при низких обоих — ложная тревога"
          "\nна честном перефразе (модель строга к неточному цитированию). История"
          "\nверсий и цифры — myizam/docs/eval_report.md.")


if __name__ == "__main__":
    main()
