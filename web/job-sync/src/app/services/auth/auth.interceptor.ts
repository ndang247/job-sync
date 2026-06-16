import { HttpErrorResponse, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { API_BASE_URL } from '../api-config';
import { AuthService } from './auth';
import { SKIP_AUTH } from './auth-context';

export const authInterceptor: HttpInterceptorFn = (
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
) => {
  const auth = inject(AuthService);

  if (request.context.get(SKIP_AUTH) || !request.url.startsWith(API_BASE_URL)) {
    return next(request);
  }

  const authorizedRequest = withAccessToken(request, auth.getAccessToken());

  return next(authorizedRequest).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return auth.refreshSession(true).pipe(
        switchMap((refreshed) => {
          const token = auth.getAccessToken();
          if (!refreshed || !token) {
            auth.handleUnauthorized();
            return throwError(() => error);
          }

          return next(withAccessToken(request, token));
        }),
        catchError(() => {
          auth.handleUnauthorized();
          return throwError(() => error);
        }),
      );
    }),
  );
};

function withAccessToken(
  request: HttpRequest<unknown>,
  accessToken: string | null,
): HttpRequest<unknown> {
  const requestWithCredentials = request.clone({ withCredentials: true });
  if (!accessToken) return requestWithCredentials;

  return requestWithCredentials.clone({
    setHeaders: {
      Authorization: `Bearer ${accessToken}`,
    },
  });
}
