import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, map, tap } from 'rxjs';

interface LoginResponse { 
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: {
    id: string;
    email: string;
    firstName: string;
    lastName: string;
    fullName: string;
  };
}

interface RefreshRequest {
  refreshToken: string;
}

interface LoginRequest {
  email: string;
  password: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private accessTokenKey = 'wib_access_token';
  private refreshTokenKey = 'wib_refresh_token';
  private userKey = 'wib_user';

  getToken(): string | null { return localStorage.getItem(this.accessTokenKey); }
  setToken(token: string) { localStorage.setItem(this.accessTokenKey, token); }
  clearToken() { 
    localStorage.removeItem(this.accessTokenKey); 
    localStorage.removeItem(this.refreshTokenKey);
    localStorage.removeItem(this.userKey);
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

  login(email: string, password: string): Observable<LoginResponse> {
    const request: LoginRequest = { email, password };
    return this.http.post<LoginResponse>('/api/auth/login', request).pipe(
      tap(response => {
        if (response?.accessToken) {
          this.setToken(response.accessToken);
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
          this.setToken(response.accessToken);
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
