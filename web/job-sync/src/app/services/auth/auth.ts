import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, finalize, map, Observable, of, shareReplay, tap } from 'rxjs';
import { API_BASE_URL } from '../api-config';
import { SKIP_AUTH } from './auth-context';

export interface OtpRequestedResponse {
  message: string;
  expiresInSeconds: number;
  resendAfterSeconds: number;
}

export interface AuthSessionResponse {
  tokenType: string;
  accessToken: string;
  expiresInSeconds: number;
  refreshTokenExpiresAt: string;
}

export interface AuthError {
  code?: string;
  error?: string;
}

const PENDING_EMAIL_KEY = 'jobSyncPendingOtpEmail';
const PENDING_RESEND_KEY = 'jobSyncPendingOtpResendAt';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private readonly document = inject(DOCUMENT);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly accessToken = signal<string | null>(null);
  private readonly tokenExpiresAt = signal<number | null>(null);
  private refreshRequest: Observable<boolean> | null = null;

  readonly authenticated = computed(() => {
    const token = this.accessToken();
    const expiresAt = this.tokenExpiresAt();
    return Boolean(token && expiresAt && Date.now() < expiresAt);
  });

  requestOtp(email: string): Observable<OtpRequestedResponse> {
    return this.http
      .post<OtpRequestedResponse>(
        `${API_BASE_URL}/api/v1/auth/otp/request`,
        { email },
        { context: this.skipAuthContext(), withCredentials: true },
      )
      .pipe(
        tap((response) => {
          this.setPendingOtp(email, response.resendAfterSeconds);
        }),
      );
  }

  verifyOtp(email: string, code: string): Observable<void> {
    return this.http
      .post<AuthSessionResponse>(
        `${API_BASE_URL}/api/v1/auth/otp/verify`,
        { email, code },
        { context: this.skipAuthContext(), withCredentials: true },
      )
      .pipe(
        tap((response) => {
          this.storeSession(response);
          this.clearPendingOtp();
        }),
        map(() => undefined),
      );
  }

  refreshSession(force = false): Observable<boolean> {
    if (!force && this.hasUsableAccessToken()) return of(true);
    if (this.refreshRequest) return this.refreshRequest;

    this.refreshRequest = this.http
      .post<AuthSessionResponse>(
        `${API_BASE_URL}/api/v1/auth/token/refresh`,
        {},
        { context: this.skipAuthContext(), withCredentials: true },
      )
      .pipe(
        tap((response) => {
          this.storeSession(response);
        }),
        map(() => true),
        catchError(() => {
          this.clearSession();
          return of(false);
        }),
        finalize(() => {
          this.refreshRequest = null;
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.refreshRequest;
  }

  logout(): Observable<void> {
    return this.http
      .post<void>(
        `${API_BASE_URL}/api/v1/auth/logout`,
        {},
        { context: this.skipAuthContext(), withCredentials: true },
      )
      .pipe(
        catchError(() => of(undefined)),
        tap(() => {
          this.clearSession();
        }),
        map(() => undefined),
      );
  }

  logoutAndRedirect(): void {
    this.logout().subscribe({
      next: () => void this.router.navigateByUrl('/login'),
    });
  }

  getAccessToken(): string | null {
    return this.hasUsableAccessToken() ? this.accessToken() : null;
  }

  getAccessTokenForRealtime(): Promise<string> {
    const token = this.getAccessToken();
    if (token) return Promise.resolve(token);

    return new Promise((resolve) => {
      this.refreshSession().subscribe({
        next: () => resolve(this.getAccessToken() ?? ''),
        error: () => resolve(''),
      });
    });
  }

  handleUnauthorized(): void {
    this.clearSession();
    void this.router.navigate(['/login'], {
      queryParams: { returnUrl: this.currentPath() },
    });
  }

  storePendingEmail(email: string): void {
    this.setSessionItem(PENDING_EMAIL_KEY, email);
  }

  pendingEmail(): string | null {
    return this.getSessionItem(PENDING_EMAIL_KEY);
  }

  pendingResendSeconds(): number {
    const resendAt = Number(this.getSessionItem(PENDING_RESEND_KEY));
    if (!Number.isFinite(resendAt)) return 0;
    return Math.max(0, Math.ceil((resendAt - Date.now()) / 1000));
  }

  setPendingOtp(email: string, resendAfterSeconds: number): void {
    this.setSessionItem(PENDING_EMAIL_KEY, email);
    this.setSessionItem(PENDING_RESEND_KEY, String(Date.now() + resendAfterSeconds * 1000));
  }

  clearPendingOtp(): void {
    this.removeSessionItem(PENDING_EMAIL_KEY);
    this.removeSessionItem(PENDING_RESEND_KEY);
  }

  private storeSession(response: AuthSessionResponse): void {
    this.accessToken.set(response.accessToken);
    this.tokenExpiresAt.set(Date.now() + response.expiresInSeconds * 1000);
  }

  private clearSession(): void {
    this.accessToken.set(null);
    this.tokenExpiresAt.set(null);
  }

  private hasUsableAccessToken(): boolean {
    const token = this.accessToken();
    const expiresAt = this.tokenExpiresAt();
    return Boolean(token && expiresAt && Date.now() < expiresAt - 10_000);
  }

  private skipAuthContext(): HttpContext {
    return new HttpContext().set(SKIP_AUTH, true);
  }

  private currentPath(): string {
    const window = this.document.defaultView;
    if (!window) return '/';
    return `${window.location.pathname}${window.location.search}`;
  }

  private setSessionItem(key: string, value: string): void {
    try {
      this.document.defaultView?.sessionStorage.setItem(key, value);
    } catch {
      // Auth still works for current navigation if session storage is unavailable.
    }
  }

  private getSessionItem(key: string): string | null {
    try {
      return this.document.defaultView?.sessionStorage.getItem(key) ?? null;
    } catch {
      return null;
    }
  }

  private removeSessionItem(key: string): void {
    try {
      this.document.defaultView?.sessionStorage.removeItem(key);
    } catch {
      // Nothing to clear when storage is unavailable.
    }
  }
}

export function readAuthError(error: unknown): AuthError {
  if (error instanceof HttpErrorResponse && typeof error.error === 'object' && error.error) {
    return error.error as AuthError;
  }

  return {};
}
