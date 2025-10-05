#!/usr/bin/env bash
set -euo pipefail

echo "[E2E] Starting stack via docker-compose..."
docker-compose up -d --build

echo "[E2E] Waiting for services..."
for i in {1..60}; do
  if curl -sf http://localhost:8085/ocr/health >/dev/null 2>&1 && \
     curl -sf http://localhost:8085/ml/health >/dev/null 2>&1; then
    echo "[E2E] Services healthy"; break
  fi
  sleep 2
done

echo "[E2E] Smoke: spending (should be [])"
curl -s "http://localhost:8085/analytics/spending?from=$(date +%Y-%m-01)&to=$(date +%Y-%m-%d)" || true

echo "[E2E] Smoke: POST /receipts"
tmpimg=$(mktemp /tmp/wib-receipt-XXXXXX.jpg)
dd if=/dev/zero of="$tmpimg" bs=1024 count=1 >/dev/null 2>&1 || true
code=$(curl -s -o /dev/null -w "%{http_code}\n" -F "file=@$tmpimg;type=image/jpeg" http://localhost:8085/receipts)
if [ "$code" != "202" ]; then
  echo "[E2E] Upload failed, HTTP $code"; exit 1
fi
echo "[E2E] Upload accepted"

echo "[E2E] Done. Tail logs with: docker-compose logs -f"

