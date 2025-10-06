import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, map, tap } from 'rxjs';

interface LoginResponse { accessToken?: string; AccessToken?: string; token?: string; [k: string]: any }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private storageKey = 'wib_token';

  getToken(): string | null { return localStorage.getItem(this.storageKey); }
  setToken(t: string) { localStorage.setItem(this.storageKey, t); }
  clearToken() { localStorage.removeItem(this.storageKey); }

  isTokenExpired(token: string | null): boolean {
    if (!token) return true;
    try {
      const parts = token.split('.');
      if (parts.length < 2) return false; // not a JWT, assume no exp info
      const payload = JSON.parse(this.base64UrlDecode(parts[1]));
      if (!payload || typeof payload.exp !== 'number') return false;
      const nowSec = Math.floor(Date.now() / 1000);
      return payload.exp <= nowSec;
    } catch {
      return false; // be permissive if can't decode
    }
  }

  private base64UrlDecode(s: string): string {
    s = s.replace(/-/g, '+').replace(/_/g, '/');
    const pad = s.length % 4; if (pad) s += '='.repeat(4 - pad);
    return decodeURIComponent(Array.prototype.map.call(atob(s), (c: string) =>
      '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2)
    ).join(''));
  }

  login(username: string, password: string): Observable<string> {
    return this.http.post<LoginResponse>('/api/auth/token', { username, password }).pipe(
      map(r => (r?.accessToken || r?.AccessToken || r?.token || '') as string),
      tap(t => { if (t) this.setToken(t); })
    );
  }

  logoutAndRedirect(returnUrl?: string) {
    this.clearToken();
    const extras = returnUrl ? { queryParams: { returnUrl } } : undefined as any;
    this.router.navigate(['/login'], extras);
  }
}
