---
labels: [frontend, ui, enhancement, fase-4]
---

# TASK 7: Receipt Review UI - Drag & Drop Righe

## 🎯 Obiettivo
Implementa drag & drop per riordinare righe scontrino in UI WMC con persistenza ordine.

## 📋 Requisiti
1. Aggiungi campo ReceiptLine.DisplayOrder (int, nullable) al DB:
   - Migration EF Core: `dotnet ef migrations add AddDisplayOrder -s backend/WIB.API -p backend/WIB.Infrastructure`
2. Aggiorna GET /receipts/{id} per restituire righe ordinate per DisplayOrder (se presente) poi Id
3. Frontend: integra Angular CDK Drag-Drop in receipt-detail.component:
   - cdkDropList con [cdkDropListData]="lines"
   - (cdkDropListDropped)="onLineDrop($event)"
   - Aggiorna array locale e chiama PATCH /receipts/{id}/reorder
4. Backend: nuovo endpoint PATCH /receipts/{id}/reorder con body `{ lineOrders: [{lineId, order}] }`

## ✅ Criteri di Successo
- [ ] `dotnet ef database update -s backend/WIB.API -p backend/WIB.Infrastructure` applica migration
- [ ] `npm run test:wmc` passa con test per drag-drop component
- [ ] UI: drag riga in receipt detail, salva, ricarica pagina → ordine mantenuto
- [ ] `curl -X PATCH http://localhost:8085/receipts/{id}/reorder -d '{"lineOrders":[...]}'` aggiorna ordine
- [ ] `docker compose logs api` non mostra errori

## 📁 File da Modificare
- `backend/WIB.Domain/Receipt.cs` (aggiungi DisplayOrder a ReceiptLine)
- `backend/WIB.Infrastructure/Data/Migrations/` (nuova migration)
- `backend/WIB.API/Controllers/ReceiptsController.cs` (nuovo endpoint PATCH)
- `frontend/apps/wib-wmc/src/app/pages/receipt-detail/receipt-detail.component.ts`
- `frontend/apps/wib-wmc/src/app/pages/receipt-detail/receipt-detail.component.html`
- `package.json` (aggiungi @angular/cdk se mancante)

## 🏷️ Fase
FASE 4: Frontend e UX