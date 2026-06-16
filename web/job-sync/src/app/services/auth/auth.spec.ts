import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '../api-config';
import { AuthService } from './auth';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('requests an email OTP and stores resend metadata only', () => {
    service.requestOtp('person@example.com').subscribe();

    const request = http.expectOne(`${API_BASE_URL}/api/v1/auth/otp/request`);
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toEqual({ email: 'person@example.com' });
    request.flush({
      message: 'If the address can receive email, a verification code has been sent.',
      expiresInSeconds: 300,
      resendAfterSeconds: 60,
    });

    expect(service.pendingEmail()).toBe('person@example.com');
    expect(service.pendingResendSeconds()).toBeGreaterThan(0);
    expect(localStorage.length).toBe(0);
  });

  it('verifies an OTP and keeps the access token in memory', () => {
    service.setPendingOtp('person@example.com', 60);

    service.verifyOtp('person@example.com', '123456').subscribe();

    const request = http.expectOne(`${API_BASE_URL}/api/v1/auth/otp/verify`);
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    request.flush({
      tokenType: 'Bearer',
      accessToken: 'access-token',
      expiresInSeconds: 900,
      refreshTokenExpiresAt: '2026-06-17T00:00:00Z',
    });

    expect(service.getAccessToken()).toBe('access-token');
    expect(service.pendingEmail()).toBeNull();
    expect(localStorage.length).toBe(0);
  });

  it('refreshes the session using credentials', () => {
    service.refreshSession().subscribe((authenticated) => {
      expect(authenticated).toBe(true);
    });

    const request = http.expectOne(`${API_BASE_URL}/api/v1/auth/token/refresh`);
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    request.flush({
      tokenType: 'Bearer',
      accessToken: 'rotated-access-token',
      expiresInSeconds: 900,
      refreshTokenExpiresAt: '2026-06-17T00:00:00Z',
    });

    expect(service.getAccessToken()).toBe('rotated-access-token');
  });
});
