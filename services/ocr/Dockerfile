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
# Prebuild wheels for service-specific extras to reuse cache
WORKDIR /app
COPY services/ocr/requirements.txt /app/services/ocr/requirements.txt
COPY docker/requirements.common.txt /tmp/requirements.common.txt
RUN --mount=type=cache,target=/root/.cache/pip pip wheel --wheel-dir=/wheels \
    -r /tmp/requirements.common.txt \
    -r /app/services/ocr/requirements.txt

FROM base AS runtime
WORKDIR /app
COPY services/ocr/requirements.txt /app/services/ocr/requirements.txt
COPY --from=wheels /wheels /wheels
RUN sed -n '1,200p' /app/services/ocr/requirements.txt >/dev/null 2>&1 || true \
    && apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
       tesseract-ocr tesseract-ocr-ita libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/* \
    && pip install --no-index --find-links=/wheels -r /app/services/ocr/requirements.txt

# Copy source
COPY services/shared /app/services/shared
COPY services/ocr /app/services/ocr

# Default detector env (overridable)
ENV OCR_DETECTOR_ENABLED=true \
    RECEIPT_DETECTOR_WEIGHTS=/models/receipt_yolov8n.pt \
    RECEIPT_DETECTOR_CONF=0.25 \
    RECEIPT_DETECTOR_IOU=0.5 \
    RECEIPT_DETECTOR_DEVICE=auto

RUN mkdir -p /models

EXPOSE 8081
CMD ["uvicorn","services.ocr.main:app","--host","0.0.0.0","--port","8081"]
