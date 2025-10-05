FROM python:3.10-slim AS base
ENV PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_NO_CACHE_DIR=0
WORKDIR /app
# Common deps (shared cache across services)
COPY docker/requirements.common.txt /tmp/requirements.common.txt
RUN --mount=type=cache,target=/root/.cache/pip pip install -r /tmp/requirements.common.txt

FROM python:3.10-slim AS wheels
ENV PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_NO_CACHE_DIR=0
# Prebuild wheels (lightweight set: avoid heavy torch deps)
RUN --mount=type=cache,target=/root/.cache/pip pip wheel --wheel-dir=/wheels \
    scikit-learn==1.5.0 \
    joblib==1.4.2

FROM base AS runtime
WORKDIR /app
COPY --from=wheels /wheels /wheels
RUN --mount=type=cache,target=/root/.cache/pip pip install --no-index --find-links=/wheels \
    scikit-learn==1.5.0 \
    joblib==1.4.2

# Copy source
COPY services/ml /app/services/ml

ENV MODEL_DIR=/app/models
EXPOSE 8082
CMD ["uvicorn","services.ml.main:app","--host","0.0.0.0","--port","8082"]
