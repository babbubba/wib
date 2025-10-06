# Repository Guidelines

## Project Structure & Module Organization
- `backend/` (.NET 8): `WIB.API` (REST), `WIB.Worker` (background), libraries under `WIB.*`, tests in `backend/WIB.Tests`, solution `backend/WIB.sln`.
- `frontend/` (Angular 19): apps `wib-devices` and `wib-wmc` under `frontend/apps/*/src`. Config in `frontend/angular.json`, scripts in `frontend/package.json`.
- `services/` (Python FastAPI): `ocr/` and `ml/` with `main.py`, `Dockerfile`, `requirements.txt`, tests in `services/*/tests`.
- `proxy/` (nginx), `docker-compose.yml` (local stack), `scripts/` (helpers), `docs/` (data/tools).

## Build, Test, and Development Commands
- Full stack (Docker): `docker compose up -d --build` then visit proxy `http://localhost:8085`.
- Backend (.NET): `dotnet build backend/WIB.sln`, `dotnet test backend/WIB.Tests/WIB.Tests.csproj`, run API `dotnet run --project backend/WIB.API`, run worker `dotnet run --project backend/WIB.Worker`.
- Frontend (Angular): `npm install --prefix frontend`; dev servers `npm run start:devices --prefix frontend` (4200) and `npm run start:wmc --prefix frontend` (4201); builds `npm run build:devices|build:wmc`.
- Python services: `python -m pip install -r services/ocr/requirements.txt` (and for `ml`); tests `python -m pytest services\ocr\tests services\ml\tests` (PowerShell). If needed: `set PYTHONPATH=.` (cmd) or `$env:PYTHONPATH='.'` (PowerShell).
- E2E smoke (Windows): `.\scripts\e2e.ps1`.

## Coding Style & Naming Conventions
- C#: 4-space indent; PascalCase for types/methods, camelCase for locals/fields; one class per file within `WIB.*` namespaces.
- TypeScript/Angular: 2-space indent; files follow `*.component.ts|html|css`; prefer template/style URLs over inline.
- Python: PEP 8 (4 spaces), type hints where practical; modules under `services/*` with `test_*.py` naming.

## Testing Guidelines
- Backend: xUnit (`backend/WIB.Tests/*Tests.cs`). Coverage: `dotnet test /p:CollectCoverage=true` (coverlet). Add tests for new handlers/controllers.
- Frontend: Jasmine/Karma; keep `*.spec.ts` near components; run `npm run test:devices|test:wmc --prefix frontend` (headless).
- Python: pytest; place tests under `services/*/tests` and name `test_*.py`.

## Commit & Pull Request Guidelines
- Commits: imperative, concise. Conventional Commits encouraged: `feat(api): add KIE status endpoint`, `fix(ocr): handle empty uploads`.
- PRs: include problem/solution summary, linked issues, test/run steps, screenshots for UI changes, and notes on config/env vars touched.

## Security & Configuration Tips
- Use env vars (double-underscore) for nested settings (see `docker-compose.yml`): `ConnectionStrings__Default`, `Ocr__Endpoint`, `Ml__Endpoint`, `Auth__Key`.
- Do not commit secrets; mount models/data under `.data/`. Default ports: API 8080, OCR 8081, ML 8082, Proxy 8085.
- Windows/PowerShell workflow and tips: see `agent.md`.

## Direttive Windows 11 (PowerShell)
- Usa solo PowerShell su Windows 11: non usare sintassi Bash (`export`, `&&`, `VAR=cmd`).
- Variabili d'ambiente: imposta con `$env:KEY = 'value'` nella stessa sessione.
- Docker gira in locale: esegui comandi e leggi i log direttamente (`docker compose up -d --build`, `docker compose ps`, `docker compose logs -f`, `docker compose logs -f api`).
- Avvio lento con i rebuild: preferisci rebuild selettivi e restart (`docker compose build api worker` poi `docker compose up -d`, oppure `docker compose restart api`); usa `--no-cache` solo se necessario.
- HTTP client: usa `Invoke-RestMethod`/`Invoke-WebRequest` o `curl.exe` (non l'alias `curl` di PowerShell).
