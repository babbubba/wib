---
labels: [ocr, kie, enhancement, fase-3]
---

# TASK 2: Enhanced KIE Parser per Layout Scontrini

## 🎯 Obiettivo
Implementa parser KIE avanzato per riconoscere layout scontrini italiani con pattern euristici robusti.

## 📋 Requisiti
1. Crea `services/ocr/kie_parser.py` con classe ReceiptParser:
   - Pattern recognition per store (nomi comuni + partita IVA)
   - Parsing data/ora con date-parser italiano (DD/MM/YYYY, DD-MM-YYYY, formati testuali)
   - Riconoscimento valuta (€, EUR, euro)
   - Estrazione righe prodotto con pattern: "QTY x PRICE", "LABEL   PRICE", "LABEL QTY PRICE"
   - Estrazione totali: TOTALE, SUBTOTALE, IVA, CONTANTI, RESTO
   - Riconoscimento aliquote IVA per riga (4%, 10%, 22%)
2. Gestione fallback per layout non standard
3. Logging dettagliato con confidence score per ogni campo estratto
4. Integrazione in POST /kie endpoint (`services/ocr/main.py`)

## ✅ Criteri di Successo
- [ ] `pytest services/ocr/tests/test_kie_parser.py` passa (almeno 5 receipt samples)
- [ ] Accuracy >85% su campi: store, date, total, currency
- [ ] Accuracy >75% su righe: qty, unitPrice, lineTotal
- [ ] curl test con 3 scontrini diversi (supermercato, bar, benzina) estrae campi corretti
- [ ] `docker compose logs ocr` mostra confidence scores

## 📁 File da Modificare
- `services/ocr/kie_parser.py` (nuovo)
- `services/ocr/main.py`
- `services/ocr/tests/test_kie_parser.py` (nuovo)
- `services/ocr/tests/fixtures/sample_receipts.json` (nuovo, 5+ esempi)

## 🏷️ Fase
FASE 3: ML e OCR Enhancement
