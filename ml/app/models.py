"""Ленивая загрузка моделей (ТЗ v2.1 §2: /health мгновенный, модели — по первому запросу).

Guard-инференс перенесён из RagGuard (kyrgyz/model.py, ragguard/model.py):
CrossEncoder с сигмоидой, скоринг пары (context, answer), выше score = более grounded.
KG-модель (XLM-R) обслуживает и кыргызский, и русский: XLM-R мультиязычный,
а EN-модель на ModernBERT кириллицу токенизирует в байтовую кашу.
"""

import threading

from app import config

_lock = threading.Lock()
_reranker = None
_guards: dict[str, object] = {}


def get_reranker():
    global _reranker
    if _reranker is None:
        with _lock:
            if _reranker is None:
                from sentence_transformers.cross_encoder import CrossEncoder
                _reranker = CrossEncoder(config.RERANK_MODEL, max_length=config.RERANK_MAX_LENGTH)
    return _reranker


def _guard_key(lang: str) -> str:
    # ru обслуживается кыргызской XLM-R моделью (мультиязычная база);
    # en — отдельной ModernBERT-моделью, если она включена в GUARD_MODELS
    if lang == "en" and "en" in config.GUARD_MODELS:
        return "en"
    return "kg"


def get_guard(lang: str):
    key = _guard_key(lang)
    if key not in _guards:
        with _lock:
            if key not in _guards:
                import torch
                from sentence_transformers.cross_encoder import CrossEncoder
                path = config.GUARD_MODEL_PATH_EN if key == "en" else config.GUARD_MODEL_PATH_KG
                _guards[key] = CrossEncoder(path, activation_fn=torch.nn.Sigmoid())
    return key, _guards[key]


def loaded_models() -> dict:
    return {
        "reranker": _reranker is not None,
        "guards": sorted(_guards.keys()),
    }
