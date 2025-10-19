import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError, of } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.getToken();
  
  // Add authorization header to all requests if token exists
  if (token && !auth.isTokenExpired(token)) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  
  return next(req).pipe(
    catchError((err: any) => {
      const isLoginCall = req.url.includes('/api/auth/login');
      
      if (err instanceof HttpErrorResponse && err.status === 401 && !isLoginCall) {
        // Token expired, try to refresh
        const refreshToken = auth.getRefreshToken();
        
        if (refreshToken) {
          return auth.refreshToken().pipe(
            switchMap((response) => {
              // Retry original request with new token
              const newReq = req.clone({ 
                setHeaders: { Authorization: `Bearer ${response.accessToken}` } 
              });
              return next(newReq);
            }),
            catchError((refreshErr) => {
              // Refresh failed, redirect to login
              const returnUrl = router.routerState.snapshot.url || '/';
              auth.logoutAndRedirect(returnUrl);
              return throwError(() => refreshErr);
            })
          );
        } else {
          // No refresh token, redirect to login
          const returnUrl = router.routerState.snapshot.url || '/';
          auth.logoutAndRedirect(returnUrl);
        }
      }
      
      return throwError(() => err);
    })
  );
};
