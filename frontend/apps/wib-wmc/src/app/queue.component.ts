import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';

interface QueueStatus { length: number; pending: string[] }
interface StorageList { keys: string[] }

@Component({
  selector: 'wib-queue',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './queue.component.html',
  styleUrls: ['./queue.component.css'],
})
export class QueueComponent implements OnInit, OnDestroy {
  length = signal<number>(0);
  pending = signal<string[]>([]);
  objectKey = signal<string>('');
  take = signal<number>(10);
  private timer: any;
  prefix = signal<string>('');
  unprocessedOnly = signal<boolean>(true);
  storeKeys = signal<string[]>([]);
  selected = signal<Record<string, boolean>>({});
  logs = signal<string[]>([]);
  previews = signal<Record<string, string>>({});

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.refresh();
    this.timer = setInterval(() => this.refresh(true), 5000);
  }
  ngOnDestroy(): void { if (this.timer) clearInterval(this.timer); }

  refresh(silent=false) {
    this.http.get<QueueStatus>('/api/queue/status', { params: { take: this.take().toString() } }).subscribe({
      next: (s) => { this.length.set(s.length); this.pending.set(s.pending || []); },
      error: (e) => { if (!silent) console.error(e); }
    });
    const p: any = { take: String(Math.max(10, this.take())) };
    if (this.prefix()) p.prefix = this.prefix();
    const url = this.unprocessedOnly() ? '/api/storage/receipts/unprocessed' : '/api/storage/receipts';
    this.http.get<StorageList>(url, { params: p }).subscribe({
      next: (res) => {
        const keys = res.keys || [];
        const prev = this.previews();
        Object.values(prev).forEach(u => { try { URL.revokeObjectURL(u); } catch {} });
        this.previews.set({});
        this.storeKeys.set(keys);
        keys.forEach(k => this.fetchPreview(k));
      },
      error: (e) => { if (!silent) console.error(e); }
    });
  }

  reprocessKey(k: string) {
    this.http.post('/api/queue/reprocess', { objectKey: k }).subscribe({ next: () => { this.appendLog(`Enqueued ${k}`); this.refresh(true); }, error: (e)=> this.appendLog(`Failed ${k}: ${e.message||e}`) });
  }

  reprocessObject(ev: Event) {
    ev.preventDefault();
    const k = this.objectKey().trim();
    if (!k) return;
    this.http.post('/api/queue/reprocess', { objectKey: k }).subscribe({ next: () => { this.appendLog(`Enqueued ${k}`); this.objectKey.set(''); this.refresh(true); }, error: (e)=> this.appendLog(`Failed ${k}: ${e.message||e}`) });
  }

  toggleAll(ev: Event) {
    const checked = (ev.target as HTMLInputElement).checked;
    const map: Record<string, boolean> = {};
    this.storeKeys().forEach(k => map[k] = checked);
    this.selected.set(map);
  }

  bulkEnqueue() {
    const keys = Object.entries(this.selected()).filter(([,v])=>!!v).map(([k])=>k);
    if (!keys.length) return;
    this.http.post('/api/storage/reprocess/bulk', { objectKeys: keys }).subscribe({
      next: (res: any) => { this.appendLog(`Enqueued ${res?.enqueued ?? keys.length} items`); this.refresh(true); },
      error: (e) => this.appendLog(`Bulk enqueue failed: ${e.message||e}`)
    });
  }

  appendLog(msg: string) {
    const arr = this.logs().slice(-49); arr.push(`${new Date().toLocaleTimeString()} ${msg}`); this.logs.set(arr);
  }

  onSelectedChange(key: string, checked: boolean) {
    const map = { ...this.selected() } as Record<string, boolean>;
    map[key] = checked;
    this.selected.set(map);
  }

  isSelected(key: string) { return !!this.selected()[key]; }
  onToggleSelection(key: string, ev: Event) {
    const checked = (ev.target as HTMLInputElement).checked;
    this.onSelectedChange(key, checked);
  }

  onTakeChange(ev: Event) {
    const v = Number((ev.target as HTMLInputElement).value);
    this.take.set(isNaN(v) ? 10 : v);
    this.refresh();
  }

  onToggleUnprocessed(ev: Event) {
    const checked = (ev.target as HTMLInputElement).checked;
    this.unprocessedOnly.set(checked);
    this.refresh(true);
  }

  logText() { return (this.logs() || []).join('\n'); }

  fetchPreview(key: string) {
    if (!key) return;
    if (this.previews()[key]) return;
    this.http.get('/api/storage/object', { params: { objectKey: key }, responseType: 'blob' as any }).subscribe({
      next: (blob: any) => {
        const url = URL.createObjectURL(blob);
        this.previews.set({ ...this.previews(), [key]: url });
      },
      error: (e) => this.appendLog(`Preview failed for ${key}: ${e.message||e}`)
    });
  }
  previewSrc(key: string) { return this.previews()[key] || ''; }
}
