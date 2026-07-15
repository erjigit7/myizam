"""Конфигурация sidecar. Всё через env — контейнер и локальный запуск не различаются кодом."""

import os

RERANK_MODEL = os.getenv("RERANK_MODEL", "cross-encoder/mmarco-mMiniLMv2-L12-H384-v1")
RERANK_MAX_LENGTH = int(os.getenv("RERANK_MAX_LENGTH", "512"))

# Какие guard-модели загружать: "kg" (демо, экономия RAM) или "kg,en" (ТЗ v2.1 §2)
GUARD_MODELS = [m.strip() for m in os.getenv("GUARD_MODELS", "kg").split(",") if m.strip()]

# Пути к весам RagGuard. Локально — соседний проект; в Docker — примонтированный volume.
GUARD_MODEL_PATH_KG = os.getenv("GUARD_MODEL_PATH_KG", "models/ragguard-kyrgyz-v1")
GUARD_MODEL_PATH_EN = os.getenv("GUARD_MODEL_PATH_EN", "models/ragguard-v1")

GUARD_THRESHOLD = float(os.getenv("GUARD_THRESHOLD", "0.5"))
