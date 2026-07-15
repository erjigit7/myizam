FROM python:3.12-slim

WORKDIR /app

# CPU-torch: официальный индекс, без CUDA — образ на гигабайты меньше
RUN pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app ./app

# Кэш HF-моделей — в volume (docker-compose), чтобы не тянуть mmarco при каждом старте
ENV HF_HOME=/hf-cache

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
