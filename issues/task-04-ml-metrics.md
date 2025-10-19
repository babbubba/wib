---
labels: [ml, metrics, enhancement, fase-3]
---

# TASK 4: ML Metrics & Performance Tracking

## 🎯 Obiettivo
Implementa sistema di tracking metriche ML (precision, recall, F1-score) con storico performance.

## 📋 Requisiti
1. Crea `services/ml/metrics.py` con classe MetricsTracker:
   - Calcolo precision/recall/F1 per ProductType e Category
   - Confusion matrix per analisi errori
   - Tracking storico metriche con timestamp
   - Persistenza in `MODEL_DIR/metrics_history.json`
2. Aggiungi endpoint GET /ml/metrics:
   - `/current`: metriche modello corrente
   - `/history`: storico metriche (ultimi 30 giorni)
   - `/comparison`: confronto versioni modello
3. Integrazione in POST /ml/train per calcolare metriche post-training
4. Logging metriche in Redis Streams (app_logs) con level INFO

## ✅ Criteri di Successo
- [ ] `pytest services/ml/tests/test_metrics.py` passa
- [ ] `curl http://localhost:8082/ml/metrics/current` restituisce precision/recall/F1 per entrambi i modelli
- [ ] `docker compose exec ml cat /app/models/metrics_history.json` mostra storico con timestamp
- [ ] Metriche loggate in Redis visibili in dashboard monitoring (`/monitoring`)
- [ ] Documentazione aggiornata in `docs/ML_TRAINING.md` (nuovo)

## 📁 File da Modificare
- `services/ml/metrics.py` (nuovo)
- `services/ml/main.py`
- `services/ml/model_manager.py`
- `services/ml/tests/test_metrics.py` (nuovo)
- `docs/ML_TRAINING.md` (nuovo)

## 🏷️ Fase
FASE 3: ML e OCR Enhancement