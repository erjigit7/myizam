"""Eval golden set против живого API (ТЗ v2.0 §11, план v2.1 фаза 4).

Метрики: retrieval hit@20 (до rerank), hit@5 (после rerank) — таблица «вклад
reranker»; citation accuracy; trap refusal; распределение similarity для
калибровки MIN_SIMILARITY.

Запуск (API с EVAL_ENDPOINTS=true и высоким RATE_LIMIT_DAILY):
    python eval/run_eval.py [--api http://localhost:5080] [--skip-ask]
--skip-ask: только retrieval-метрики (без генерации LLM — быстро и без токенов).
"""

import argparse
import json
import pathlib
import sys
import time

import httpx

HERE = pathlib.Path(__file__).parent


def hit(expected_law: str, expected_articles: list[str], results: list[dict]) -> bool:
    return any(r["lawCode"] == expected_law and r["article"] in expected_articles for r in results)


def post_retry(client: httpx.Client, url: str, payload: dict, attempts: int = 3) -> httpx.Response:
    """Локальный LLM-мост иногда выдаёт невалидный JSON → 500; ретраим."""
    for i in range(attempts):
        r = client.post(url, json=payload)
        if r.status_code < 500:
            return r
        time.sleep(2)
    return r


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--api", default="http://localhost:5080")
    ap.add_argument("--skip-ask", action="store_true")
    args = ap.parse_args()

    golden = json.loads((HERE / "golden_set.json").read_text(encoding="utf-8"))
    client = httpx.Client(base_url=args.api, timeout=300)

    rows = []
    for item in golden:
        row = {"id": item["id"], "lang": item["lang"], "trap": item["trap"]}

        r = post_retry(client, "/api/eval/retrieval", {"question": item["question"]})
        r.raise_for_status()
        data = r.json()
        row["top_similarity"] = max((c["similarity"] for c in data["top20"]), default=0)
        if not item["trap"]:
            row["hit20"] = hit(item["expected_law_code"], item["expected_articles"], data["top20"])
            row["hit5"] = hit(item["expected_law_code"], item["expected_articles"], data["top5"])
            row["reranker_used"] = data["rerankerUsed"]

        if not args.skip_ask:
            r = post_retry(client, "/api/ask", {"question": item["question"]})
            if r.status_code == 429:
                print(f"!! {item['id']}: rate limit — поднимите RATE_LIMIT_DAILY", file=sys.stderr)
                return 2
            r.raise_for_status()
            ask = r.json()
            row["refused"] = ask["refused"]
            if not item["trap"]:
                cited = {(s["law"], s["article"]) for s in ask["sources"]}
                row["cited_expected"] = any(
                    art in {a for (_, a) in cited} for art in item["expected_articles"]
                ) and not ask["refused"]

        rows.append(row)
        status = "trap" if item["trap"] else f"hit20={row.get('hit20')} hit5={row.get('hit5')}"
        print(f"{item['id']} [{item['lang']}] {status} sim={row['top_similarity']:.3f}"
              + (f" refused={row.get('refused')}" if "refused" in row else ""))
        time.sleep(0.3)

    # --- сводка ---
    nontrap = [r for r in rows if not r["trap"]]
    traps = [r for r in rows if r["trap"]]

    def pct(xs):
        return f"{100 * sum(xs) / len(xs):.0f}%" if xs else "n/a"

    print("\n=== СВОДКА ===")
    print(f"hit@20 (до rerank):    {pct([r['hit20'] for r in nontrap])} ({sum(r['hit20'] for r in nontrap)}/{len(nontrap)})")
    print(f"hit@5 (после rerank):  {pct([r['hit5'] for r in nontrap])} ({sum(r['hit5'] for r in nontrap)}/{len(nontrap)})")
    for lng in ("ru", "ky", "en"):
        sub = [r for r in nontrap if r["lang"] == lng]
        if sub:
            print(f"  hit@5 {lng}: {pct([r['hit5'] for r in sub])} ({sum(r['hit5'] for r in sub)}/{len(sub)})")
    if not args.skip_ask:
        print(f"citation accuracy:     {pct([r.get('cited_expected', False) for r in nontrap])}")
        print(f"trap refusal:          {sum(r.get('refused', False) for r in traps)}/{len(traps)}")

    sims_ok = sorted(r["top_similarity"] for r in nontrap)
    sims_trap = sorted(r["top_similarity"] for r in traps)
    print(f"\nsimilarity непустых вопросов: min={sims_ok[0]:.3f} p25={sims_ok[len(sims_ok)//4]:.3f} max={sims_ok[-1]:.3f}")
    print(f"similarity ловушек:           {', '.join(f'{s:.3f}' for s in sims_trap)}")
    print(f"→ калибровка MIN_SIMILARITY: порог между {max(sims_trap):.3f} (max trap) и {sims_ok[0]:.3f} (min честный)")

    (HERE / "last_run.json").write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"\nДетали: eval/last_run.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
