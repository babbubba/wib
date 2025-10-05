# Guida Agente (PowerShell su Windows)

Documento operativo per lavorare sul monorepo â€œWhere I Buyâ€ usando esclusivamente PowerShell su Windows (niente sintassi Bash).

## Scopo
- Dare comandi corretti in PowerShell per avviare stack, sviluppare e testare componenti.
- Evitare errori tipici del porting Bashâ†’PowerShell (es. `export`, `VAR=cmd`, `curl` con apici singoli, `&&`).
- Riassumere la struttura del repo (da `README.md`) per orientare le attivitÃ .

## Struttura del Monorepo (estratta da README)
- `backend/` (.NET)
  - `WIB.sln`, progetti: `WIB.API`, `WIB.Worker`, `WIB.Application`, `WIB.Domain`, `WIB.Infrastructure`, test `WIB.Tests`.
- `services/` (Python FastAPI)
  - `ocr/` (OCR/KIE) con `main.py`, `requirements.txt`, `Dockerfile`.
  - `ml/` (Classificazione/ML) con `main.py`, `requirements.txt`, `Dockerfile`.
- `frontend/` (Angular 19)
  - app: `wib-devices` (upload) e `wib-wmc` (analytics/review). Scripts in `package.json`.
- `proxy/` (Nginx) e `docker-compose.yml` (orchestrazione locale con API, Worker, DB, Redis, MinIO, OCR, ML, Proxy, ecc.).
- `scripts/`
  - `e2e.ps1` (PowerShell) per smoke/E2E locali; presenti anche versioni `.sh` per ambienti Unix.
- `.data/` (dati runtime, modelli ML/KIE montati nei container).

Endpoint chiave (via proxy): `http://localhost:8085` â†’ inoltra verso API, OCR, ML.

## Prerequisiti (Windows)
- Docker Desktop (Linux containers attivi).
- Node.js 20.x (vedi `frontend/package.json: engines.node`).
- .NET SDK 9 (vedi `global.json`).
- Python + pip (per esecuzione servizi `services/*` fuori da Docker).
- PowerShell 5.1+ (consigliato 7.x); eseguire la console come utente con permessi Docker.

## Convenzioni PowerShell da usare (e cosa NON usare)
- Impostare variabili dâ€™ambiente (sessione corrente):
  - `# OK` â†’ `$env:ASPNETCORE_URLS = "http://localhost:8080"`
  - `# NON usare` â†’ `export ASPNETCORE_URLS=...` oppure `ASPNETCORE_URLS=... dotnet run`
- Chiamate HTTP: preferire `Invoke-RestMethod`/`Invoke-WebRequest` oppure `curl.exe` (non lâ€™alias `curl` che in PS mappa a `Invoke-WebRequest`).
- Sequenze di comandi: usare nuove righe o `;`. Evitare `&&`/`||` (non portabili su PS 5.1).
- Continuazione riga: backtick `` ` `` a fine riga (non la barra `\`).
- Percorsi: `backend\WIB.API` (backslash) o `/` funziona, ma usare virgolette quando sono presenti spazi.
- JSON: costruire con `ConvertTo-Json` quando possibile; in alternativa usare doppi apici e fare escaping correttamente.

## Avvio rapido dello stack (Docker)
Esegue API, Worker, DB, Redis, MinIO, OCR, ML, Proxy.

```powershell
# dalla root del repo
Docker version
docker compose up -d --build

# stato e log
docker compose ps
docker compose logs -f
```

Proxy locale: `http://localhost:8085`

## Frontend (DEV)
```powershell
# install (una sola volta)
npm install --prefix frontend

# avvio DEVICES (porta 4200)
npm run start:devices --prefix frontend

# avvio WMC (porta 4201)
npm run start:wmc --prefix frontend
```

## Build & Test
- .NET
  ```powershell
  dotnet --info
  dotnet build backend\WIB.sln
  dotnet test backend\WIB.Tests\WIB.Tests.csproj
  ```
- Python (servizi OCR/ML)
  ```powershell
  python -m pytest services\ocr\tests services\ml\tests
  ```

## Esecuzione locale dei singoli servizi (senza Docker)
Utile per debug rapido. Impostare le variabili dâ€™ambiente in PowerShell prima di avviare i processi.

- API .NET (`WIB.API`)
  ```powershell
  $env:ASPNETCORE_URLS = "http://localhost:8080"
  $env:ConnectionStrings__Default = "Host=localhost;Database=wib;Username=wib;Password=wib"
  $env:Minio__Endpoint = "localhost:9000"
  $env:Minio__AccessKey = "wib"
  $env:Minio__SecretKey = "wibsecret"
  $env:Redis__Connection = "localhost:6379"
  $env:Ocr__Endpoint = "http://localhost:8081"
  $env:Kie__Endpoint = "http://localhost:8081"
  $env:Ml__Endpoint  = "http://localhost:8082"
  dotnet run --project backend\WIB.API\WIB.API.csproj
  ```

- Worker .NET (`WIB.Worker`)
  ```powershell
  $env:ConnectionStrings__Default = "Host=localhost;Database=wib;Username=wib;Password=wib"
  $env:Minio__Endpoint = "localhost:9000"
  $env:Minio__AccessKey = "wib"
  $env:Minio__SecretKey = "wibsecret"
  $env:Redis__Connection = "localhost:6379"
  $env:Ocr__Endpoint = "http://localhost:8081"
  $env:Kie__Endpoint = "http://localhost:8081"
  $env:Ml__Endpoint  = "http://localhost:8082"
  dotnet run --project backend\WIB.Worker\WIB.Worker.csproj
  ```

- Servizio OCR (FastAPI)
  ```powershell
  # opzionale: creare venv e installare dipendenze
  python -m pip install -r services\ocr\requirements.txt

  # opzionali: modelli KIE
  $env:KIE_MODEL_DIR = "$PWD\.data\kie"
  # $env:PP_STRUCTURE_SER_CFG = "$env:KIE_MODEL_DIR\ser_infer.yml"
  # $env:PP_STRUCTURE_RE_CFG  = "$env:KIE_MODEL_DIR\re_infer.yml"
  # $env:DONUT_CHECKPOINT     = "$env:KIE_MODEL_DIR\donut.ckpt"

  # avvio
  Set-Location services\ocr
  python -m uvicorn main:app --host 0.0.0.0 --port 8081
  ```

- Servizio ML (FastAPI)
  ```powershell
  python -m pip install -r services\ml\requirements.txt
  $env:MODEL_DIR = "$PWD\.data\models"
  $env:TOP_K = "3"
  Set-Location services\ml
  python -m uvicorn main:app --host 0.0.0.0 --port 8082
  ```

> Nota: Qdrant/DB/Redis/MinIO conviene lasciarli in Docker. In alternativa, avviarli con `docker compose` e far puntare i servizi locali a `localhost` e alle porte mappate dal compose.

## Esempi chiamate API (PowerShell puro)
- Login e richiesta protetta con `Invoke-RestMethod` (consigliato):
  ```powershell
  $body = @{ username = "admin"; password = "admin" } | ConvertTo-Json
  $login = Invoke-RestMethod -Uri 'http://localhost:8085/auth/token' -Method Post -ContentType 'application/json' -Body $body
  $token = $login.accessToken

  $headers = @{ Authorization = "Bearer $token" }
  Invoke-RestMethod -Uri 'http://localhost:8085/analytics/spending?from=2025-01-01&to=2025-12-31' -Headers $headers -Method Get
  ```

- Alternativa con `curl.exe` (evita lâ€™alias `curl` di PowerShell):
  ```powershell
  $loginRaw = curl.exe -s http://localhost:8085/auth/token -H "Content-Type: application/json" -d "{\"username\":\"admin\",\"password\":\"admin\"}"
  $token = ($loginRaw | ConvertFrom-Json).accessToken
  curl.exe -s "http://localhost:8085/analytics/spending?from=2025-01-01&to=2025-12-31" -H "Authorization: Bearer $token"
  ```

## Script utili (Windows)
- Smoke/E2E locali via PowerShell: `scripts\e2e.ps1`
  ```powershell
  # se necessario: bypass della policy per la sola sessione
  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

  # dalla root
  .\scripts\e2e.ps1
  ```

## Troubleshooting rapido
- Docker Desktop deve essere in modalitÃ  Linux containers.
- Porte occupate: modificare le porte mappate in `docker-compose.yml` o chiudere processi in conflitto.
- Node 20.x: usare `nvm-windows` oppure installare la versione corretta (vedi `frontend/package.json`).
- `curl` restituisce oggetti HTML: in PowerShell Ã¨ alias di `Invoke-WebRequest`; usare `Invoke-RestMethod` o `curl.exe`.
- Variabili dâ€™ambiente per .NET/Python: impostarle con `$env:...` nella stessa sessione in cui si avvia il processo.

---
Riferimenti: vedere `README.md` per obiettivi, architettura, API e dettagli dei servizi; questo file si focalizza sullâ€™uso corretto di PowerShell su Windows.


---

Direttive aggiuntive per lo sviluppo Angular

- Template HTML sempre in file separati: non usare mai template inline nei componenti. Ogni componente deve avere `templateUrl` verso un file `.html` dedicato (e preferibilmente `styleUrls` per gli stili).
- Test obbligatori: ogni volta che crei o modifichi un componente, aggiorna/crea il relativo file di test (`*.spec.ts`). I test devono coprire almeno la creazione del componente e le azioni/metodi principali invocati dal template.
- Bootstrap 5 come stile base: integrare Bootstrap 5 come stile globale dei frontend (WMC e Devices) e adattare l'HTML con le classi Bootstrap (layout, pulsanti, liste, tabelle). Evitare CSS in linea quando possibile.

---

Direttive Docker/Compose (Windows 11 + WSL2)

- Ambiente: Windows 11 con Docker Desktop (backend WSL2). I comandi Docker vanno eseguiti in PowerShell; i container girano in WSL2 ma log e stato sono accessibili direttamente da Windows.
- Compose è in esecuzione in locale: l’agente può accedere direttamente ai log e allo stato dei servizi.
  - Stato: `docker compose ps`
  - Log stack: `docker compose logs -f`
  - Log servizio: `docker compose logs -f api|worker|ocr|ml|proxy|db|redis|minio`
- Rebuild/Restart: se i sorgenti cambiano e serve ricostruire le immagini, usare:
  - Rebuild selettivo: `docker compose build api worker ocr ml` poi `docker compose up -d`
  - Rebuild completo: `docker compose up -d --build`
  - Rebuild senza cache: `docker compose build --no-cache <service>`
  - Restart rapido: `docker compose restart <service>`
- Se l’agente non può eseguire direttamente rebuild/restart (permessi/blocchi), deve suggerire all’utente i comandi PowerShell esatti da lanciare e indicare i servizi da ricostruire.
- Note WSL2: i percorsi Windows (es. `C:\\...`) sono montati nei container; continuare a usare sintassi PowerShell e `docker compose` (spazio), non `docker-compose`.

