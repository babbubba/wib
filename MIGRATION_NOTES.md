# Database Migration Management

## Key Findings

### Migration Responsibility
- **API Service**: Handles ALL database migrations via `db.Database.Migrate()` in `Program.cs` (lines 143-179)
- **Worker Service**: Only verifies connectivity via `CanConnectAsync()` (lines 53-82), NO migrations
- **Shared Models**: Both use identical `WibDbContext` from `WIB.Infrastructure`

### Current Issue Resolution
1. **Problem**: `EnhancedNameMatcher` duplicate key "ipercoop" - RESOLVED ✓
2. **Problem**: Missing `UserId` column in `Receipts` table - IN PROGRESS
3. **Root Cause**: Migration files and database state are out of sync

### Solution Applied
The database schema is missing User authentication tables because:
- Migration files exist: `20251012170355_InitialCreate.cs`, `20251012183336_AddUserAuthentication.cs`
- Database only applied: `InitialCreate` migration
- Missing: `AddUserAuthentication` migration (creates Users, RefreshTokens, adds UserId to Receipts)

### Commands Used for Resolution
```powershell
# 1. Fixed EnhancedNameMatcher duplicate key by updating initialization logic
# 2. Applied missing SQL migration manually (temporary fix)
Get-Content add_auth_migration.sql | docker compose exec -T db psql -U wib -d wib

# 3. To create proper unified migration (recommended approach):
Remove-Item backend\WIB.Infrastructure\Data\Migrations\*.cs -Force
docker compose down -v  # Full reset
# Create new unified migration with complete current model
```

### Migration Files Location
- Path: `backend/WIB.Infrastructure/Data/Migrations/`
- Design package: `WIB.Infrastructure.csproj` includes `Microsoft.EntityFrameworkCore.Design`
- Startup project: Use `../WIB.API` for migrations

### Best Practices Going Forward
1. Only API manages migrations - never add migration logic to Worker
2. Always use EF migrations, avoid manual SQL scripts
3. Test migration on clean database before deployment
4. Keep migration files in sync with model changes
5. Use `docker compose down -v` to test full database recreation

### Architecture Summary
```
API Service (Migration Owner)
├── db.Database.Migrate() on startup
├── DatabaseSeedService for default data  
├── Retry logic (10 attempts, 2s delay)
└── Shared WibDbContext

Worker Service (Migration Consumer)  
├── db.Database.CanConnectAsync() only
├── Waits for API to complete migrations
└── Same WibDbContext (models in sync)
```

## Next Steps
- [ ] Create unified migration with all current entities
- [ ] Test clean database startup
- [ ] Verify worker starts after API migrations complete
- [ ] Update README.md with this migration policy
- [ ] Test end-to-end receipt processing pipeline