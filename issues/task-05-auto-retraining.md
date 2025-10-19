---
labels: [ml, automation, enhancement, fase-3]
---

# TASK 5: Auto-Retraining Schedulato con Threshold

## 🎯 Obiettivo
Implementa auto-retraining schedulato dei modelli ML quando si accumulano nuovi esempi oltre threshold.

## 📋 Requisiti
1. Aggiungi scheduler in `services/ml/scheduler.py`:
   - Background task con APScheduler (ogni 6 ore)
   - Check numero nuovi esempi da ultimo training (query DB LabelingEvent)
   - Trigger retraining se > RETRAINING_THRESHOLD (default 50)
   - Backup modello precedente prima di sovrascrivere
2. Configurazione via env vars: `AUTO_RETRAIN_ENABLED`, `RETRAINING_THRESHOLD`, `RETRAIN_SCHEDULE`
3. Notifica successo/fallimento training in Redis logs (level WARNING per failures)
4. Endpoint POST /ml/retrain/trigger per forzare retraining manuale

## ✅ Criteri di Successo
- [ ] `pytest services/ml/tests/test_scheduler.py` passa
- [ ] `docker compose up -d ml` con `AUTO_RETRAIN_ENABLED=true` avvia scheduler
- [ ] Simulazione: aggiungi 60 feedback via POST /ml/feedback, attendi 5 min, verifica retraining triggerato nei logs
- [ ] `curl -X POST http://localhost:8082/ml/retrain/trigger` forza retraining
- [ ] Backup modelli in `.data/models/backups/` con timestamp

## 📁 File da Modificare
- `services/ml/scheduler.py` (nuovo)
- `services/ml/main.py`
- `services/ml/tests/test_scheduler.py` (nuovo)
- `docker-compose.yml` (aggiungi env vars)
- `README.md`

## 🏷️ Fase
FASE 3: ML e OCR Enhancement