---
labels: [ml, active-learning, enhancement, fase-3]
---

# TASK 6: Active Learning per Etichettatura Prioritaria

## 🎯 Obiettivo
Implementa sistema di active learning per identificare esempi da etichettare prioritariamente.

## 📋 Requisiti
1. Crea `services/ml/active_learning.py` con classe ActiveLearner:
   - Identifica esempi con bassa confidence (<0.6) e alta incertezza
   - Ranking per uncertainty sampling (entropia predizioni)
   - API per recuperare top-N esempi da etichettare
2. Aggiungi endpoint GET /ml/active-learning/suggestions?limit=20
3. Integrazione in Worker: salva confidence scores in ReceiptLine.PredictionConfidence
4. UI WMC: pagina `/review/priority` che mostra righe da etichettare ordinate per priorità

## ✅ Criteri di Successo
- [ ] `pytest services/ml/tests/test_active_learning.py` passa
- [ ] `curl http://localhost:8082/ml/active-learning/suggestions?limit=10` restituisce esempi con confidence<0.6
- [ ] Worker salva confidence in DB (verifica con `SELECT PredictionConfidence FROM ReceiptLine`)
- [ ] Angular page `/review/priority` mostra lista ordinata con badge confidence
- [ ] Documentazione aggiornata in README.md sezione "Feedback Loop Optimization"

## 📁 File da Modificare
- `services/ml/active_learning.py` (nuovo)
- `services/ml/main.py`
- `services/ml/tests/test_active_learning.py` (nuovo)
- `backend/WIB.Worker/ReceiptProcessor.cs` (salva confidence)
- `frontend/apps/wib-wmc/src/app/pages/review-priority/` (nuovo component)
- `README.md`

## 🏷️ Fase
FASE 3: ML e OCR Enhancement