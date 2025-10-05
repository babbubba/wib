import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.getToken();
  if (token) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  return next(req).pipe(
    catchError((err: any) => {
      const isAuthCall = req.url.includes('/auth/token');
      if (err instanceof HttpErrorResponse && err.status === 401 && !isAuthCall) {
        // token missing/expired or invalid; redirect to login preserving returnUrl
        const returnUrl = router.routerState.snapshot.url || '/';
        auth.logoutAndRedirect(returnUrl);
      }
      return throwError(() => err);
    })
  );
};
