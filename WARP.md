# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Project Overview

**WIB (Where I Buy)** is a receipt processing monorepo that extracts data from receipt images using on-premises OCR and ML. The system processes receipts through OCR → KIE (Key Information Extraction) → ML classification → data persistence pipeline.

**Tech Stack**: .NET 8 backend, Angular 19 frontend, PostgreSQL, Redis, MinIO object storage, Python FastAPI services (OCR/ML), containerized with Docker Compose.

## Core Commands

### Full Stack Development

```powershell
# Start complete stack (recommended for development)
docker compose up -d --build

# Access points:
# - Proxy (main entry): http://localhost:8085
# - API direct: http://localhost:8080
# - Devices UI: http://localhost:4200 (via web-devices container)
# - WMC UI: http://localhost:4201 (via web-wmc container)
# - MinIO Console: http://localhost:9001 (wib/wibsecret)

# Rebuild specific services after changes
docker compose build api worker && docker compose up -d api worker
docker compose build web-devices web-wmc && docker compose up -d web-devices web-wmc
```

### Backend (.NET)

```powershell
# Build and test
dotnet build backend/WIB.sln
dotnet test backend/WIB.Tests/WIB.Tests.csproj

# Run services locally (requires infrastructure running)
dotnet run --project backend/WIB.API/WIB.API.csproj
dotnet run --project backend/WIB.Worker/WIB.Worker.csproj
```

### Frontend (Angular 19)

```powershell
# Install dependencies
npm install --prefix frontend

# Development servers (with API proxy)
npm run start:devices --prefix frontend  # Port 4200
npm run start:wmc --prefix frontend      # Port 4201

# Build for production
npm run build:devices --prefix frontend
npm run build:wmc --prefix frontend

# Tests
npm run test:devices --prefix frontend
npm run test:wmc --prefix frontend
```

### Python Services

```powershell
# Install dependencies
python -m pip install -r services/ocr/requirements.txt
python -m pip install -r services/ml/requirements.txt

# Run tests
python -m pytest services\ocr\tests services\ml\tests

# Run services directly (development)
cd services/ocr && python main.py
cd services/ml && python main.py
```

### Quick Testing/Verification

```powershell
# E2E smoke test
./scripts/e2e.ps1

# Manual API testing
# Login: POST http://localhost:8085/auth/token {"username":"admin","password":"admin"}
# Upload: POST http://localhost:8085/receipts (multipart/form-data with image)
# Analytics: GET http://localhost:8085/analytics/spending?from=2025-01-01&to=2025-12-31
```

## Architecture

### High-Level Flow
```
[Upload] → [API] → [MinIO + Redis Queue] → [Worker] → [OCR/KIE] → [ML Classification] → [PostgreSQL]
                                                         ↓
[WMC UI] ← [Analytics/Review APIs] ← [Database] ← [Processed Receipts]
```

### Project Structure

**Backend (.NET 8)**:
- `WIB.API`: REST API endpoints, authentication, file handling
- `WIB.Worker`: Background service for processing receipt queue
- `WIB.Application`: Business logic, handlers, services
- `WIB.Domain`: Core entities (Receipt, Store, Product, etc.)
- `WIB.Infrastructure`: Data access, external service integrations
- `WIB.Tests`: xUnit tests

**Frontend (Angular 19 Monorepo)**:
- `wib-devices`: Mobile-first upload app (camera/file upload)
- `wib-wmc`: Web Management Console (analytics, review, editing)

**Services (Python FastAPI)**:
- `ocr/`: Tesseract OCR + KIE parsing service
- `ml/`: Product type/category classification with incremental learning

### Core Domain Model

```csharp
Receipt (Id, StoreId, Date, Total, Currency, RawText, ImageObjectKey)
├── ReceiptLine[] (LabelRaw, Qty, UnitPrice, LineTotal, VatRate, Predictions)
├── Store (Name, Chain) → StoreLocation[] (Address, City, VatNumber)
└── Product (Name, Brand, GTIN) → ProductType, Category, ProductAlias[]

// Supporting entities
PriceHistory (ProductId, StoreId, Date, UnitPrice)
LabelingEvent (for ML feedback loop)
BudgetMonth, ExpenseAggregate (analytics)
```

## Key Processing Pipeline

### 1. Upload & Queueing
- Images uploaded via `POST /receipts` → saved to MinIO → object key queued in Redis (`wib:receipts`)
- API returns 202 immediately; processing happens asynchronously

### 2. Worker Processing
- Worker dequeues → downloads image → calls OCR/KIE → calls ML predictions → saves to DB
- OCR extracts text; KIE parses structured fields (store, date, lines, totals)
- ML predicts ProductType/Category for each line with confidence scores

### 3. Review & Learning
- WMC UI displays processed receipts with editable fields
- User corrections feed back to ML via `/ml/feedback` for incremental learning
- Manual editing updates Receipt/ReceiptLine data and creates proper Product associations

## Development Patterns

### Environment Variables
Use double-underscore nesting for configuration:
```
ConnectionStrings__Default=Host=localhost;Database=wib;...
Minio__Endpoint=localhost:9000
Redis__Connection=localhost:6379
Auth__Key=your-secret-key
```

### Error Handling
- API uses standard HTTP status codes (400/401/404/409/500)
- Worker includes retry logic for transient failures
- OCR/ML services gracefully degrade (stub responses when models unavailable)

### Testing Strategy
- **Backend**: xUnit with in-memory EF Core for integration tests
- **Frontend**: Jasmine/Karma for component tests
- **Services**: pytest with TestClient for API tests
- **E2E**: Docker Compose stack with scripted smoke tests

## ML Training & Configuration

### Product Classification
- **Features**: TF-IDF (char 3-5, word 1-2) from `LabelRaw` + optional brand
- **Models**: Separate SGDClassifier for ProductType and Category
- **Learning**: Online via `/ml/feedback`, batch via `/ml/train`
- **Storage**: Models persist to `.data/models` volume

### OCR/KIE Configuration
- **Current**: Tesseract OCR + heuristic parsing (production-ready baseline)
- **Advanced**: PP-Structure (PaddleOCR) or Donut models can be mounted to `.data/kie`
- **Environment**: Set `KIE_MODEL_DIR=/app/kie_models` to enable advanced KIE

## Infrastructure Services

### Required for Development
- **PostgreSQL 16**: Main database
- **Redis 7**: Job queue and caching  
- **MinIO**: Object storage for receipt images
- **Nginx**: Reverse proxy (routes to API/OCR/ML)

### Optional Services
- **Qdrant**: Vector database (for semantic search features)
- **Label Studio**: Data annotation tool (port 8088)
- **Doccano**: Text annotation tool (port 8089)

## Authentication & Authorization

- **JWT-based**: Bearer tokens with role-based access
- **Roles**: `wmc` (full access), `devices` (upload only)
- **Default users**: `admin/admin` (wmc), `device/device` (devices)
- **Protected endpoints**: Analytics, ML suggestions, receipt management require `wmc` role

## Common Development Tasks

### Adding New API Endpoints
1. Define handler in `WIB.Application`
2. Add controller action in `WIB.API`
3. Update authorization attributes as needed
4. Add integration tests in `WIB.Tests`

### Extending ML Classification
1. Update domain models if needed
2. Modify ML service prediction logic
3. Add feedback collection in UI
4. Test with sample data via `/ml/train`

### Receipt Processing Customization
1. Extend KIE parsing in `services/ocr/main.py`
2. Update Worker processing logic
3. Modify UI for new fields
4. Add database migrations if schema changes

### Database Changes
1. Update domain entities in `WIB.Domain`
2. Generate EF migrations: `dotnet ef migrations add <Name> --project backend/WIB.Infrastructure`
3. Apply: `dotnet ef database update --project backend/WIB.API`

## Troubleshooting

### Worker Not Processing
- Check Redis queue: `docker exec -it <redis-container> redis-cli LRANGE wib:receipts 0 -1`
- Check worker logs: `docker compose logs -f worker`
- Verify OCR/ML service health: `GET /health` endpoints

### OCR/ML Service Issues
- Services run in stub mode by default (returns mock data)
- Check model directories are properly mounted
- Verify service logs for initialization errors

### Database Connection Issues
- Ensure PostgreSQL is running: `docker compose ps`
- Check connection strings in environment variables
- Verify database exists and migrations applied

## Performance Considerations

- **Images**: Auto-resize to max 2048px in frontend before upload
- **Queue Processing**: Worker processes one item at a time (can be scaled horizontally)
- **ML Models**: TF-IDF vectorizers loaded in memory, incremental learning supported
- **Database**: Consider indexing on Receipt.Date, Store.Name for analytics queries

---

## Windows 11 + PowerShell 5.1: Command Equivalents and Gotchas

To avoid common cross-shell mistakes on Windows, prefer native PowerShell cmdlets. If you need GNU tools, use WSL or Git Bash; otherwise use the mappings below.

- HTTP/REST
  - Prefer `Invoke-RestMethod` (JSON) or `Invoke-WebRequest`.
  - PowerShell's `curl` is an alias to `Invoke-WebRequest`. To call the real curl, use `curl.exe` explicitly.
  - Examples:
    ```powershell
    # JSON login (recommended)
    $body    = @{ username = "admin"; password = "admin" } | ConvertTo-Json
    $login   = Invoke-RestMethod -Uri 'http://localhost:8085/auth/token' -Method Post -ContentType 'application/json' -Body $body
    $token   = $login.accessToken
    $headers = @{ Authorization = "Bearer $token" }
    Invoke-RestMethod -Uri 'http://localhost:8085/analytics/spending?from=2025-01-01&to=2025-12-31' -Headers $headers -Method Get

    # Force native curl (not the PS alias)
    $raw = curl.exe -s http://localhost:8085/health
    ```

- grep
  - `grep pattern file` → `Select-String -Pattern "pattern" -Path file`
  - `grep -R pattern .` → `Get-ChildItem -Recurse | Select-String -Pattern "pattern"`
  - `ls -R | grep pattern` → `Get-ChildItem -Recurse | Where-Object Name -match 'pattern'`

- ls/dir
  - `ls -la` → `Get-ChildItem -Force`
  - `ls -R` → `Get-ChildItem -Recurse`
  - Built-in aliases: `ls` / `dir` / `gci` → `Get-ChildItem`

- cat/head/tail
  - `cat file` → `Get-Content file`
  - `head -n 20 file` → `Get-Content file -TotalCount 20`
  - `tail -n 100 -f file` → `Get-Content file -Tail 100 -Wait`

- sed (inline replace)
  - `sed -i 's/foo/bar/g' file` → `(Get-Content file) -replace 'foo','bar' | Set-Content file`

- find/xargs
  - `find . -name "*.cs"` → `Get-ChildItem -Path . -Recurse -Include *.cs -File`
  - `find . -name "*.tmp" -print0 | xargs -0 rm -f` → `Get-ChildItem -Recurse -Include *.tmp -File | Remove-Item -Force`

- cut/awk (column selection)
  - `cut -d, -f2` → `ConvertFrom-Csv | Select-Object -ExpandProperty 2` (or select by property name)
  - Prefer structured parsing: `ConvertFrom-Json`/`ConvertFrom-Csv` + `Select-Object`.

Quoting tips (PS 5.1)
- Use double quotes for variable interpolation ("$var"); single quotes do not interpolate.
- When running Linux commands inside a container via `bash -lc`/`sh -lc`, wrap the whole command in double quotes in PowerShell and use single quotes inside when needed.

---

## Running DB queries inside Docker containers (non-interactive, safe)

Manage credentials via environment variables in your PowerShell session. Do not print secrets; reference them as variables.

Preparation:
```powershell
# PostgreSQL
$env:PGUSER = 'wib_user'
$env:PGDATABASE = 'wib'
$env:PGPASSWORD = '{{PGPASSWORD}}'   # set your secret value securely

# MySQL/MariaDB
$env:MYSQL_USER = 'wib_user'
$env:MYSQL_DB   = 'wib'
$env:MYSQL_PWD  = '{{MYSQL_PWD}}'

# SQL Server
$env:SA_PASSWORD = '{{SA_PASSWORD}}'

# MongoDB
$env:MONGO_URI = 'mongodb://wib:wibpwd@localhost:27017/wib?authSource=admin'
$env:MONGO_DB  = 'wib'
```

PostgreSQL (container e.g. `db` with psql installed):
```powershell
$SQL = "SELECT now();"
docker exec -i db bash -lc "PGPASSWORD='$env:PGPASSWORD' psql -U $env:PGUSER -d $env:PGDATABASE -v ON_ERROR_STOP=1 -tAc \"$SQL\""
# From file
# Get-Content .\scripts\query.sql | docker exec -i db bash -lc "PGPASSWORD='$env:PGPASSWORD' psql -U $env:PGUSER -d $env:PGDATABASE -v ON_ERROR_STOP=1"
```

MySQL/MariaDB (container e.g. `mysql` with mysql client):
```powershell
$SQL = "SELECT NOW();"
docker exec -i mysql bash -lc "MYSQL_PWD='$env:MYSQL_PWD' mysql -u $env:MYSQL_USER -D $env:MYSQL_DB -se \"$SQL\""
# From file
# Get-Content .\scripts\query.sql | docker exec -i mysql bash -lc "MYSQL_PWD='$env:MYSQL_PWD' mysql -u $env:MYSQL_USER -D $env:MYSQL_DB"
```

SQL Server (container e.g. `mssql` with sqlcmd):
```powershell
$SQL = "SELECT GETDATE();"
docker exec -i mssql /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P $env:SA_PASSWORD -Q "$SQL" -l 30 -W -h -1
```

MongoDB (container e.g. `mongo` with mongosh):
```powershell
# With connection string
docker exec -i mongo mongosh "$env:MONGO_URI" --quiet --eval "db.getSiblingDB('$env:MONGO_DB').stats().ok"
# Query a collection
$JS = "db.getSiblingDB('$env:MONGO_DB').mycol.findOne()"
docker exec -i mongo mongosh "$env:MONGO_URI" --quiet --eval "$JS"
```

Guidelines
- Avoid interactive prompts: pass queries via `-c`/`-Q`/`--eval` or pipe from a file.
- Do not inline secrets in commands; set them in `$env:...` and reference the variables.
- Prefer compact outputs: `-tAc` (psql), `-se` (mysql), `--quiet` (mongosh), `-W -h -1` (sqlcmd) to avoid pagers.

<citations>
<document>
<document_type>WARP_DOCUMENTATION</document_type>
<document_id>getting-started/quickstart-guide/coding-in-warp</document_id>
</document>
</citations>
