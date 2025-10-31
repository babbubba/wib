# Piano di Sviluppo — Issue #15: Ottimizza estrazione/modifica Store label

Autore: team WIB • Data: 2025-10-19 • Issue: https://github.com/babbubba/wib/issues/15

## Contesto e Obiettivo
- In fase di edit del nome Store, vogliamo evitare duplicati e orfani.
- Regole:
  - Se esiste già uno Store con lo stesso nome normalizzato ⇒ MERGE (riassegna i Receipt al target, crea alias, elimina/archivia l’attuale).
  - Se non esiste ⇒ RENAME in place dello Store corrente (creando alias dell’old name).
- Richiesta: implementare tutto via EF Core (no SQL raw), con migrazioni progressive e backfill.

## Disegno Soluzione
- Modello dominio (nuovo/esteso):
  - `Store`
    - `Name: string`
    - `NameNormalized: string` (NUOVO)
    - `IsDeleted: bool` (opzionale; utile se si preferisce soft-delete per merge)
    - `Aliases: ICollection<StoreAlias>` (NUOVO)
  - `StoreAlias`
    - `Id: Guid`
    - `StoreId: Guid` (FK → Store, cascade delete)
    - `AliasNormalized: string` (unique per Store)
    - `CreatedAt: DateTime` (UTC)
- Vincoli DB:
  - `Store.NameNormalized` NOT NULL + indice univoco.
  - `StoreAlias (StoreId, AliasNormalized)` indice univoco.
- Normalizzazione in C# (no SQL):
  - Funzione `Normalize(string)` usata per NameNormalized e AliasNormalized (lowercase, rimozione diacritici, keep alnum+spazi, collapse spazi).
  - Utility proposta: `WIB.Infrastructure/Utils/StringNormalizer.cs` oppure metodo statico condiviso (evitare duplicazioni con EnhancedNameMatcher: eventuale refactor futuro per riuso).

## Sequenza Migrazioni (2 step)
1) Bootstrap (schema iniziale)
   - Aggiungi colonna `Store.NameNormalized` nullable (temporaneo).
   - Crea tabella `StoreAlias` + indice unico su `(StoreId, AliasNormalized)`.
   - Opzionale: indice non univoco provvisorio su `Store.NameNormalized`.
2) Finalize (hardening vincoli)
   - Imposta `Store.NameNormalized` NOT NULL.
   - Crea indice unico su `Store.NameNormalized`.

Comandi EF (da root repo):
- Add bootstrap: `dotnet ef migrations add Store_Normalized_Bootstrap -s backend/WIB.API -p backend/WIB.Infrastructure`
- Update DB: `dotnet ef database update -s backend/WIB.API -p backend/WIB.Infrastructure`
- Dopo backfill/merge: `dotnet ef migrations add Store_Normalized_Finalize -s backend/WIB.API -p backend/WIB.Infrastructure`
- Update DB finale: `dotnet ef database update -s backend/WIB.API -p backend/WIB.Infrastructure`

## Backfill e Data Patch (senza SQL)
- All’avvio (feature-flag, es.: `Migrations__RunStoreBackfill=true`) o con HostedService dedicato:
  - Per ogni `Store` con `NameNormalized == null`:
    - Calcola `Normalize(Name)` e assegna `NameNormalized`.
  - Gestione collisioni (stesso `NameNormalized`):
    - Seleziona un target (es. il più vecchio Id/CreatedAt se disponibile, o ordinamento deterministico).
    - Esegui MERGE:
      - Riassegna in EF: `Receipts.Where(r => r.StoreId == current.Id).ForEachAsync(r => r.StoreId = target.Id)`.
      - Completa metadati del target (chain, locations se mancanti) con preferenza non distruttiva.
      - Crea alias: `current.NameNormalized` e il `newNorm` (se diverso) in `StoreAlias` del target.
      - Elimina `current` (o `IsDeleted=true`).
    - SaveChanges in batch ragionevoli per non caricare eccessivamente DB.
- Tutto in transazioni piccole per ridurre lock; in caso di concorrenza l’unico vincolo unico scatterà solo dopo la migrazione finale.

## Logica Applicativa (Rename/Merge)
- Metodo applicativo (Application/Infrastructure Service):
  - Firma: `Task<Store> RenameOrMergeAsync(Guid currentStoreId, string newName, CancellationToken ct)`.
  - Passi:
    1) `newNorm = Normalize(newName)`.
    2) Carica `current` per `currentStoreId`.
    3) Se `current.NameNormalized == newNorm` ⇒ no-op.
    4) Cerca `target = Stores.FirstOrDefault(NameNormalized == newNorm)`.
       - Se `target != null && target.Id != current.Id` ⇒ MERGE: riassegna receipts; `MergeMetadata`; aggiungi `StoreAlias` (old/current/new); rimuovi `current`; SaveChanges; return `target`.
       - Altrimenti ⇒ RENAME in-place: crea `StoreAlias` con `current.NameNormalized`; aggiorna `current.Name = newName` e `current.NameNormalized = newNorm`; SaveChanges; return `current`.
  - Concorrenza: se al SaveChanges scatta unique su `NameNormalized`, ricarica `target` e ripeti come MERGE (retry singolo).

## Integrazione API/Worker
- `POST /receipts/{id}/edit`
  - In `ReceiptEditController.UpdateReceiptStoreAsync(...)` sostituire la ricerca case-insensitive attuale con chiamata a `RenameOrMergeAsync(receipt.StoreId, storeName)` e aggiornare `receipt.StoreId`/`receipt.Store` col risultato.
- `StoresController.Create` (opzionale, qualità):
  - Prima di creare un nuovo Store, usa `Normalize` e cerca su `NameNormalized` per evitare duplicati.

## Modifiche Codice
- Domain: `backend/WIB.Domain/Receipt.cs`
  - Aggiungi `NameNormalized`, `IsDeleted?`, `Aliases` a `Store`.
  - Aggiungi classe `StoreAlias`.
- Infrastructure: `backend/WIB.Infrastructure/Data/WibDbContext.cs`
  - OnModelCreating: configurazione `Store` (max len 256, required, index unique su `NameNormalized`) e `StoreAlias` (FK + unique su `(StoreId, AliasNormalized)`).
- Infrastructure Utils (nuovo): `StringNormalizer`
  - `RemoveDiacritics`, `CollapseSpaces`, regex per alfanumerici e spazi.
- Application/Infrastructure Service (nuovo): `StoreService` o in un servizio esistente lato Infrastructure
  - Implementa `RenameOrMergeAsync` (con transazione breve ove necessario).
- API: `ReceiptEditController`
  - Usa `RenameOrMergeAsync` per StoreName.
- API (opzionale): `StoresController`
  - Usa `NameNormalized` per Create/Search.

## Testing
- Unit (xUnit, `backend/WIB.Tests`):
  - `Normalize` copre: accenti/diacritici, case, spazi multipli, punteggiatura.
  - `RenameOrMergeAsync`:
    - No-op quando stesso normalizzato.
    - Rename: crea alias oldName norm, aggiorna campi, nessun duplicato.
    - Merge: riassegna receipts, alias doppi aggiunti, elimina current.
  - Concorrenza: simulare unique conflict e verificare retry/merge.
- Integration (InMemory/Sqlite):
  - Migrazioni applicate; backfill popola `NameNormalized`; finalize vincoli; query su NameNormalized.
  - `POST /receipts/{id}/edit` con `storeName` forza rename/merge.

## Osservabilità & Sicurezza
- Logging: info per rename/merge con conteggio receipts, id current/target, alias creati; warn su collisioni e retry.
- Niente SQL raw; transazioni EF con SaveChanges batch.

## Rollout
1) Deploy migrazione Bootstrap + codice con backfill (flag ON in ambienti controllati).
2) Esegui job di backfill/merge e verifica metriche/log.
3) Deploy migrazione Finalize (unique NOT NULL) + disattiva flag backfill.

## Rischi & Mitigazioni
- Concorrenza su rename: retry/merge a vincoli.
- Dati incoerenti pregressi: backfill risolve collisioni via merge deterministico.
- Prestazioni backfill: eseguire a lotti (batch) e fuori orario di punta.

## Stima Sforzo
- Dev + migrazioni + test: 1.5–2.0 g.
- Backfill + rollout assistito: 0.5–1.0 g.

## TODO Operativi
- [ ] Domain/DbContext aggiornati (Store.NameNormalized, StoreAlias)
- [ ] Migrazione 1: Bootstrap applicata
- [ ] Backfill EF eseguito (flag) e verificato
- [ ] Migrazione 2: Finalize (unique+NOT NULL) applicata
- [ ] Service `RenameOrMergeAsync` implementato e testato
- [ ] ReceiptEditController integrato con service
- [ ] StoresController rifinito su NameNormalized (opzionale)
- [ ] Test unit/integration verdi
- [ ] README/Docs aggiornate (nota gestione Store)

