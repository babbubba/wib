---
labels: [ml, enhancement, fase-3]
---

# TASK 3: ML Dataset Management & Versioning

## 🎯 Obiettivo
Implementa sistema di gestione dataset per training/validation modelli ML con versioning.

## 📋 Requisiti
1. Crea `services/ml/dataset_manager.py` con classe DatasetManager:
   - Load/save dataset da file JSON/JSONL
   - Split train/validation/test (70/15/15)
   - Versioning con hash MD5 del dataset
   - Export esempi etichettati da LabelingEvent (DB)
2. Aggiungi endpoint POST /ml/datasets:
   - `/create`: crea nuovo dataset da esempi DB
   - `/load`: carica dataset esistente per training
   - `/stats`: statistiche (num esempi, distribuzione classi)
3. Integrazione con ModelManager per usare dataset versionati
4. Persistenza in MODEL_DIR con naming: `dataset_v{hash[:8]}.jsonl`

## ✅ Criteri di Successo
- [ ] `pytest services/ml/tests/test_dataset_manager.py` passa
- [ ] `curl -X POST http://localhost:8082/ml/datasets/create` crea dataset da DB
- [ ] `curl http://localhost:8082/ml/datasets/stats` mostra distribuzione classi corretta
- [ ] File `.data/models/dataset_v*.jsonl` creati con format corretto
- [ ] Documentazione aggiornata in README.md sezione "Addestramento ML"

## 📁 File da Modificare
- `services/ml/dataset_manager.py` (nuovo)
- `services/ml/main.py`
- `services/ml/model_manager.py`
- `services/ml/tests/test_dataset_manager.py` (nuovo)
- `README.md`

## 🏷️ Fase
FASE 3: ML e OCR Enhancement