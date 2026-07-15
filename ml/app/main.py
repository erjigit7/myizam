"""ML-sidecar «Мыйзам» (ТЗ v2.1 фаза 2): rerank кандидатов retrieval + guard-проверка
groundedness ответа (модели из проекта RagGuard).
"""

from fastapi import FastAPI
from pydantic import BaseModel, Field

from app import config, models

app = FastAPI(title="Myizam ML Sidecar", version="1.0")


# --- /health: мгновенный, НЕ трогает модели ---

@app.get("/health")
def health():
    return {"status": "ok", "loaded": models.loaded_models(), "guard_models": config.GUARD_MODELS}


# --- /rerank: до 20 кандидатов -> top_k по кросс-энкодеру ---

class RerankCandidate(BaseModel):
    id: int
    text: str


class RerankRequest(BaseModel):
    query: str
    candidates: list[RerankCandidate] = Field(max_length=20)
    top_k: int = 5


class RerankResult(BaseModel):
    id: int
    score: float


class RerankResponse(BaseModel):
    results: list[RerankResult]
    model: str


@app.post("/rerank", response_model=RerankResponse)
def rerank(req: RerankRequest):
    reranker = models.get_reranker()
    pairs = [(req.query, c.text) for c in req.candidates]
    scores = reranker.predict(pairs)
    ranked = sorted(zip(req.candidates, scores), key=lambda x: float(x[1]), reverse=True)
    return RerankResponse(
        results=[RerankResult(id=c.id, score=round(float(s), 4)) for c, s in ranked[: req.top_k]],
        model=config.RERANK_MODEL,
    )


# --- /guard/check: groundedness пары (context, answer) ---

class GuardRequest(BaseModel):
    context: str
    answer: str
    lang: str = "ru"   # ru|ky|kg|en; ru и ky обслуживает kg-модель (XLM-R)


class GuardResponse(BaseModel):
    grounded: bool
    score: float
    model: str


@app.post("/guard/check", response_model=GuardResponse)
def guard_check(req: GuardRequest):
    key, guard = models.get_guard("en" if req.lang == "en" else "kg")
    score = float(guard.predict([(req.context, req.answer)])[0])
    return GuardResponse(
        grounded=score >= config.GUARD_THRESHOLD,
        score=round(score, 4),
        model=f"ragguard-{key}",
    )
