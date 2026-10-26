import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

/**
 * Sends a 401 to the login page and passes the failure on, so the screen that asked still settles.
 * Safe for every call, because the server answers 401 for one reason only: no login cookie. A
 * refusal from Jira or Azure DevOps arrives as a 400 with a code of its own.
 */
export const unauthorizedInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      // The address bar rather than router.url: the settings initializer and the shell's own loads
      // run before the router's first navigation, when router.url still says "/" on /login.
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        window.location.pathname !== '/login'
      ) {
        void router.navigateByUrl('/login');
      }

      return throwError(() => error);
    }),
  );
};
