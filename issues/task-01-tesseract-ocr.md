---
labels: [ocr, enhancement, fase-3]
---

# TASK 1: Tesseract OCR Production Configuration

## 🎯 Obiettivo
Configura Tesseract OCR con ottimizzazioni per scontrini italiani nel servizio services/ocr.

## 📋 Requisiti
1. Aggiorna Dockerfile per installare tesseract-ocr-ita e dipendenze ottimizzazione
2. Implementa pre-processing immagini in `services/ocr/preprocessing.py`:
   - Deskew (correzione inclinazione)
   - Denoising (rimozione rumore)
   - Adaptive thresholding
   - Contrast enhancement
3. Modifica `services/ocr/main.py` per usare configurazione ottimizzata:
   - --oem 3 (LSTM + legacy)
   - --psm 6 (assume blocco uniforme di testo)
   - Lingua ita+eng
4. Aggiungi configurazione tramite env vars: `TESSERACT_LANG`, `TESSERACT_PSM`, `TESSERACT_OEM`

## ✅ Criteri di Successo
- [ ] `docker compose build ocr && docker compose up -d ocr`
- [ ] `curl -F "file=@docs/sample_receipt.jpg" http://localhost:8081/extract` restituisce testo con >80% accuratezza
- [ ] `pytest services/ocr/tests/test_preprocessing.py` passa
- [ ] Documentazione aggiornata in README.md sezione "Configurazione OCR/KIE"

## 📁 File da Modificare
- `services/ocr/Dockerfile`
- `services/ocr/preprocessing.py` (nuovo)
- `services/ocr/main.py`
- `services/ocr/tests/test_preprocessing.py` (nuovo)
- `README.md`

## 🏷️ Fase
FASE 3: ML e OCR Enhancement
