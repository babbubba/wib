# 🗺️ ROADMAP - WIB Refactoring Branch

**Branch**: `warp-refactor`  
**Iniziato**: 2025-01-12  
**Obiettivo**: Stabilizzare, migliorare e estendere il sistema WIB

---

## ✅ COMPLETATO

### Setup Iniziale
- ✅ Creato branch `warp-refactor`
- ✅ Configurato Docker Compose con nome progetto isolato (`name: warp-refactor`)
- ✅ Analizzato stato del codice e identificato problemi critici

---

## 🎯 ROADMAP DETTAGLIATA

## **FASE 1: 🔧 Stabilizzazione e Bug Fix (1-2 giorni)**

### 1.1 **Pulizia del Workspace** ⚠️ CRITICO
- [x] Commit delle modifiche in corso
- [x] Verifica che tutti i file siano tracciati
- [x] Test smoke dell'ambiente isolato

### 1.2 **Fix Inconsistenze API ML** 🔥 ALTO  
- [x] Analizzare interfaccia `IProductClassifier`
- [x] Uniformare chiamate da tuple a oggetto strutturato
- [x] Aggiornare `ProcessReceiptCommandHandler.cs`
- [x] Test delle modifiche

### 1.3 **Refactoring ReceiptEditController** 🔧 ALTO
- [x] Estrarre metodi privati per logica complessa
- [x] Migliorare formattazione e leggibilità
- [x] Aggiungere validazione input più robusta
- [x] Test controller endpoints

---

## **FASE 2: 🏗️ Architettura e Funzionalità Core (2-3 giorni)**

### 2.1 **Implementazione Product-Line Association** 🎯 CRITICO
- [x] Implementare collegamento `ReceiptLine` → `ProductId`
- [x] Creare `IProductMatcher` interface
- [x] Implementare logica di matching/creazione prodotti
- [x] Test dell'associazione prodotti

### 2.2 **Miglioramento Name Matching** 🎯 MEDIO
- [ ] Migliorare algoritmi fuzzy matching per negozi
- [ ] Implementare normalizzazione brand/catene
- [ ] Cache in memoria per lookup frequenti
- [ ] Test performance matching

### 2.3 **Gestione Errori Unificata** 🛡️ MEDIO
- [ ] Introdurre pattern Result<T> o middleware uniforme
- [ ] Standardizzare error handling nei controller
- [ ] Implementare logging strutturato
- [ ] Test gestione errori

---

## **FASE 3: 🧠 ML e OCR Enhancement (3-4 giorni)**

### 3.1 **OCR/KIE Production Ready** 🚀 ALTO
- [ ] Configurare Tesseract reale con ottimizzazioni italiane
- [ ] Implementare parsing KIE avanzato per layout scontrini
- [ ] Aggiungere pre-processing immagini (deskew, denoise)
- [ ] Test accuratezza OCR

### 3.2 **ML Model Training Pipeline** 🤖 ALTO
- [ ] Dataset management per training/validation
- [ ] Model versioning e rollback
- [ ] Metrics di performance (precision/recall/f1)
- [ ] Auto-retraining schedulato
- [ ] Test pipeline ML

### 3.3 **Feedback Loop Optimization** 📈 MEDIO
- [ ] Active learning per etichettatura
- [ ] Confidence thresholds dinamici
- [ ] A/B testing per modelli
- [ ] Test feedback loop

---

## **FASE 4: 🎨 Frontend e UX (2-3 giorni)**

### 4.1 **Receipt Review UI Enhancement** 💻 MEDIO
- [ ] Drag & drop per riordinare righe
- [ ] Bulk edit per categoria/tipo
- [ ] Anteprima changes prima del save
- [ ] Keyboard shortcuts per power users
- [ ] Test UI/UX

### 4.2 **Mobile Upload Optimization** 📱 MEDIO
- [ ] Progressive Web App (PWA) completa
- [ ] Offline queue per upload
- [ ] Miglioramento compressione immagini
- [ ] Batch upload multipli
- [ ] Test mobile experience

---

## **FASE 5: 🔧 Infrastructure & Performance (1-2 giorni)**

### 5.1 **Database Optimization** 🗄️ MEDIO
- [ ] Indici per Receipt.Date, Store.Name per query analytics
- [ ] Strategia partitioning per grandi volumi
- [ ] Archiving per dati storici
- [ ] Test performance database

### 5.2 **Monitoring & Observability** 📊 BASSO
- [ ] Structured logging con Serilog
- [ ] Metrics Prometheus/Grafana
- [ ] Health checks dettagliati
- [ ] Test monitoring

### 5.3 **Security Hardening** 🔒 BASSO
- [ ] JWT rotazione chiavi, refresh tokens
- [ ] Input validation più rigorosa
- [ ] Rate limiting per prevenire abuse
- [ ] Security audit

---

## 📊 METRICHE DI SUCCESSO

### Stabilità
- [ ] Zero failing tests
- [ ] Clean builds senza warning
- [ ] Deployment automatico funzionante

### Performance  
- [ ] < 2s processing per receipt
- [ ] < 500ms response time API
- [ ] < 30s time-to-review per receipt

### Accuracy
- [ ] > 85% OCR accuracy
- [ ] > 80% ML classification accuracy
- [ ] < 5% false positives in product matching

---

## 📝 LOG ATTIVITÀ

### 2025-01-12
- ✅ Setup branch e ambiente isolato
- ✅ Analisi stato progetto
- ✅ **COMPLETATA**: FASE 1.1 - Pulizia workspace
  - ✅ Commit stabilizzato (commit 3876a03)
  - ✅ Ambiente Docker isolato testato e funzionante
  - ✅ Health checks API/OCR/ML confermati
- ✅ **COMPLETATA**: FASE 1.2 - Fix inconsistenze API ML
  - ✅ Creato MlPredictionResult per API consistente
  - ✅ Aggiornati tutti i punti di utilizzo (IProductClassifier, ProductClassifier, MlController)
  - ✅ Fixati test con stub mancanti (IImageStorage.DeleteAsync, INameMatcher)
  - ✅ Tutti i test passano (8/8)
- ✅ **COMPLETATA**: FASE 1.3 - Refactoring ReceiptEditController
  - ✅ Refactoring da 1 metodo monolitico (211 righe) → 23 metodi specializzati (408 righe)
  - ✅ Metodo Edit principale ridotto a ~35 righe con logica chiara
  - ✅ Fix async/await pattern (.Result anti-pattern eliminato)
  - ✅ Validazione input robusta con controlli numerici e formato
  - ✅ Tutti i test passano (8/8), API health check ok

✨ **FASE 1 COMPLETATA INTERAMENTE!** ✨
✅ Ambiente isolato stabilizzato
✅ API ML inconsistenze risolte  
✅ ReceiptEditController completamente refactorato

🔥 **PRONTO PER FASE 2: Architettura e Funzionalità Core**
- ✅ **COMPLETATA**: FASE 2.1 - Product-Line Association (TODO critico risolto!)
  - ✅ TODO critico ProcessReceiptCommandHandler risolto
  - ✅ IProductMatcher con 4 strategie di matching (Exact/Alias/Similar/New)
  - ✅ ProductMatcher 378 righe con algoritmi Jaccard similarity
  - ✅ Brand extraction e product cleaning automatici
  - ✅ Confidence thresholds configurabili (75% per receipt processing)
  - ✅ DI integrazione completa API + Worker
  - ✅ Tutti test passano (8/8), smoke test confermato

🏆 **MILESTONE RAGGIUNTA: RECEIPT PROCESSING ORA ASSOCIA PRODOTTI!**

🚗 **IN CORSO**: FASE 2.2 - Name Matching Enhancement

---

## 🚨 NOTE E BLOCCHI

_Nessun blocco al momento_

---

## 🎯 PROSSIMI STEP IMMEDIATI

1. **Commit workspace** - Stabilizzare lo stato attuale
2. **Test ambiente** - Verificare che il setup isolato funzioni
3. **Fix API ML** - Risolvere inconsistenze critiche