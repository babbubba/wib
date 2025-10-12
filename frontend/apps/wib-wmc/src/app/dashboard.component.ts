import { Component, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { NgSelectModule } from '@ng-select/ng-select';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ActivatedRoute } from '@angular/router';

interface SpendingItem { year: number; month: number; amount: number; }
interface ReceiptListItem { id: string; datetime: string; storeName?: string; total: number; }
interface ReceiptDto {
  id: string;
  store: { name: string; address?: string; city?: string; postalCode?: string; vatNumber?: string; ocrX?: number|null; ocrY?: number|null; ocrW?: number|null; ocrH?: number|null; };
  datetime: string;
  currency: string;
  lines: { labelRaw: string; qty: number; unitPrice: number; lineTotal: number; vatRate?: number | null; typeId?: string|null; typeName?: string|null; ocrX?: number|null; ocrY?: number|null; ocrW?: number|null; ocrH?: number|null; }[];
  totals: { total: number; }
}
interface Suggestions { typeCandidates: { id: string; conf: number }[]; categoryCandidates: { id: string; conf: number }[] }

@Component({
  selector: 'wib-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, NgSelectModule, DragDropModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css'],
})
export class DashboardComponent implements OnInit {
  from = signal<string>(this.iso(new Date(Date.now() - 30*24*60*60*1000)));
  to = signal<string>(this.iso(new Date()));
  items = signal<SpendingItem[]>([]);
  error = signal<string>('');
  receipts = signal<ReceiptListItem[]>([]);
  selected = signal<ReceiptDto | null>(null);
  sugs = signal<Record<number, Suggestions>>({});
  imageUrl = signal<string>('');
  deleting = signal<boolean>(false);
  imageDims = signal<{ nw: number; nh: number; cw: number; ch: number }|null>(null);
  highlight = signal<{ x: number; y: number; w: number; h: number }|null>(null);
  currentOrder = signal<number[]>([]);
  // edit state
  editStoreName = signal<string>('');
  editStoreAddress = signal<string>('');
  editStoreCity = signal<string>('');
  editStorePostalCode = signal<string>('');
  editStoreVatNumber = signal<string>('');
  editDatetime = signal<string>('');
  editCurrency = signal<string>('EUR');
  editLines = signal<Array<{ labelRaw: string; qty: number; unitPrice: number; lineTotal: number; vatRate?: number | null; typeName?: string; typeId?: string|null }>>([]);
  typeSugs = signal<Record<number, { id: string; name: string }[]>>({});
  storeSugs = signal<{ id: string; name: string; address?: string; city?: string; postalCode?: string; vatNumber?: string }[]>([]);
  storeLoading = signal<boolean>(false);

  constructor(private http: HttpClient, private route: ActivatedRoute) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) this.loadReceipt(id);
  }

  iso(d: Date) { return d.toISOString().slice(0,10); }

  onQuery(ev: Event) {
    ev.preventDefault();
    const f = this.from();
    const t = this.to();
    this.error.set('');
    this.http.get<SpendingItem[]>(`/api/analytics/spending`, { params: { from: f, to: t } }).subscribe({
      next: arr => this.items.set(arr),
      error: e => this.error.set(e.message || 'Errore')
    });
  }

  onLineChange(index: number, field: 'labelRaw'|'qty'|'unitPrice'|'lineTotal'|'vatRate'|'typeName', value: any) {
    const arr = [...this.editLines()];
    const cur = { ...(arr[index] || { labelRaw: '', qty: 1, unitPrice: 0, lineTotal: 0 }) } as any;
    if (field === 'qty' || field === 'unitPrice' || field === 'lineTotal' || field === 'vatRate') cur[field] = value === '' || value === null ? null : Number(value);
    else cur[field] = value;
    arr[index] = cur; this.editLines.set(arr);
    if (field === 'typeName') {
      cur['typeId'] = null;
      const q = (value || '').toString().trim();
      if (q.length >= 2) {
        this.http.get<any[]>(`/api/producttypes/search`, { params: { query: q, take: 8 } }).subscribe({
          next: list => { const sugg = (list || []).map((x:any)=>({ id: x.id, name: x.name })); const map = { ...this.typeSugs() }; map[index] = sugg; this.typeSugs.set(map); },
          error: () => {}
        });
      } else { const map = { ...this.typeSugs() }; delete map[index]; this.typeSugs.set(map); }
    }
  }

  onTypeSearch(index: number, term: string) {
    const q = (term || '').toString().trim();
    const arr = [...this.editLines()];
    const cur = { ...(arr[index] || { labelRaw: '', qty: 1, unitPrice: 0, lineTotal: 0 }) } as any;
    cur['typeName'] = q; cur['typeId'] = null; arr[index] = cur; this.editLines.set(arr);
    if (q.length >= 2) {
      this.http.get<any[]>(`/api/producttypes/search`, { params: { query: q, take: 8 } }).subscribe({
        next: list => { const sugg = (list || []).map((x:any)=>({ id: x.id, name: x.name })); const map = { ...this.typeSugs() }; map[index] = sugg; this.typeSugs.set(map); },
        error: () => {}
      });
    } else { const map = { ...this.typeSugs() }; delete map[index]; this.typeSugs.set(map); }
  }

  ontypeIdChange(index: number, id: string | null) {
    const arr = [...this.editLines()];
    const cur = { ...(arr[index] || { labelRaw: '', qty: 1, unitPrice: 0, lineTotal: 0 }) } as any;
    cur['typeId'] = id || null;
    if (id) { const found = (this.typeSugs()[index] || []).find(x => String(x.id) === String(id)); if (found) cur['typeName'] = found.name; }
    arr[index] = cur; this.editLines.set(arr);
  }

  chooseCategory(i: number, cat: { id: string; name: string }) {
    const arr = [...this.editLines()]; const linePatch = { ...(arr[i] || {}) } as any; linePatch.typeName = cat.name; linePatch.typeId = cat.id; arr[i] = linePatch; this.editLines.set(arr); const sugg = { ...this.typeSugs() }; delete sugg[i]; this.typeSugs.set(sugg);
  }

  createType(i: number) {
    const name = i === -1 ? (this.newCatName() || '').toString().trim() : (this.editLines()[i]?.typeName || '').toString().trim();
    if (!name) return;
    this.http.post<any>(`/categories`, { name }).subscribe({
      next: res => { const createdName = res?.name || name; const createdId = res?.id || null; if (i === -1) { this.newCatName.set(createdName); this.newCatId.set(createdId || undefined); const map = { ...this.typeSugs() }; delete (map as any)[-1]; this.typeSugs.set(map); } else { const arr = [...this.editLines()]; const linePatch = { ...(arr[i] || {}) } as any; linePatch.typeName = createdName; linePatch.typeId = createdId; arr[i] = linePatch; this.editLines.set(arr); const map = { ...this.typeSugs() }; delete map[i]; this.typeSugs.set(map); } },
      error: () => {}
    });
  }

  onStoreSearch(term: string) {
    const q = (term || '').toString().trim(); this.editStoreName.set(q);
    if (q.length < 2) { this.storeSugs.set([]); return; }
    this.storeLoading.set(true);
    this.http.get<any[]>(`/api/stores/search`, { params: { query: q, take: 8 } }).subscribe({
      next: list => { this.storeSugs.set((list||[]).map((x:any)=>({ id: x.id, name: x.name, address: x.address, city: x.city, postalCode: x.postalCode, vatNumber: x.vatNumber }))); this.storeLoading.set(false); },
      error: () => { this.storeLoading.set(false); }
    });
  }

  chooseStore(s: { id: string; name: string; address?: string; city?: string; postalCode?: string; vatNumber?: string } | null) {
    if (!s) { this.storeSugs.set([]); return; }
    this.editStoreName.set(s.name); if (s.address) this.editStoreAddress.set(s.address); if (s.city) this.editStoreCity.set(s.city); if (s.postalCode) this.editStorePostalCode.set(s.postalCode); if (s.vatNumber) this.editStoreVatNumber.set(s.vatNumber); this.storeSugs.set([]);
  }

  removeLine(i: number) {
    const rec = this.selected(); if (!rec) return;
    const body = { lines: [{ index: i, remove: true }] } as any;
    this.http.post(`/api/receipts/${rec.id}/edit`, body).subscribe({ next: () => this.select({ id: rec.id, datetime: rec.datetime, storeName: rec.store.name, total: rec.totals.total } as any), error: () => this.error.set('Eliminazione riga fallita') });
  }

  loadReceipts() {
    this.http.get<ReceiptListItem[]>(`/api/receipts`, { params: { take: 10 } }).subscribe({ next: arr => this.receipts.set(arr), error: e => this.error.set(e.message || 'Errore') });
  }

  select(item: ReceiptListItem) {
    this.http.get<ReceiptDto>(`/api/receipts/${item.id}`).subscribe({
      next: r => { this.selected.set(r); this.sugs.set({}); this.editStoreName.set(r.store.name || ''); this.editDatetime.set(r.datetime ? new Date(r.datetime).toISOString().slice(0,16) : ''); this.editCurrency.set(r.currency || 'EUR'); this.editStoreAddress.set(r.store.address || ''); this.editStoreCity.set(r.store.city || ''); this.editStorePostalCode.set(r.store.postalCode || ''); this.editStoreVatNumber.set(r.store.vatNumber || ''); this.editLines.set((r.lines || []).map(l => ({ labelRaw: l.labelRaw, qty: l.qty as any, unitPrice: l.unitPrice as any, lineTotal: l.lineTotal as any, vatRate: l.vatRate as any, typeName: l.typeName || '', typeId: l.typeId || null }))); this.currentOrder.set(Array.from({length: r.lines.length}, (_,i)=>i)); },
      error: e => this.error.set(e.message || 'Errore')
    });
    this.http.get(`/api/receipts/${item.id}/image`, { responseType: 'blob' as any }).subscribe({ next: (b: any) => { if (this.imageUrl()) URL.revokeObjectURL(this.imageUrl()); this.imageUrl.set(URL.createObjectURL(b)); this.imageDims.set(null); this.highlight.set(null); }, error: e => this.error.set(e.message || 'Errore immagine') });
  }

  private loadReceipt(id: string) {
    this.http.get<ReceiptDto>(`/api/receipts/${id}`).subscribe({
      next: r => { this.selected.set(r); this.sugs.set({}); this.editStoreName.set(r.store.name || ''); this.editDatetime.set(r.datetime ? new Date(r.datetime).toISOString().slice(0,16) : ''); this.editCurrency.set(r.currency || 'EUR'); this.editStoreAddress.set(r.store.address || ''); this.editStoreCity.set(r.store.city || ''); this.editStorePostalCode.set(r.store.postalCode || ''); this.editStoreVatNumber.set(r.store.vatNumber || ''); this.editLines.set((r.lines || []).map(l => ({ labelRaw: l.labelRaw, qty: l.qty as any, unitPrice: l.unitPrice as any, lineTotal: l.lineTotal as any, vatRate: l.vatRate as any, typeName: l.typeName || '', typeId: l.typeId || null }))); this.currentOrder.set(Array.from({length: r.lines.length}, (_,i)=>i)); },
      error: e => this.error.set(e.message || 'Errore')
    });
    this.http.get(`/api/receipts/${id}/image`, { responseType: 'blob' as any }).subscribe({ next: (b: any) => { if (this.imageUrl()) URL.revokeObjectURL(this.imageUrl()); this.imageUrl.set(URL.createObjectURL(b)); this.imageDims.set(null); this.highlight.set(null); }, error: e => this.error.set(e.message || 'Errore immagine') });
  }

  suggest(labelRaw: string, idx: number) {
    this.http.get<Suggestions>(`/api/ml/suggestions`, { params: { labelRaw } }).subscribe({ next: s => this.sugs.set({ ...this.sugs(), [idx]: s }), error: e => this.error.set(e.message || 'Errore') });
  }

  feedback(labelRaw: string, finalTypeId: string | null, finaltypeId: string | null) {
    const payload: any = { labelRaw }; if (finalTypeId) payload.finalTypeId = finalTypeId; if (finaltypeId) payload.finaltypeId = finaltypeId;
    this.http.post(`/api/ml/feedback`, payload).subscribe({ next: () => {}, error: e => this.error.set(e.message || 'Errore') });
  }

  // New line creation state
  newLabel = signal<string>('');
  newQty = signal<number>(1);
  newUnitPrice = signal<number>(0);
  newLineTotal = signal<number>(0);
  newVat = signal<number|null>(null);
  newCatName = signal<string>('');
  newCatId = signal<string|undefined>(undefined);

  addNewLine() {
    const rec = this.selected(); if (!rec) return;
    const label = (this.newLabel() || '').trim(); if (!label) { this.error.set('Label mancante'); return; }
    let qty = Number(this.newQty()); let unitPrice = Number(this.newUnitPrice()); let lineTotal = Number(this.newLineTotal());
    if (Number.isNaN(qty) || qty <= 0) qty = 1; if (Number.isNaN(unitPrice)) unitPrice = 0; if (Number.isNaN(lineTotal) || lineTotal <= 0) lineTotal = unitPrice * qty;
    const body: any = { addLines: [{ labelRaw: label, qty, unitPrice, lineTotal, ...(this.newVat() == null ? {} : { vatRate: this.newVat() }), ...(this.newCatId() ? { finaltypeId: this.newCatId() } : {}), ...(((this.newCatName()||'').trim()) ? { finaltypeName: (this.newCatName()||'').trim() } : {}) }] };
    this.http.post(`/api/receipts/${rec.id}/edit`, body).subscribe({ next: () => { this.newLabel.set(''); this.newQty.set(1); this.newUnitPrice.set(0); this.newLineTotal.set(0); this.newVat.set(null); this.newCatName.set(''); this.newCatId.set(undefined); this.select({ id: rec.id, datetime: rec.datetime, storeName: rec.store.name, total: rec.totals.total } as any); }, error: e => this.error.set(this.apiErr(e, 'Aggiunta riga fallita')) });
  }

  saveEdits() {
    const rec = this.selected(); if (!rec) return;
    const linesPayload: any[] = [];
    (this.editLines() || []).forEach((l, idx) => { const orig = rec.lines[idx]; const patch: any = { index: idx }; if ((l as any).__remove) { patch.remove = true; linesPayload.push(patch); return; } if (l.labelRaw !== orig.labelRaw) patch.labelRaw = l.labelRaw; if (Number(l.qty) !== Number(orig.qty)) patch.qty = Number(l.qty); if (Number(l.unitPrice) !== Number(orig.unitPrice)) patch.unitPrice = Number(l.unitPrice); if (Number(l.lineTotal) !== Number(orig.lineTotal)) patch.lineTotal = Number(l.lineTotal); if ((l.vatRate ?? null) !== (orig.vatRate ?? null)) patch.vatRate = l.vatRate; if ((l.typeId || null) && String(l.typeId) !== String(orig.typeId||'')) patch.finaltypeId = l.typeId; else if ((l.typeName || '').trim()) patch.finaltypeName = (l.typeName || '').trim(); if (Object.keys(patch).length > 1) linesPayload.push(patch); });
    const removedIdx = linesPayload.filter(p => p.remove === true).map(p => p.index);
    let orderForApi = (this.currentOrder() || []).filter(i => removedIdx.indexOf(i) === -1); orderForApi = orderForApi.map(n => Number(n)).filter(n => Number.isInteger(n));
    const body: any = { storeName: this.editStoreName(), storeAddress: this.editStoreAddress(), storeCity: this.editStoreCity(), storePostalCode: this.editStorePostalCode(), storeVatNumber: this.editStoreVatNumber(), currency: this.editCurrency(), lines: linesPayload, order: orderForApi };
    const ts = this.editDatetime(); if (ts) body.datetime = new Date(ts).toISOString();
    this.http.post(`/api/receipts/${rec.id}/edit`, body).subscribe({ next: () => { this.error.set(''); this.select({ id: rec.id, datetime: rec.datetime, storeName: rec.store.name, total: rec.totals.total } as any); }, error: e => this.error.set(this.apiErr(e, 'Salvataggio fallito')) });
  }

  deleteSelected() {
    const rec = this.selected(); if (!rec || this.deleting()) return; const ok = confirm('Eliminare definitivamente lo scontrino selezionato?'); if (!ok) return; this.deleting.set(true);
    this.http.delete(`/api/receipts/${rec.id}`).subscribe({ next: () => { if (this.imageUrl()) { URL.revokeObjectURL(this.imageUrl()); this.imageUrl.set(''); } this.selected.set(null); this.loadReceipts(); this.deleting.set(false); }, error: e => { this.error.set(e.message || 'Eliminazione scontrino fallita'); this.deleting.set(false); } });
  }

  drop(event: CdkDragDrop<any[]>) {
    const arr = [...this.editLines()]; moveItemInArray(arr, event.previousIndex, event.currentIndex); this.editLines.set(arr);
    const ord = [...this.currentOrder()]; const [moved] = ord.splice(event.previousIndex, 1); ord.splice(event.currentIndex, 0, moved); this.currentOrder.set(ord);
  }

  onImageLoad(ev: Event) { const img = ev.target as HTMLImageElement; this.imageDims.set({ nw: img.naturalWidth, nh: img.naturalHeight, cw: img.clientWidth, ch: img.clientHeight }); }
  highlightLine(i: number) { const r = this.selected(); if (!r) return; const l = (r.lines as any[])[i]; if (l && l.ocrW && l.ocrH) this.highlight.set({ x: l.ocrX!, y: l.ocrY!, w: l.ocrW!, h: l.ocrH! }); }
  highlightStore() { const r = this.selected(); if (!r) return; const s: any = r.store; if (s && s.ocrW && s.ocrH) this.highlight.set({ x: s.ocrX!, y: s.ocrY!, w: s.ocrW!, h: s.ocrH! }); }
  clearHighlight() { this.highlight.set(null); }
  highlightStyle() { const dims = this.imageDims(); const box = this.highlight(); if (!dims || !box) return { display: 'none' } as any; const sx = dims.cw / dims.nw; const sy = dims.ch / dims.nh; const l = Math.round(box.x * sx), t = Math.round(box.y * sy); const w = Math.round(box.w * sx), h = Math.round(box.h * sy); return { display: 'block', left: l+'px', top: t+'px', width: w+'px', height: h+'px' } as any; }

  private apiErr = (e: any, fallback: string): string => { try { const status = e && (e as any).status; const bodyMsg = typeof (e && (e as any).error) === 'string' ? (e as any).error : (((e && (e as any).error && (e as any).error.message) || '')); const msg = bodyMsg || (e && (e as any).message) || ''; if (status === 409) return 'Conflitto: lo scontrino e stato modificato da un altro utente. Ricarica e riprova.'; if (status === 400) return 'Richiesta non valida'; return msg || fallback; } catch { return fallback; } };
}

