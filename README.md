# Where I Buy Monorepo

## Avvio Rapido

- Requisiti: Docker Desktop attivo (Linux containers), Node 20.x, .NET SDK 9 (per build locali).
- Avvio stack locale (API, Worker, DB, Redis, MinIO, OCR, ML, Proxy):
  - `docker compose up -d --build`
- Test locali (opzionali):
  - .NET: `dotnet build backend/WIB.sln && dotnet test backend/WIB.Tests/WIB.Tests.csproj`
  - Python: `python -m pytest services/ocr/tests services/ml/tests`
- Frontend DEV (Angular):
  - Devices (upload): `npm install --prefix frontend && npm run start:devices --prefix frontend` → http://localhost:4200
  - WMC (analytics/review): `npm run start:wmc --prefix frontend` → http://localhost:4201
  - Proxy unico per API: http://localhost:8085
  - Nota: il dev proxy Angular (`frontend/proxy.conf.json`) inoltra ora anche `/auth` e `/ml` verso `http://localhost:8080` per supportare login e suggerimenti ML in sviluppo.

**Elaborazione Scontrini (panoramica rapida)**

- Carica con Devices (4200) o `POST /receipts` → l’immagine viene salvata in MinIO e la chiave oggetto viene accodata in Redis (`wib:receipts`).
- Il Worker legge dalla coda, scarica l’immagine da MinIO, fa OCR e KIE, classifica le righe con ML e salva tutto nel DB (metadati scontrino + righe + raw text). 
- L’UI WMC (4201) mostra analytics e permette di inviare feedback per migliorare i suggerimenti ML.

## Obiettivi

- Caricare foto/screenshot di scontrini da app Angular (mobile-first, fotocamera del telefono).
- OCR + parsing totalmente on-prem / offline.
- Normalizzazione prodotti, categoria e tipo commerciale (nome merceologico).
- Tracking prezzi per supermercato e budgeting/uscite mensili.
- Stack: Backend .NET, Frontend Angular 19, DB PostgreSQL, storage MinIO, cache Redis.
- AI locale: OCR (PaddleOCR/Tesseract) + KIE (Key-Info Extraction) e classificazione prodotti (FastAPI ML).

## Architettura Logica

```
[Angular 19 (PWA)] -> [WIB.API (.NET)] -> [Queue] -> [WIB.Worker]
                           |                |-> MinIO (immagini)
                           |                |-> PostgreSQL (dati)
                           |                `-> Redis (coda)
                           |-> OCR/KIE (HTTP)
                           `-> ML (HTTP)

[Worker]:
  1) legge job da Redis (objectKey)
  2) scarica immagine da MinIO
  3) OCR locale + KIE (campi/righe)
  4) Classificazione (Tipo/Categoria) + soglie
  5) Persistenza (Receipt/Lines, Product, PriceHistory)

[ML] (FastAPI):
  - /predict: suggerimenti Tipo/Categoria
  - /feedback: apprendimento incrementale
  - /train: batch train e salvataggio modelli
```

## Modello dati (relazionale)

- Store(Id, Name, Chain, Address, City)
- ProductType(Id, Name, Aliases JSONB) → tipo commerciale / merceologico
- Category(Id, Name, ParentId FK nullable)
- Product(Id, Name, Brand, GTIN nullable, ProductTypeId FK, CategoryId FK nullable)
- ProductAlias(Id, ProductId FK, Alias)
- Receipt(Id, StoreId FK, Date, Total, TaxTotal nullable, Currency, RawText, ImageObjectKey)
- ReceiptLine(Id, ReceiptId FK, ProductId FK nullable, LabelRaw, Qty dec(10,3), UnitPrice dec(10,3), LineTotal dec(10,3), VatRate nullable, PredictedTypeId, PredictedCategoryId, PredictionConfidence)
- PriceHistory(Id, ProductId FK, StoreId FK, Date, UnitPrice)
- BudgetMonth(Id, Year, Month, LimitAmount)
- ExpenseAggregate(Id, Year, Month, StoreId nullable, CategoryId nullable, Amount)
- LabelingEvent(Id, ProductId FK nullable, LabelRaw, PredictedTypeId nullable, PredictedCategoryId nullable, FinalTypeId, FinalCategoryId, Confidence dec(3,2), WhenUtc)

## Compose (locale)

Estratto aggiornato di `docker-compose.yml` (ingresso via proxy `http://localhost:8085`).

```yaml
services:
  api:
    build: ./backend/WIB.API
    environment:
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__Default: Host=db;Database=wib;Username=wib;Password=wib
      Minio__Endpoint: minio:9000
      Minio__AccessKey: wib
      Minio__SecretKey: wibsecret
      Redis__Connection: redis:6379
      Ocr__Endpoint: http://ocr:8081
      Kie__Endpoint: http://ocr:8081
      Ml__Endpoint: http://ml:8082
    ports: ["8080:8080"]
    depends_on: [db]

  worker:
    build: ./backend/WIB.Worker
    environment:
      ConnectionStrings__Default: Host=db;Database=wib;Username=wib;Password=wib
      Minio__Endpoint: minio:9000
      Minio__AccessKey: wib
      Minio__SecretKey: wibsecret
      Redis__Connection: redis:6379
      Ocr__Endpoint: http://ocr:8081
      Kie__Endpoint: http://ocr:8081
      Ml__Endpoint: http://ml:8082
    depends_on: [db]

  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: wib
      POSTGRES_USER: wib
      POSTGRES_PASSWORD: wib

  redis:
    image: redis:7-alpine

  minio:
    image: minio/minio:latest
    environment:
      MINIO_ROOT_USER: wib
      MINIO_ROOT_PASSWORD: wibsecret
    command: server /data --console-address ":9001"
    ports: ["9000:9000", "9001:9001"]

  ocr:
    build: ./services/ocr
    ports: ["8081:8081"]

  ml:
    build: ./services/ml
    ports: ["8082:8082"]
    environment:
      MODEL_DIR: /app/models
      TOP_K: "3"
    volumes:
      - ./.data/models:/app/models

  qdrant:
    image: qdrant/qdrant:latest
    ports: ["6333:6333"]

  proxy:
    image: nginx:alpine
    depends_on: [api, ocr, ml]
    ports: ["8085:80"]
    volumes:
      - ./proxy/nginx.conf:/etc/nginx/conf.d/default.conf:ro
```

## Estrazione Locale (OCR/KIE) e Addestramento

- OCR (locale): Tesseract (default implementato) o PaddleOCR CPU (ITA).
  - Il servizio `services/ocr` estrae testo con Tesseract e applica un parser euristico per ricostruire campi/righe/totali.
  - Per accuratezza avanzata, è possibile abilitare PP-Structure o Donut come indicato sotto.
- KIE (Key-Info Extraction) per campi Store/Data/Valuta/Totali e righe:
  - PP-Structure (PaddleOCR, SER/RE): addestrabile con dataset etichettato (box/relazioni);
  - Donut (NAVER): modello end-to-end (OCR-free) fine-tunabile su JSON target; richiede >100 esempi per risultati stabili.
- Parsing righe: fallback con tokenizer/regex quando il layout non è tabellare.

### Come addestrare KIE (campi + righe)

1) Raccolta dataset
   - 200–1000 scontrini reali (immagine + annotazioni)
   - Annotare: Store (nome/chain), Date/Time, Currency, Totals (subtotal/tax/total), e linee (labelRaw, qty, unitPrice, lineTotal, vatRate) con box.
   - Strumenti: Label Studio/Doccano; convertire nel formato atteso dal modello.

2) PP-Structure (SER/RE)
   - Formato: dataset PaddleOCR (segmentation + entities/relations). Vedi documentazione PP-Structure.
   - Training (esempio):
     - `python tools/train.py -c configs/kie/ser.yml -o Global.save_model_dir=./output/ser`
     - `python tools/train.py -c configs/kie/re.yml  -o Global.save_model_dir=./output/re`
  - Deploy: esporre in `services/ocr` un endpoint `/kie` che carica i pesi e produce JSON nello schema ReceiptDraft.

3) Donut (alternativa)
   - Formato: JSON target (store/datetime/currency/lines/totals) per ogni immagine.
   - Fine-tune con Hugging Face (donut-base) su immagini+JSON.

4) Validazione
   - F1 su campi chiave (store/date/totals), accuratezza su righe (qty/unitPrice/lineTotal).
   - Fallback robusti per formati diffusi (decimal separator, diciture “TOTALE”).

### Riconoscimento Prodotti/Prezzi/Store

- Store: KIE estrae nome/insegna; normalizzare con dizionario catene (lookup su `Store.Chain`), fuzzy match e alias.
- Prezzi/quantità: preferire KIE tabellare; in fallback regex (pattern `qty x unitPrice`, `lineTotal`).
- Etichette prodotto: pulizia (lowercase, stopwords scontrino), alias storici (`ProductAlias`).

### Pipeline OCR/KIE implementata (baseline)

- Endpoint `POST /ocr/extract`: OCR Tesseract (`--oem 3 --psm 6`) → `{ text }`.
- Endpoint `POST /ocr/kie`: parsing euristico del testo per identificare Store, Data/Ora (DD/MM/YYYY), Valuta (€, EUR), Righe (`qty x unit` o `label price`) e Totali (TOTALE/SUBTOTALE/IVA).
- Estrazione Store: nome + address, city, CAP, P.IVA (se presenti nelle prime righe).
- Righe: ove indicata, viene estratta anche l'aliquota IVA per riga (es. "22%"), salvata in `VatRate`.
- Pre‑processing immagini: autocontrast, sharpening e, se disponibile, denoise + adaptive threshold con OpenCV per migliorare la leggibilità.
- Dipendenze container: `tesseract-ocr`, `tesseract-ocr-ita`, `pytesseract`, `Pillow`, `python-dateutil`. PaddleOCR è preinstallato (opzionale, attivabile).

Esempi:
- `curl -F file=@/path/receipt.jpg http://localhost:8081/extract`
- `curl -X POST http://localhost:8081/kie -H 'Content-Type: application/json' -d '{"text":"..."}'`

## Classificazione Predittiva (Categoria & Tipo commerciale)

**Requisiti**

- Apprendimento incrementale dai dati etichettati a mano.
- Suggerimenti top-k con confidence e soglia di “rifiuto” → invio a revisione.

**Approccio ibrido (tutto locale)**

1) Feature testuali da `labelRaw` + brand (opz.)
   - TF-IDF (char 3–5, word 1–2) → input a SGDClassifier/LinearSVM con `partial_fit`.
   - (Opz) embedding semantico + KNN (Qdrant) per candidati.
2) Ensemble: media pesata (Linear + KNN) per predire ProductType e Category separatamente.
3) Online learning: ogni conferma/correzione dell’utente → `/feedback` salva esempio e aggiorna il modello (e/o retrain batch schedulato).

**API ML**

- `POST /predict { labelRaw, brand? } -> { typeCandidates:[{id,name,conf}], categoryCandidates:[...] }`
- `POST /feedback { labelRaw, brand?, finalTypeId, finalCategoryId? }`
- `POST /train` (batch) salva modelli in `MODEL_DIR`.

Proxy Nginx (container `proxy`):
- `/ml/suggestions` e `/ml/feedback` sono esposti via `WIB.API` (ruolo `wmc`).
- Rotte di debug/health generiche per ML/OCR rimangono disponibili come passthrough:
  - `/ml/health` → servizio ML
  - `/ocr/health` → servizio OCR

## Workflow di Elaborazione Scontrini (dettaglio)

- Upload e accodamento
  - UI Devices (4200) invia `multipart/form-data` a `/receipts`.
  - L’API salva l’immagine in MinIO (bucket `receipts`) e inserisce la chiave oggetto nella coda Redis (`wib:receipts`).
  - Verifica MinIO: console su http://localhost:9001 (user: `wib`, pass: `wibsecret`).

- Worker e pipeline
  - Il servizio `WIB.Worker` prende dalla coda e invoca:
    - OCR: `POST /extract` (servizio OCR)
    - KIE: `POST /kie` (estrazione campi e righe strutturate)
    - ML: `POST /predict` per tipo/categoria di ciascuna riga
    - Persistenza: salva `Receipt` con `Lines` nel DB (include: store, data/ora, valuta, totale, tax, testo grezzo, chiave immagine, righe con quantità, prezzi, IVA, predizioni e confidenza)
  - Log Worker: `docker compose logs -f worker`
  - Coda manuale: per forzare la rielaborazione, inserisci una chiave oggetto in Redis:
    - `docker exec -it wib-main-redis-1 redis-cli LPUSH wib:receipts "yyyy/MM/dd/<guid>.jpg"`

- Verifica rapido post-elaborazione
  - Lista: `GET /receipts?take=20` (richiede ruolo `wmc`)
  - Dettaglio: `GET /receipts/{id}` e `GET /receipts/{id}/image`
  - Modifica (nuovo): `POST /receipts/{id}/edit` per store/data/valuta e righe (quantità/prezzi/etichetta, categoria finale)
  - Analytics di spesa: `GET /analytics/spending?from=YYYY-MM-DD&to=YYYY-MM-DD`

### Modifica scontrini (nuovo)

- UI WMC: nella sezione Review/Modifica puoi aggiornare Negozio, Data/Ora, Valuta e ogni riga (Etichetta, Q.tà, Prezzo, Totale, Categoria). Salva con “Salva modifiche”.
- API `POST /receipts/{id}/edit`:
  - Body: `{ storeName?, datetime?, currency?, lines?: [{ index, labelRaw?, qty?, unitPrice?, lineTotal?, vatRate?, finalCategoryId?, finalCategoryName?, productName? }] }`
- Effetto: aggiorna i campi del `Receipt`; per righe con `finalCategory*`, crea/riusa `Category` e `Product`, associandoli alla riga (e `PredictionConfidence=1.0`).
  - Lookup categorie (nuovo): `GET /categories/lookup?name=...` restituisce `{ id, name, exists }` con confronto case‑insensitive e `name` normalizzato (Title Case). Utile per evitare duplicati con maiuscole/minuscole diverse.

## Configurazione OCR/KIE (estrazione campi e righe)

- Stato attuale
  - Il servizio OCR/KIE è eseguibile out‑of‑the‑box in modalità stub (ritorna campi/righe fittizie coerenti) per consentire l’integrazione end‑to‑end.
  - Per l’estrazione reale occorre abilitare un motore KIE e implementare/invocare un’inferenza reale in `services/ocr/main.py` (metodo `KieEngine.infer_image`).

- Opzione A: PP‑Structure (PaddleOCR)
  - Prepara i pesi/config in una cartella locale, ad es. `.data/kie`.
  - Imposta variabili d’ambiente (già montate da `docker-compose.yml`):
    - `KIE_MODEL_DIR=/app/kie_models` (montata da `./.data/kie:/app/kie_models:ro`)
    - facoltative: `PP_STRUCTURE_SER_CFG`, `PP_STRUCTURE_RE_CFG` verso i file `*_infer.yml`.
  - Implementa l’uso di PaddleOCR in `KieEngine.infer_image` per produrre il JSON compatibile con `KieResponse` (store/datetime/currency/lines/totals con `qty`, `unitPrice`, `lineTotal`, `vatRate`).
  - Verifica:
    - Salute: `GET http://localhost:8081/health`
    - Stato KIE: `GET http://localhost:8081/kie/status`

- Opzione B: Donut (OCR‑free)
  - Posiziona un checkpoint di Donut (es. `donut.ckpt`) in `.data/kie` e imposta `DONUT_CHECKPOINT` + `KIE_MODEL_DIR`.
  - Implementa l’inferenza nel metodo `KieEngine.infer_image` per restituire `KieResponse` coerente.

- Consigli pratici su scontrini
  - Normalizza orientamento/contrasto; riduci a max ~2048 px lato lungo (già fatto in Devices UI) per tempo/ram.
  - Cattura intero scontrino; evita riflessi e pieghe su totali/aliquote.
  - Mappa pattern di sconto: parole tipo `SCONTO`, `OFFERTA`, `-10%`, prezzi “tagliati”; utile per ricostruire promozioni.

## Addestramento ML (tipo/categoria prodotto)

- Come funziona
  - Il servizio ML usa TF‑IDF (char 3–5) + `SGDClassifier` separati per Tipo e Categoria; aggiorna online con `/feedback` e persiste i modelli in `MODEL_DIR` (montato in `./.data/models`).
  - Parametro `TOP_K`: numero candidati suggeriti (env `TOP_K`, default 3).

- Feedback via UI WMC
  - In dettaglio scontrino, usa “Suggerisci” e clicca i bottoni su Tipo/Categoria → l’UI invia `POST /ml/feedback` con `{ labelRaw, finalTypeId, finalCategoryId? }`.
  - Dopo alcuni esempi per classe, i suggerimenti diventano consistenti.

- Feedback via API (curl)
  - `curl -s http://localhost:8085/ml/feedback -H "Authorization: Bearer <TOKEN>" -H "Content-Type: application/json" -d '{"labelRaw":"LATTE 1L","finalTypeId":"<GUID-TYPE>","finalCategoryId":"<GUID-CAT>"}'`
  - Predizione: `curl -s "http://localhost:8085/ml/suggestions?labelRaw=latte" -H "Authorization: Bearer <TOKEN>"`

- Batch train
  - `POST /ml/train` con elenco di esempi (stesso schema di `/feedback`) per ricostruire/ri‑allenare rapidamente.
  - I modelli vengono salvati in `MODEL_DIR` (joblib + json etichette) e ricaricati all’avvio.

- Buone pratiche
  - Fornisci varianti di scrittura (`latte 1l`, `latte UHT`, brand, abbreviations) per migliorare robustezza.
  - Se parti da zero, invia prima feedback su 2–3 classi minime per avviare il training; con una sola classe, il servizio usa fallback deterministico.

## Negozio, Tasse, Totali, Storico Prezzi e Promozioni

- Provenienza dei dati
  - Store, data/ora, valuta, righe (qty, `unitPrice`, `lineTotal`, `vatRate`) e totali (`subtotal`, `tax`, `total`) provengono da KIE; il Worker salva questi campi nel DB.
  - Il testo grezzo OCR viene salvato in `Receipt.RawText` per audit/debug.

- Storico prezzi
  - L’endpoint `GET /analytics/price-history?productId=...&storeId?=...` restituisce la serie storica prezzi per prodotto/negozio.
  - Nota: l’associazione riga→ProductId è ancora da completare (TODO nel codice). Possibili strategie:
    - Mapping semi‑automatico: dopo aver classificato tipo/categoria, proporre `Product` esistenti via alias/brand e confermare.
    - Creazione nuovi `Product` quando non esistono e aggiornamento `PriceHistory` con `Date`, `UnitPrice`, `StoreId`.

- Promozioni e sconti
  - Heuristics pratiche:
    - `unitPrice * qty > lineTotal` → sconto implicito.
    - Pattern nel testo (`SCONTO`, `OFFERTA`, `-10%`, `2x1`) dal `RawText` o da label riga.
  - Persisti un flag di sconto o un evento di promozione a livello di `ReceiptLine` o tabella dedicata per auditing.

## Esecuzione Worker e configurazioni

- Docker (consigliato)
  - `docker compose up -d worker`
  - Env usate dal Worker (override possibili):
    - `ConnectionStrings__Default` (Postgres)
    - `Minio__Endpoint`, `Minio__AccessKey`, `Minio__SecretKey`
    - `Redis__Connection`
    - `Ocr__Endpoint`, `Kie__Endpoint`, `Ml__Endpoint`

- Esecuzione locale (senza Docker)
  - Imposta env in PowerShell:
    - `$env:ConnectionStrings__Default = "Host=localhost;Database=wib;Username=wib;Password=wib"`
    - `$env:Minio__Endpoint = "localhost:9000"`
    - `$env:Redis__Connection = "localhost:6379"`
    - `$env:Ocr__Endpoint = "http://localhost:8081"; $env:Kie__Endpoint = "http://localhost:8081"; $env:Ml__Endpoint = "http://localhost:8082"`
  - Avvio: `dotnet run --project backend\WIB.Worker\WIB.Worker.csproj`

## Troubleshooting Worker/OCR/ML

- L’immagine è in MinIO ma non viene processata
  - Controlla coda Redis: `LRANGE wib:receipts 0 -1` (in `redis-cli`).
  - Log Worker: errori su OCR/KIE/ML (endpoint non raggiungibili) o su DB.
  - Forza un item: `LPUSH wib:receipts "yyyy/MM/dd/<guid>.jpg"`.

- Swagger API su container
  - `http://localhost:8080/swagger` è attivo (compose setta `SWAGGER__ENABLED=true`).
  - Health: `GET /health/live` e `GET /health/ready`.

**Persistenza/Training ML**

- Online: `/feedback` aggiorna i modelli con `partial_fit` (classi create on-the-fly).
- Batch: `/train` rigioca esempi etichettati.
- Persistenza: `MODEL_DIR` (default `/app/models`) per `*.joblib` e vocabolari.

## Frontend (DEV)

- Avvio DEVICES: `npm run start:devices --prefix frontend` (porta 4200)
- Avvio WMC: `npm run start:wmc --prefix frontend` (porta 4201)
- Proxy dev: inoltra `/receipts`, `/analytics` ecc. al backend `http://localhost:8080` (in E2E usare `http://localhost:8085`).

In dev, assicurarsi che `frontend/proxy.conf.json` includa:
```
  "/auth": { "target": "http://localhost:8080", "secure": false, "changeOrigin": true },
  "/ml":   { "target": "http://localhost:8080", "secure": false, "changeOrigin": true }
```

Feature minime incluse:
- DEVICES: upload `multipart/form-data` a `/receipts` con anteprima e compressione.
- WMC: analytics spending, lista receipts, dettaglio con righe e suggerimenti ML, anteprima immagine (`/receipts/{id}/image`).

## API Chiave

- `POST /receipts` (upload) [pubblico]
- `GET /receipts` [wmc]
- `GET /receipts/{id}` [wmc]
- `GET /receipts/{id}/image` [wmc]
- `GET /receipts/pending?maxConfidence=` [wmc]
- `GET /analytics/spending?from=...&to=...` [wmc]
- `GET /analytics/price-history?productId=...&storeId=...` [wmc]
- `GET /ml/suggestions?labelRaw=...` [wmc]
- `POST /ml/feedback` [wmc]
- `POST /auth/token { username, password }` → `{ accessToken, tokenType, expiresIn, role }`

## Politica di Testing

- Unit, integrazione leggera, E2E (piramide). Test .NET con xUnit + EF InMemory; Python con pytest + TestClient.
- Comandi rapidi:
  - `.NET`: `dotnet build backend/WIB.sln && dotnet test backend/WIB.Tests/WIB.Tests.csproj`
  - `Python`: `python -m pytest services/ocr/tests services/ml/tests`
  - `Compose`: `docker compose up -d --build`
- Note SDK: .NET SDK 9 (vedi `global.json`). EF Tools: `dotnet tool update --global dotnet-ef`.

## E2E / Smoke test (via Docker + Proxy)

Esecuzione rapida end‑to‑end con proxy locale `http://localhost:8085`:

```powershell
# dalla root del repo
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
./scripts/e2e.ps1
```

Lo script:
- Avvia lo stack con `docker compose`
- Attende il servizio ML
- Esegue login, una query di spending (vuota), e un upload di scontrino (202)

Verifiche manuali utili:
- Login: `POST http://localhost:8085/auth/token {"username":"admin","password":"admin"}`
- ML suggestions via API: `GET http://localhost:8085/ml/suggestions?labelRaw=latte` con header `Authorization: Bearer <token>`

## Autenticazione (JWT)

- Schema: Bearer JWT con ruoli (`wmc`, `devices`).
- Endpoint di login: `POST /auth/token { username, password }` → `{ accessToken, tokenType, expiresIn, role }`.
- Dev users default: `admin`/`admin` (wmc), `device`/`device` (devices)
- Config: `Auth__Issuer`, `Auth__Audience`, `Auth__Key`
- Rotte protette: `[Authorize(Roles="wmc")]` su analytics, ml, receipts (GET)
- Esempi:
  - Login: `curl -s http://localhost:8085/auth/token -H "Content-Type: application/json" -d '{"username":"admin","password":"admin"}'`
  - Chiamata protetta: `curl -H "Authorization: Bearer <TOKEN>" http://localhost:8085/analytics/spending?from=2025-01-01&to=2025-12-31`

## Appendice: Glossario AI/ML & Strumenti

- AI: Intelligenza Artificiale; campo che include ML e DL.
- ML (Machine Learning): tecniche per apprendere modelli dai dati.
- OCR (Optical Character Recognition): riconoscimento del testo in immagini/documenti.
- KIE (Key-Information Extraction): estrazione di campi chiave e righe da documenti (es. scontrini).
- PP-Structure: componente PaddleOCR per layout/KIE (SER/RE) su documenti.
- SER (Semantic Entity Recognition): riconoscimento di entità con ruolo semantico nel documento.
- RE (Relation Extraction): estrazione di relazioni tra entità (es. associare valore alla sua etichetta).
- Donut: modello OCR-free per document understanding (fine‑tuning su coppie immagine↔JSON target).
- TF‑IDF: pesatura termini per rappresentare testi come vettori (importanza locale/globale).
- N‑grammi: sequenze di n caratteri/parole usate come feature testuali.
- SGDClassifier: classificatore scikit‑learn addestrato con discesa del gradiente stocastico (qui loss logistica).
- KNN: k‑nearest neighbors; classificazione in base ai vicini in uno spazio vettoriale.
- Embedding: rappresentazione densa/vettoriale di testo o immagini in uno spazio numerico.
- Top‑k: i k candidati con punteggio/probabilità più alta.
- Confidence: stima di confidenza/probabilità di una predizione.
- Fine‑tuning: adattamento di un modello pre‑addestrato a un dominio/compito specifico.
- Checkpoint: file/pacchetto di pesi salvati di un modello addestrato.
- JSON/JSONL: formati dati (JSON Lines = un JSON per riga) per dataset/config.
- YAML: formato testuale per file di configurazione (es. PaddleOCR configs).
- PaddleOCR: libreria OCR/KIE basata su PaddlePaddle (include PP‑Structure).
- Tesseract: motore OCR open‑source tradizionale.
- Label Studio: strumento di annotazione (immagini/testo, bbox, relazioni) per creare dataset.
- Doccano: strumento di annotazione testuale (classificazione/sequence labeling).
- FastAPI: framework Python per API (usato dai servizi OCR/ML).
- scikit‑learn: libreria ML Python (TF‑IDF, SGDClassifier, ecc.).
- Joblib: utility per serializzare/persistire modelli e vettorizzatori scikit‑learn.
- Qdrant: database vettoriale per similitudine/ricerca per embedding (opzionale nello stack).
