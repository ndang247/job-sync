# ENG-8 Auth Wiring Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build email OTP authentication for Angular web using `/login` and `/otp`, protect product routes, and use a cookie-backed refresh session.

**Architecture:** Backend owns refresh-token persistence through an HttpOnly cookie; Angular keeps only the short-lived access token in memory. Angular restores sessions by calling refresh on boot, attaches access tokens through an interceptor, and refreshes once on `401`.

**Tech Stack:** ASP.NET Core Identity/JWT, Angular standalone components/signals/reactive forms, RxJS, SignalR.

---

## Summary

- Build Angular email OTP auth screens from `web/job-sync/open-design/passwordless-login.html` and `passwordless-otp.html`.
- Use routes `/login` and `/otp`; protect all other routes.
- Keep refresh tokens out of JavaScript storage; backend sets/rotates/revokes an HttpOnly refresh cookie.
- Keep access token in memory only. Refresh after reload through cookie-backed endpoint.

## Key Changes

- Backend auth:
  - Change `VerifyOtp` to set `job_sync_refresh` cookie and return only access-token session data.
  - Change `Refresh` to read refresh token from cookie, rotate it, set replacement cookie, and return access-token session data.
  - Change `Logout` to read refresh cookie when present, revoke its token family, clear cookie, and return `204`.
  - Keep refresh/logout anonymous at the bearer-token level; cookie validity is the auth proof.
- Angular auth:
  - Add `AuthService`, auth interceptor, protected guard, guest guard, and shared API base URL.
  - Add `/login` and `/otp` routes; guard app routes and wildcard.
  - On startup/guard, call refresh once to restore session if no memory token exists.
  - On `401`, refresh once, retry original request, else clear session and redirect to `/login`.
- Login and OTP UI:
  - Email-only label, placeholder, copy, input type, and validation.
  - Request OTP using `/api/v1/auth/otp/request`.
  - OTP route reads pending email from router state/query/session metadata and redirects to `/login` when absent.
  - Resend countdown comes from `resendAfterSeconds`; throttled state uses `Retry-After`.
- Protected app integration:
  - Add logout button in app hero.
  - Change Gmail connect to authenticated `POST /api/v1/mail-connect/gmail/start`, then navigate to returned `authorizationUrl`.
  - Change SignalR hub setup to use `accessTokenFactory`.

## Tests

- .NET integration:
  - Verify OTP sets refresh cookie and does not expose refresh token.
  - Refresh rotates refresh cookie and rejects replay.
  - Logout clears cookie and revokes token family.
- Angular unit:
  - Login validates email only and requests OTP.
  - OTP verifies six digits, stores access token in memory, and resends with countdown.
  - Guard redirects unauthenticated users to `/login`.
  - Interceptor attaches bearer token, refreshes once on `401`, and clears session on refresh failure.
  - Gmail connect POSTs and navigates to returned URL.
- Manual:
  - Login, reload protected route, deep-link while unauthenticated, expired access refresh, logout.
  - Validate responsive login/OTP at mobile and desktop widths with no horizontal overflow.

## Assumptions

- Refresh token must not persist in JS storage.
- Dev can relax cookie `Secure`; production must require HTTPS.
- Refresh/logout requests include credentials.
- Default successful auth target is `/`; attempted protected route is preserved by `returnUrl`.
