# Stato di Implementazione del Progetto "Where I Buy"

## Panoramica
- **Backend .NET**: API REST `WIB.API`, worker `WIB.Worker` e librerie condivise orchestrano l'acquisizione dello scontrino, l'elaborazione OCR/KIE/ML e la persistenza su PostgreSQL tramite Entity Framework Core e `WibDbContext` (ricevute, negozi, categorie, storici prezzi, eventi di labeling).
- **Servizi Python**: `services/ocr` espone OCR+KIE euristici con FastAPI e supporto opzionale a modelli esterni; `services/ml` fornisce classificazione incrementale (TF-IDF + SGD) e raccolta feedback.
- **Frontend Angular**: `wib-devices` consente upload mobile-first con compressione client-side; `wib-wmc` offre dashboard di analisi, revisione scontrini, gestione code/storage e login JWT.
- **Infrastruttura**: storage immagini su MinIO, coda Redis, orchestrazione Docker Compose con servizi API, worker, database, servizi ML/OCR e proxy nginx.
- **Testing**: suite xUnit (`backend/WIB.Tests`), pytest basilare per OCR; spec Angular presenti ma non eseguite automaticamente qui.

## Backend API (`backend/WIB.API`)
- Gestione upload (`POST /receipts`) con salvataggio immagine su MinIO e accodamento Redis; listing/pending con join su store e righe. Rate limiting, health check, swagger condizionale, migrazioni automatiche, JWT multi-ruolo (`devices` vs `wmc`).
- Recupero dettagli scontrino con mapping store/location, righe, categorie predette e download immagine (`/receipts/{id}`, `/receipts/{id}/image`).
- Modifica scontrini (`POST /receipts/{id}/edit`): upsert store/location, aggiornamento righe, creazione categorie, aggiunta/eliminazione righe e ricalcolo totale.
- API di supporto: ricerca/store create, ricerca/crea categorie, analytics spending e price history, gestione queue (status, reprocess), storage (listing oggetti, reprocess bulk, preview) e proxy verso servizio ML (suggestions/feedback).

## Worker (`backend/WIB.Worker`)
- Esegue `BackgroundService` che consuma da Redis `wib:receipts`, scarica immagine da MinIO e invoca `ProcessReceiptCommandHandler`.
- Il command handler riusa stream OCR, richiama OCR/KIE/ML, costruisce entità domain `Receipt` (store/location, linee con predizioni e confidenza), salva immagine se serve, persiste via `IReceiptStorage` che aggiorna anche storici prezzi.
- TODO aperto: collegamento righe a `ProductId` dopo classificazione/normalizzazione prodotti.

## Servizi Python
- **OCR/KIE** (`services/ocr/main.py`): FastAPI con endpoint `/extract`, `/kie`, `/kie/status`; pre-processing Tesseract, heuristics per parse store/data/righe/totali, caricamento opzionale modelli PP-Structure o Donut via env; health check.
- **ML** (`services/ml/main.py`): gestione modelli persistenti in `MODEL_DIR`, TF-IDF char n-gram + SGDClassifier per tipo e categoria con `partial_fit`; fallback se singola classe; endpoint `/predict`, `/feedback`, `/train`, `/health`.

## Frontend Angular
- **wib-devices**: form upload compressione (canvas) fino 2MB, anteprima, stato progresso, messaggi successo/errore, azioni reset/camera.
- **wib-wmc**: routing con guard e interceptor JWT; login form; navbar con link rapido. Dashboard: filtri data, tabella spending, lista scontrini, review/edit righe con suggerimenti ML e creazione categorie, aggiunta/eliminazione righe, aggiornamento store/location/datetime/currency. Vista coda/storage con refresh periodico, reprocess singolo/bulk, anteprime immagini.

## Persistenza e Infrastruttura
- `MinioImageStorage` crea bucket se assente e salva/recupera stream; `RedisReceiptQueue` con retry e peek/length.
- `ReceiptStorage` salva entità e aggiorna `PriceHistory` per righe con `ProductId` (min unit price per giorno/store).
- Migrazioni EF Core definiscono schema per receipt, store, categorie, prodotti, storici, budgeting e labeling events.

## Testing
- xUnit copre: controller (auth/receipts/analytics), client OCR/KIE, queue Redis (in-memory stub), storage. Python pytest verifica `/health` e `/extract` stub (richiede fixture monkeypatch per Tesseract). Angular spec di base generate.
- Esecuzione test .NET richiede SDK 8/9; non disponibile in container corrente.

## Gap e Passi Successivi
- Associazione `ReceiptLine`→`Product` da completare (`TODO` in `ProcessReceiptCommandHandler`).
- Ampliare test Python (mock OCR) e Angular end-to-end per UI complesse.
- Integrazione reale modelli KIE/ML (caricamento checkpoint, training dataset) e miglioramento suggestion feedback loop.
- Hardening sicurezza: storage credenziali, rotazione JWT key, ruoli granulari, validazione input.
