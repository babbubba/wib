import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';

interface LoginResponse { 
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: {
    id: string;
    username: string;
    email: string;
    firstName: string;
    lastName: string;
    fullName: string;
    roles: string[];
  };
}

interface RefreshRequest {
  refreshToken: string;
}

interface LoginRequest {
  username: string;
  password: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private accessTokenKey = 'wib_access_token';
  private refreshTokenKey = 'wib_refresh_token';
  private userKey = 'wib_user';

  constructor() {
    const existingToken = this.getToken();
    if (existingToken) {
      this.setAuthCookie(existingToken);
    }
  }

  getToken(): string | null { return localStorage.getItem(this.accessTokenKey); }
  setToken(token: string, expiresAt?: string) {
    localStorage.setItem(this.accessTokenKey, token);
    this.setAuthCookie(token, expiresAt);
  }
  clearToken() { 
    localStorage.removeItem(this.accessTokenKey); 
    localStorage.removeItem(this.refreshTokenKey);
    localStorage.removeItem(this.userKey);
    this.setAuthCookie(null);
  }
  
  getRefreshToken(): string | null { return localStorage.getItem(this.refreshTokenKey); }
  setRefreshToken(token: string) { localStorage.setItem(this.refreshTokenKey, token); }
  
  getUser(): any { 
    const user = localStorage.getItem(this.userKey);
    return user ? JSON.parse(user) : null;
  }
  setUser(user: any) { localStorage.setItem(this.userKey, JSON.stringify(user)); }

  isTokenExpired(token: string | null): boolean {
    if (!token) return true;
    const payload = this.tryDecodeJwt(token);
    if (!payload || typeof payload.exp !== 'number') return false;
    const nowSec = Math.floor(Date.now() / 1000);
    return payload.exp <= nowSec;
  }

  private base64UrlDecode(s: string): string {
    s = s.replace(/-/g, '+').replace(/_/g, '/');
    const pad = s.length % 4; if (pad) s += '='.repeat(4 - pad);
    return decodeURIComponent(Array.prototype.map.call(atob(s), (c: string) =>
      '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2)
    ).join(''));
  }

  private tryDecodeJwt(token: string): any | null {
    try {
      const parts = token.split('.');
      if (parts.length < 2) return null;
      return JSON.parse(this.base64UrlDecode(parts[1]));
    } catch {
      return null;
    }
  }

  private setAuthCookie(token: string | null, expiresAt?: string) {
    if (typeof document === 'undefined') {
      return;
    }

    const attributes = this.getCookieAttributes();

    if (!token) {
      document.cookie = `${this.accessTokenKey}=; Max-Age=0${attributes}`;
      return;
    }

    let expiry: Date | null = null;

    if (expiresAt) {
      const parsed = new Date(expiresAt);
      if (!Number.isNaN(parsed.getTime())) {
        expiry = parsed;
      }
    }

    if (!expiry) {
      const payload = this.tryDecodeJwt(token);
      if (payload && typeof payload.exp === 'number') {
        expiry = new Date(payload.exp * 1000);
      }
    }

    if (!expiry) {
      expiry = new Date(Date.now() + 60 * 60 * 1000);
    }

    document.cookie = `${this.accessTokenKey}=${token}; expires=${expiry.toUTCString()}${attributes}`;
  }

  private getCookieAttributes(): string {
    const secure = typeof window !== 'undefined' && window.location.protocol === 'https:' ? '; Secure' : '';
    return `; path=/; SameSite=Lax${secure}`;
  }

  login(username: string, password: string): Observable<LoginResponse> {
    const request: LoginRequest = { username, password };
    return this.http.post<LoginResponse>('/api/auth/login', request).pipe(
      tap(response => {
        if (response?.accessToken) {
          this.setToken(response.accessToken, response.expiresAt);
          this.setRefreshToken(response.refreshToken);
          this.setUser(response.user);
        }
      })
    );
  }

  refreshToken(): Observable<LoginResponse> {
    const refreshToken = this.getRefreshToken();
    if (!refreshToken) {
      throw new Error('No refresh token available');
    }
    
    const request: RefreshRequest = { refreshToken };
    return this.http.post<LoginResponse>('/api/auth/refresh', request).pipe(
      tap(response => {
        if (response?.accessToken) {
          this.setToken(response.accessToken, response.expiresAt);
          this.setRefreshToken(response.refreshToken);
          this.setUser(response.user);
        }
      })
    );
  }

  logout(): Observable<any> {
    const refreshToken = this.getRefreshToken();
    if (refreshToken) {
      return this.http.post('/api/auth/logout', { refreshToken }).pipe(
        tap(() => this.clearToken())
      );
    } else {
      this.clearToken();
      return new Observable(subscriber => {
        subscriber.next(null);
        subscriber.complete();
      });
    }
  }

  logoutAndRedirect(returnUrl?: string) {
    this.logout().subscribe({
      next: () => {
        const extras = returnUrl ? { queryParams: { returnUrl } } : undefined as any;
        this.router.navigate(['/login'], extras);
      },
      error: () => {
        // Even if logout fails on server, clear local tokens and redirect
        this.clearToken();
        const extras = returnUrl ? { queryParams: { returnUrl } } : undefined as any;
        this.router.navigate(['/login'], extras);
      }
    });
  }
}
