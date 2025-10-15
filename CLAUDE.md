# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Where I Buy (WIB)** is an on-premise receipt OCR and expense tracking system. It captures receipt photos from mobile devices, extracts structured data via OCR/KIE, classifies products using local ML, and tracks prices/spending over time.

**Tech Stack:**
- Backend: .NET 8 (Clean Architecture: Domain, Application, Infrastructure, API, Worker)
- Frontend: Angular 19 (two apps: `wib-devices` for upload, `wib-wmc` for analytics/review)
- AI Services: Python FastAPI (OCR/KIE with Tesseract/PaddleOCR, ML classification with scikit-learn)
- Infrastructure: PostgreSQL, Redis (queue), MinIO (object storage), Qdrant (optional vector DB)
- Deployment: Docker Compose for local dev, nginx proxy on port 8085

## Development Environment

**Prerequisites:**
- Docker Desktop (Linux containers)
- Node.js 20.x
- .NET SDK 9 (pinned via `global.json`, targets `net8.0`)
- Python 3.x (for services/ocr and services/ml)

**Platform:** Windows 11 with PowerShell
- Use PowerShell syntax, not Bash (`$env:VAR = "value"`, not `export VAR=value`)
- Use `&&` for sequential commands in single bash calls only
- For multi-step operations, chain with PowerShell's `;` or use separate commands

## Quick Start Commands

**Full Stack (Docker):**
```powershell
# Start entire stack (API, Worker, DB, Redis, MinIO, OCR, ML, Proxy)
docker compose up -d --build

# Proxy available at http://localhost:8085
# Individual services: API :8080, OCR :8081, ML :8082, MinIO console :9001
```

**Backend (.NET):**
```powershell
# Build solution
dotnet build backend/WIB.sln

# Run tests
dotnet test backend/WIB.Tests/WIB.Tests.csproj

# Run API locally (requires DB/Redis/MinIO/OCR/ML running)
dotnet run --project backend/WIB.API/WIB.API.csproj

# Run Worker locally
dotnet run --project backend/WIB.Worker/WIB.Worker.csproj

# EF Migrations (if needed)
dotnet ef migrations add MigrationName --project backend/WIB.Infrastructure --startup-project backend/WIB.API
dotnet ef database update --project backend/WIB.Infrastructure --startup-project backend/WIB.API
```

**Frontend (Angular):**
```powershell
# Install dependencies
npm install --prefix frontend

# Dev server for Devices app (upload UI, port 4200)
npm run start:devices --prefix frontend

# Dev server for WMC app (analytics/review UI, port 4201)
npm run start:wmc --prefix frontend

# Build production
npm run build:devices --prefix frontend
npm run build:wmc --prefix frontend

# Run tests
npm run test:devices --prefix frontend
npm run test:wmc --prefix frontend
```

**Python Services:**
```powershell
# Install dependencies
python -m pip install -r services/ocr/requirements.txt
python -m pip install -r services/ml/requirements.txt

# Run tests
python -m pytest services/ocr/tests services/ml/tests
```

**Docker Selective Rebuild:**
```powershell
# Rebuild specific services without full restart
docker compose build api worker
docker compose up -d api worker

# Restart without rebuild
docker compose restart api

# View logs
docker compose logs -f worker
docker compose logs -f api
```

## Architecture Overview

### Clean Architecture (.NET Backend)

The backend follows Clean Architecture with strict dependency flow:

```
WIB.Domain (entities, no dependencies)
    ↑
WIB.Application (use cases, interfaces, MediatR commands/handlers)
    ↑
WIB.Infrastructure (EF Core, external clients: MinIO, Redis, HTTP clients for OCR/ML)
    ↑
WIB.API (REST controllers, JWT auth, Swagger)
WIB.Worker (background service, consumes Redis queue)
```

**Key Patterns:**
- **Domain:** Pure entities in `Receipt.cs` (Receipt, ReceiptLine, Store, StoreLocation, Product, Category, ProductType, User, Role, etc.)
- **Application:** MediatR CQRS pattern. Example: `ProcessReceiptCommand` → `ProcessReceiptCommandHandler` orchestrates OCR/KIE/ML/persistence
- **Infrastructure:**
  - `WibDbContext` (EF Core with Postgres)
  - `Storage/MinioClient.cs` for object storage
  - `Queue/RedisQueue.cs` for job queue
  - `Clients/` for HTTP calls to OCR/KIE/ML services
- **API:** REST endpoints with JWT bearer auth. Roles: `wmc` (analytics/admin), `devices` (upload)
- **Worker:** Background service that polls Redis queue (`wib:receipts`), downloads images from MinIO, calls OCR→KIE→ML, saves to DB

### Receipt Processing Pipeline

1. **Upload:** Angular app → `POST /receipts` (multipart/form-data) → API saves image to MinIO → pushes object key to Redis queue
2. **Worker Processing:**
   - Polls `wib:receipts` queue (Redis LPOP)
   - Downloads image from MinIO
   - Calls OCR service (`POST /extract`) for raw text
   - Calls KIE service (`POST /kie`) for structured extraction (store, datetime, currency, lines with qty/unitPrice/lineTotal/vatRate, totals)
   - For each line: calls ML service (`POST /predict`) for ProductType and Category suggestions
   - Persists Receipt + ReceiptLines to DB with predictions and confidence scores
3. **Review/Edit:** WMC app displays receipts, allows manual correction, sends feedback to ML (`POST /ml/feedback`)

### OCR/KIE Service (FastAPI, Python)

- **Stub mode by default** (returns mock data for integration testing)
- **Production modes** (configurable via env vars):
  - `PP-Structure` (PaddleOCR SER/RE for layout-aware KIE)
  - `Donut` (OCR-free transformer model)
- **Endpoints:**
  - `POST /extract` → raw OCR text (Tesseract baseline)
  - `POST /kie` → structured fields (store, datetime, currency, lines[], totals)
  - `GET /health`, `GET /kie/status`
- **Environment variables:**
  - `KIE_MODEL_DIR`: path to model weights (mounted from `.data/kie`)
  - `PP_STRUCTURE_SER_CFG`, `PP_STRUCTURE_RE_CFG`: config files for PP-Structure
  - `DONUT_CHECKPOINT`: path to Donut checkpoint

### ML Classification Service (FastAPI, Python)

- **Online learning** with TF-IDF (char 3-5 grams) + SGDClassifier
- **Two separate models:** ProductType classifier and Category classifier
- **Endpoints:**
  - `POST /predict` → top-k suggestions for type and category (with confidence scores)
  - `POST /feedback` → incremental learning from user corrections (partial_fit)
  - `POST /train` → batch retrain from examples
  - `GET /health`
- **Persistence:** Models saved to `MODEL_DIR` (mounted from `.data/models`) as joblib files
- **Environment variables:**
  - `MODEL_DIR`: path to persist models
  - `TOP_K`: number of candidates to return (default 3)

### Frontend Architecture (Angular 19)

Two separate Angular applications in a monorepo:

1. **wib-devices** (port 4200): Mobile-first upload UI
   - Capture/select receipt photo
   - Compress and upload to `/receipts`
   - Sequential upload mode for rapid batch processing

2. **wib-wmc** (port 4201): Analytics and review UI
   - Dashboard with spending analytics (`/analytics/spending`)
   - Receipt list and detail views
   - Edit receipts: store, datetime, currency, lines (qty, price, category)
   - ML suggestion UI with feedback buttons (`/ml/suggestions`, `/ml/feedback`)
   - Queue management (view pending items, re-queue failed jobs)

**Dev Proxy:** `frontend/proxy.conf.json` forwards `/receipts`, `/analytics`, `/auth`, `/ml` to `http://localhost:8080` (API)

## Configuration & Environment Variables

All services use hierarchical config with `__` separator (e.g., `ConnectionStrings__Default`).

**API/Worker (.NET):**
- `ConnectionStrings__Default`: Postgres connection string
- `Minio__Endpoint`, `Minio__AccessKey`, `Minio__SecretKey`
- `Redis__Connection`: Redis connection string
- `Ocr__Endpoint`, `Kie__Endpoint`, `Ml__Endpoint`: Python service URLs
- `Auth__Key`, `Auth__Issuer`, `Auth__Audience`: JWT config
- `SWAGGER__ENABLED`: Enable Swagger UI (default true in dev)

**OCR Service (Python):**
- `KIE_MODEL_DIR`: Mount path for model weights
- `PP_STRUCTURE_SER_CFG`, `PP_STRUCTURE_RE_CFG`: Optional config paths
- `DONUT_CHECKPOINT`: Optional Donut checkpoint path
- `OCR_STUB`: Set to "true" for stub mode

**ML Service (Python):**
- `MODEL_DIR`: Path to persist trained models
- `TOP_K`: Number of candidates to return (default 3)

**Local development (PowerShell):**
```powershell
$env:ConnectionStrings__Default = "Host=localhost;Database=wib;Username=wib;Password=wib"
$env:Minio__Endpoint = "localhost:9000"
$env:Redis__Connection = "localhost:6379"
$env:Ocr__Endpoint = "http://localhost:8081"
$env:Kie__Endpoint = "http://localhost:8081"
$env:Ml__Endpoint = "http://localhost:8082"
```

## Key API Endpoints

**Authentication:**
- `POST /auth/token` → `{ username, password }` returns `{ accessToken, tokenType, expiresIn, role }`
  - Dev users: `admin/admin` (wmc role), `device/device` (devices role)

**Receipts (upload public, others require `[Authorize(Roles="wmc")]`):**
- `POST /receipts` → Upload receipt image (multipart/form-data)
- `GET /receipts?take=20` → List receipts
- `GET /receipts/{id}` → Receipt detail with lines
- `GET /receipts/{id}/image` → Download receipt image
- `POST /receipts/{id}/edit` → Edit store, datetime, currency, lines
- `GET /receipts/pending?maxConfidence=0.8` → Low-confidence receipts for review

**Analytics (`[Authorize(Roles="wmc")]`):**
- `GET /analytics/spending?from=YYYY-MM-DD&to=YYYY-MM-DD` → Spending aggregates
- `GET /analytics/price-history?productId={guid}&storeId={guid}` → Price trends

**ML (`[Authorize(Roles="wmc")]`):**
- `GET /ml/suggestions?labelRaw={text}` → Get type/category predictions
- `POST /ml/feedback` → Send correction for online learning

**Categories:**
- `GET /categories/lookup?name={text}` → Case-insensitive category lookup (returns `{ id, name, exists }`)

**Health:**
- `GET /health/live`, `GET /health/ready` (API)
- `GET /health` (OCR, ML services)

## Database Schema (Postgres)

Main entities (managed by EF Core migrations in `WIB.Infrastructure/Data/Migrations/`):

- **Users & Auth:** `User`, `Role`, `UserRole`, `RefreshToken`
- **Receipts:** `Receipt` (store, date, total, currency, rawText, imageObjectKey) → `ReceiptLine` (productId, labelRaw, qty, unitPrice, lineTotal, vatRate, predictions)
- **Stores:** `Store` (name, chain) → `StoreLocation` (address, city, postalCode, vatNumber)
- **Products:** `Product` (name, brand, GTIN, typeId, categoryId) → `ProductAlias` (alternate names)
- **Taxonomy:** `ProductType`, `Category` (hierarchical with parentId)
- **Tracking:** `PriceHistory` (product, store, date, unitPrice), `ExpenseAggregate`, `BudgetMonth`, `LabelingEvent` (ML feedback audit)

## Testing Strategy

- **.NET:** xUnit in `backend/WIB.Tests/`, uses EF Core InMemory provider
- **Python:** pytest in `services/ocr/tests/` and `services/ml/tests/`, uses FastAPI TestClient
- **Angular:** Jasmine/Karma (run with `npm run test:devices|test:wmc`)
- **E2E:** PowerShell script `scripts/e2e.ps1` (starts stack, tests login/upload/analytics)

## Common Development Workflows

**Adding a new API endpoint:**
1. Define entity in `WIB.Domain/Receipt.cs` (or new file)
2. Add migration: `dotnet ef migrations add AddNewFeature --project backend/WIB.Infrastructure --startup-project backend/WIB.API`
3. Create command/handler in `WIB.Application/` (MediatR pattern)
4. Add controller action in `WIB.API/Controllers/`
5. Update `WibDbContext` if needed
6. Write tests in `WIB.Tests/`

**Training ML models:**
1. Ensure ML service is running (`docker compose up -d ml`)
2. Send feedback via WMC UI or API: `POST /ml/feedback` with `{ labelRaw, finalTypeId, finalCategoryId }`
3. After ~10-20 examples per class, predictions improve
4. Models persist in `.data/models/` (mounted volume)
5. For batch training: `POST /ml/train` with array of examples

**Configuring KIE (OCR extraction):**
- Currently in stub mode (returns mock data)
- To enable real KIE:
  1. Place model weights in `.data/kie/`
  2. Set env vars: `KIE_MODEL_DIR=/app/kie_models`, optionally `PP_STRUCTURE_SER_CFG` or `DONUT_CHECKPOINT`
  3. Implement inference in `services/ocr/main.py` → `KieEngine.infer_image()`
  4. Rebuild OCR service: `docker compose build ocr && docker compose up -d ocr`

**Re-processing receipts:**
- Manual queue insertion:
  ```powershell
  docker exec -it wib-main-redis-1 redis-cli LPUSH wib:receipts "2025/01/15/<guid>.jpg"
  ```
- Check queue: `docker exec -it wib-main-redis-1 redis-cli LRANGE wib:receipts 0 -1`

**Debugging Worker:**
- View logs: `docker compose logs -f worker`
- Common issues:
  - OCR/ML endpoints unreachable → check service health with `docker compose ps`
  - Image not in MinIO → verify upload succeeded, check MinIO console at http://localhost:9001
  - DB errors → check migrations applied, verify connection string

## Code Style

**C# (.NET):**
- 4-space indentation
- PascalCase for types/methods, camelCase for variables/fields
- One class per file, file name matches class name
- Prefer explicit types over `var` for domain entities
- Use nullable reference types (`#nullable enable`)

**TypeScript/Angular:**
- 2-space indentation
- Component files: `*.component.ts|html|css` in same directory
- Services use dependency injection
- Prefer standalone components (Angular 19+)

**Python:**
- PEP 8 (4-space indentation)
- Type hints encouraged (especially for request/response models)
- Test files: `test_*.py` in `tests/` subdirectory

## Security Notes

- **No secrets in repo:** Use environment variables for keys/passwords
- **JWT auth:** API validates bearer tokens, roles checked via `[Authorize(Roles="wmc")]`
- **CORS:** Configured in API for dev (allows localhost:4200, 4201, 8085)
- **File uploads:** 20 MB limit (configured in proxy nginx.conf and API FormOptions)
- **Dev credentials:** Default admin/admin and device/device are for local dev only

## Port Reference

- **8080:** API (direct access, use 8085 proxy in E2E)
- **8081:** OCR/KIE service
- **8082:** ML service
- **8085:** Nginx proxy (unified entry point for E2E)
- **4200:** Angular wib-devices dev server
- **4201:** Angular wib-wmc dev server
- **9000:** MinIO API
- **9001:** MinIO console UI
- **9998:** PostgreSQL (mapped from container's 5432)
- **6333:** Qdrant (optional, for vector search)
- **8088:** Label Studio (optional, for annotation)
- **8089:** Doccano (optional, for annotation)

## Monorepo Structure

```
wib-github/
├── backend/
│   ├── WIB.Domain/          # Entities (Receipt, Store, Product, etc.)
│   ├── WIB.Application/     # MediatR commands/handlers, interfaces
│   ├── WIB.Infrastructure/  # EF Core, MinIO, Redis, HTTP clients
│   ├── WIB.API/             # REST API, controllers, JWT auth
│   ├── WIB.Worker/          # Background queue consumer
│   └── WIB.Tests/           # xUnit tests
├── frontend/
│   ├── apps/
│   │   ├── wib-devices/     # Upload Angular app
│   │   └── wib-wmc/         # Analytics Angular app
│   └── proxy.conf.json      # Dev proxy config
├── services/
│   ├── ocr/                 # FastAPI OCR/KIE service
│   └── ml/                  # FastAPI ML classification service
├── proxy/
│   └── nginx.conf           # Nginx reverse proxy config
├── docs/                    # Documentation and sample data
├── scripts/                 # Helper scripts (e2e.ps1, etc.)
├── .data/                   # Local data (models, KIE weights, annotations)
├── docker-compose.yml       # Full stack orchestration
├── global.json              # .NET SDK version (9.0.304)
└── README.md                # Detailed project documentation
```

## Important Files

- `docker-compose.yml`: Full stack definition, service dependencies, env vars, volume mounts
- `backend/WIB.Domain/Receipt.cs`: All domain entities in one file
- `backend/WIB.Infrastructure/Data/WibDbContext.cs`: EF Core context with entity configurations
- `backend/WIB.Application/Receipts/ProcessReceiptCommandHandler.cs`: Core receipt processing orchestration
- `services/ocr/main.py`: OCR/KIE service (KieEngine class for model loading)
- `services/ml/main.py`: ML service (ModelManager class for online learning)
- `frontend/proxy.conf.json`: Dev proxy routes for Angular
- `README.md`: Comprehensive guide (27KB) covering architecture, deployment, training, troubleshooting

## References

For detailed information on specific topics, refer to `README.md` sections:
- Addestramento ML (tipo/categoria prodotto)
- Come addestrare KIE (campi + righe)
- Configurazione OCR/KIE (estrazione campi e righe)
- Workflow di Elaborazione Scontrini (dettaglio)
- Troubleshooting Worker/OCR/ML
- Glossario AI/ML & Strumenti
