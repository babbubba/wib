import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpEventType } from '@angular/common/http';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css'],
})
export class AppComponent {
  file = signal<File | null>(null);
  fileName = signal<string>('');
  uploading = signal<boolean>(false);
  progress = signal<number>(0);
  result = signal<string>('');
  error = signal<string>('');
  preview = signal<string>('');
  successId = signal<string>('');

  constructor(private http: HttpClient) {}

  onFile(ev: Event) {
    const input = ev.target as HTMLInputElement;
    const f = input.files && input.files[0];
    if (f) {
      this.file.set(f);
      this.fileName.set(f.name);
      this.result.set('');
      this.error.set('');
      if (this.preview()) URL.revokeObjectURL(this.preview());
      this.preview.set(URL.createObjectURL(f));
    }
  }

  upload() {
    if (!this.file()) return;
    this.uploading.set(true);
    this.progress.set(0);
    this.compressImage(this.file()!, 2048, 0.85).then((blob) => {
      const form = new FormData();
      const fname = this.fileName() || 'receipt.jpg';
      form.append('file', new File([blob], fname, { type: 'image/jpeg' }));
      this.http.post(`/receipts`, form, { observe: 'events', reportProgress: true }).subscribe({
        next: (evt) => {
          if (evt.type === HttpEventType.UploadProgress && evt.total) {
            const pct = Math.round((evt.loaded / evt.total) * 100);
            this.progress.set(pct);
          } else if (evt.type === HttpEventType.Response) {
            const r = evt.body as any;
            this.result.set(r?.objectKey || '');
            this.successId.set(r?.objectKey || '');
            this.uploading.set(false);
            this.progress.set(100);
          }
        },
        error: (e) => {
          this.error.set(e.message || 'Errore');
          this.uploading.set(false);
        }
      });
    }).catch((e) => { this.error.set('Errore compressione'); this.uploading.set(false); });
  }

  newReceipt() {
    // reset state to allow selecting a new file
    this.file.set(null);
    this.fileName.set('');
    this.preview() && URL.revokeObjectURL(this.preview());
    this.preview.set('');
    this.result.set('');
    this.successId.set('');
    this.progress.set(0);
    this.error.set('');
  }

  backToCamera() {
    // Similar to newReceipt; keeps UX simple
    this.newReceipt();
    // Optionally, scroll to top to the capture button
    try { window.scrollTo({ top: 0, behavior: 'smooth' }); } catch {}
  }

  private async compressImage(file: File, maxLongSide = 2048, quality = 0.85): Promise<Blob> {
    const imgBitmap = await createImageBitmap(file).catch(async () => {
      return new Promise<HTMLImageElement>((resolve, reject) => {
        const fr = new FileReader();
        fr.onerror = () => reject(fr.error);
        fr.onload = () => {
          const img = new Image();
          img.onload = () => resolve(img);
          img.onerror = reject;
          img.src = fr.result as string;
        };
        fr.readAsDataURL(file);
      });
    });
    const w = (imgBitmap as any).width ?? (imgBitmap as HTMLImageElement).naturalWidth;
    const h = (imgBitmap as any).height ?? (imgBitmap as HTMLImageElement).naturalHeight;
    const scale = Math.min(1, maxLongSide / Math.max(w, h));
    const tw = Math.round(w * scale);
    const th = Math.round(h * scale);
    const canvas = document.createElement('canvas');
    canvas.width = tw; canvas.height = th;
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('canvas');
    if ('close' in (imgBitmap as any)) {
      ctx.drawImage(imgBitmap as ImageBitmap, 0, 0, tw, th);
      (imgBitmap as ImageBitmap).close();
    } else {
      ctx.drawImage(imgBitmap as HTMLImageElement, 0, 0, tw, th);
    }
    return await new Promise<Blob>((resolve, reject) => {
      canvas.toBlob((b) => b ? resolve(b) : reject(new Error('toBlob')), 'image/jpeg', quality);
    });
  }
}
