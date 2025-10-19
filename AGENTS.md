# Repository Guidelines

## Project Structure & Module Organization
- Backend (.NET): `backend/` with solution `backend/WIB.sln`.
  - API: `backend/WIB.API`, Worker: `backend/WIB.Worker`, Domain/Application/Infrastructure libs, tests in `backend/WIB.Tests`.
- Frontend (Angular): `frontend/` with apps `apps/wib-wmc` and `apps/wib-devices`.
- Python services: `services/ml`, `services/ocr` (+ shared utils in `services/shared`).
- Orchestration & config: `docker-compose.yml`, `proxy/`, `docker/`, `docs/`, `scripts/`.

## Build, Test, and Development Commands
- Backend (.NET 9 per `global.json`):
  - Restore/build/test: `dotnet restore && dotnet build backend/WIB.sln -c Debug && dotnet test backend/WIB.sln`
  - Run API/Worker: `dotnet run --project backend/WIB.API` / `dotnet run --project backend/WIB.Worker`
- Frontend (Node 20 per `frontend/package.json`):
  - Install: `cd frontend && npm ci`
  - Dev servers: `npm run start:wmc` (4201) or `npm run start:devices` (4200)
  - Build/tests: `npm run build:wmc` / `npm run test:wmc`
- Python services:
  - ML: `pip install -r services/ml/requirements.txt && pytest services/ml/tests -q`
  - OCR: `pip install -r services/ocr/requirements.txt && pytest services/ocr/tests -q`
- Full stack (Docker): `docker compose up --build`

## Ambiente & Proxy
- Entrypoint locale: `http://localhost:8085` (nginx) che inoltra a API(8080), OCR(8081), ML(8082).
- Angular DEV usa `frontend/proxy.conf.json`: mantieni `/api` → `http://localhost:8080` con `pathRewrite` `^/api`→``; aggiungi anche `/auth` e `/ml` → `http://localhost:8080` per login e suggerimenti ML.
- Evita churn sui prefissi: le API espongono rotte “flat” (es. `/receipts`, `/analytics`, `/auth`); se necessario supporta sia `/auth/*` che `/api/auth/*` per compatibilità DEV.

## Autenticazione & Ruoli
- Ruoli principali: `device` (upload scontrini) e `wmc` (monitoring, analytics, review/edit).
- Endpoint auth disponibili su `/auth/*`; configura env: `Auth__Key`, `Auth__Issuer`, `Auth__Audience` (vedi `docker-compose.yml`).
- Quando aggiungi nuove rotte .NET, rispetta gli `[Authorize(Roles=…)]` già in uso e mantieni i due route-templates se richiesto dalla UX DEV.

## Migrazioni & Database
- EF Core: aggiungi migrazioni da `WIB.Infrastructure` usando l’API come startup project:
  - `dotnet ef migrations add <Name> -s backend/WIB.API -p backend/WIB.Infrastructure`
  - `dotnet ef database update -s backend/WIB.API -p backend/WIB.Infrastructure`
- Target `net8.0`, SDK bloccato via `global.json` (9.x): non modificare senza coordinamento.

## Porte & Log
- Porte: API 8080, Proxy 8085, OCR 8081, ML 8082, MinIO 9000/9001, Postgres 9998 (host), Qdrant 6333.
- Monitoring centralizzato: Redis Streams chiave `app_logs` (vedi `docs/MONITORING.md`).
- Verifica rapida: `docker compose logs -f api worker ml ocr` e `GET /health/ready` sull’API.

## Coding Style & Naming Conventions
- C#: 4 spaces; PascalCase types/methods, camelCase locals/fields; suffix async methods with `Async`; prefer DI and nullability.
- TypeScript/Angular: 2 spaces; kebab-case file names; components `*.component.ts`; suffix observables with `$`.
- Python: PEP 8 (4 spaces), snake_case, type hints where practical.

## Testing Guidelines
- Backend: xUnit-style tests in `backend/WIB.Tests/*Tests.cs`; follow Arrange–Act–Assert.
- Frontend: Jasmine/Karma via `npm run test:wmc` or `test:devices`.
- Python: Pytest with `test_*.py` in each service’s `tests/`.
- Aim to keep or improve coverage; add tests for new logic.

## Commit & Pull Request Guidelines
- Commits: prefer Conventional Commits (e.g., `feat(api): …`, `fix(ocr): …`); imperative, concise subject; meaningful body when needed.
- PRs: include a clear description, linked issues, test plan, and screenshots for UI changes. Keep diffs focused; update docs when behavior or endpoints change.

## Gestione Issue GitHub (gh CLI)
- Usa GitHub CLI `gh` (già autenticata; non serve token) quando richiesto di lavorare su un’issue.
- Flusso tipico:
  - Leggi l’issue: `gh issue view <numero>` oppure lista: `gh issue list --label bug`.
  - Crea/checkout branch: `gh issue checkout <numero>` (crea branch `issue-<num>`).
  - Implementa seguendo il processo: test → nuovi test → docs → build immagini → verifica log.
  - Commit: `git commit -m "fix(<area>): descrizione breve (fixes #<num>)"`.
  - PR: `gh pr create --fill --draft` (aggiungi dettagli e collegamenti); apri: `gh pr view --web`.
  - Merge quando pronto: `gh pr merge --squash --delete-branch` e chiudi issue: `gh issue close <numero> -c "Risolta in #<pr>"`.
  - Verifica repo corrente: `gh repo view` (imposta default se necessario).

## Security & Configuration Tips
- Do not commit secrets; use env vars (`appsettings*.json` for .NET, service env for Docker). Update `docker-compose.yml` only with non-sensitive defaults.

## Istruzioni Specifiche per l’Agente
- Ruolo: full‑stack developer (.NET, Angular, Python) con competenze OCR avanzate, networking in ambienti virtuali/containerizzati, Docker e Windows 11 con PowerShell 5.1.
- Processo per ogni step di implementazione:
  1) Esegui tutti i test esistenti: `dotnet test backend/WIB.sln`, `cd frontend && npm run test:wmc && npm run test:devices`, `pytest services/ml/tests -q && pytest services/ocr/tests -q`.
  2) Aggiungi/aggiorna test per le nuove funzionalità se mancanti.
  3) Aggiorna la documentazione se necessario: modifica `README.md` (in italiano) con descrizioni dettagliate dei cambiamenti e, se utile, esempi d’uso (comandi, endpoint, snippet). Integra anche `docs/` quando opportuno.
  4) Rebuild solo le immagini toccate: `docker compose build api worker ml ocr` (se rilevanti) e `docker compose up -d`.
  5) Verifica i log per 2–5 minuti senza bloccare il lavoro: `docker compose logs -f --tail=200 api worker ocr ml` e interrompi quando sufficiente.
- Comandi di riferimento (Docker/PS 5.1):
  - Docker: `docker compose ps`, `… build <svc>`, `… up -d`, `… logs -f <svc>`, `docker exec -it <container> sh`, `docker inspect <container>`, `docker system df`.
  - Networking/Windows: `Test-NetConnection <host> -Port <p>`, `Invoke-WebRequest http://localhost:8080/health/ready -UseBasicParsing`.
