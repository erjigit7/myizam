"""Тесты эндпоинтов с замоканными моделями (ТЗ v2.1 §2) — без загрузки весов."""

import numpy as np
import pytest
from fastapi.testclient import TestClient

from app import models
from app.main import app

client = TestClient(app)


class FakeCrossEncoder:
    """Скорит пары длиной текста-кандидата — детерминированно и без ML."""

    def __init__(self, scores=None):
        self.scores = scores
        self.calls = []

    def predict(self, pairs):
        self.calls.append(pairs)
        if self.scores is not None:
            return np.array(self.scores[: len(pairs)])
        return np.array([len(p[1]) / 1000.0 for p in pairs])


@pytest.fixture(autouse=True)
def reset_models(monkeypatch):
    monkeypatch.setattr(models, "_reranker", None)
    monkeypatch.setattr(models, "_guards", {})
    yield


def test_health_is_instant_and_does_not_load_models():
    resp = client.get("/health")
    assert resp.status_code == 200
    body = resp.json()
    assert body["status"] == "ok"
    assert body["loaded"] == {"reranker": False, "guards": []}


def test_rerank_returns_top_k_sorted(monkeypatch):
    fake = FakeCrossEncoder(scores=[0.1, 0.9, 0.5])
    monkeypatch.setattr(models, "get_reranker", lambda: fake)

    resp = client.post("/rerank", json={
        "query": "вопрос про увольнение",
        "candidates": [
            {"id": 10, "text": "статья об отпуске"},
            {"id": 20, "text": "статья об увольнении"},
            {"id": 30, "text": "статья о командировках"},
        ],
        "top_k": 2,
    })

    assert resp.status_code == 200
    results = resp.json()["results"]
    assert [r["id"] for r in results] == [20, 30]          # по убыванию score
    assert results[0]["score"] == 0.9
    assert fake.calls[0][0] == ("вопрос про увольнение", "статья об отпуске")


def test_rerank_rejects_more_than_20_candidates():
    resp = client.post("/rerank", json={
        "query": "q",
        "candidates": [{"id": i, "text": "t"} for i in range(21)],
        "top_k": 5,
    })
    assert resp.status_code == 422


def test_guard_check_grounded_and_threshold(monkeypatch):
    fake = FakeCrossEncoder(scores=[0.83])
    monkeypatch.setattr(models, "get_guard", lambda lang: ("kg", fake))

    resp = client.post("/guard/check", json={
        "context": "Статья 68. Отпуск составляет 28 календарных дней.",
        "answer": "Отпуск составляет 28 календарных дней.",
        "lang": "ru",
    })

    assert resp.status_code == 200
    body = resp.json()
    assert body == {"grounded": True, "score": 0.83, "model": "ragguard-kg"}


def test_guard_check_not_grounded(monkeypatch):
    fake = FakeCrossEncoder(scores=[0.12])
    monkeypatch.setattr(models, "get_guard", lambda lang: ("kg", fake))

    resp = client.post("/guard/check", json={
        "context": "Статья 68. Отпуск составляет 28 календарных дней.",
        "answer": "Отпуск составляет 45 календарных дней.",   # подмена числа — приём из ragguard
        "lang": "ky",
    })

    assert resp.json()["grounded"] is False


def test_guard_lang_routing_ru_and_ky_use_kg_model():
    assert models._guard_key("ru") == "kg"
    assert models._guard_key("ky") == "kg"
    assert models._guard_key("kg") == "kg"
    # en уходит на en-модель только если она включена в GUARD_MODELS
    from app import config
    assert models._guard_key("en") == ("en" if "en" in config.GUARD_MODELS else "kg")


@pytest.mark.slow
def test_rerank_with_real_model_ranks_dismissal_above_vacation():
    """Интеграционный (медленный): реальная mmarco-модель, ручной пример из приёмки §2."""
    resp = client.post("/rerank", json={
        "query": "Могут ли меня уволить без предупреждения?",
        "candidates": [
            {"id": 1, "text": "Статья 68. Продолжительность ежегодного оплачиваемого отпуска составляет 28 календарных дней."},
            {"id": 2, "text": "Статья 41. Расторжение трудового договора по инициативе работодателя допускается с предупреждением работника."},
        ],
        "top_k": 2,
    })
    assert resp.status_code == 200
    results = resp.json()["results"]
    assert results[0]["id"] == 2, f"увольнение должно ранжироваться выше отпуска: {results}"
