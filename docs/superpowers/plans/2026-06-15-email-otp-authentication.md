# ENG-6 Email OTP Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add production-ready passwordless email authentication, rotating refresh tokens, and authenticated per-user API ownership.

**Architecture:** Keep ASP.NET Core Identity as the user store. Use portable OTP generation, hashing, and in-memory storage services; deliver OTPs through a MailKit-backed email abstraction; issue short-lived JWT access tokens; and persist only hashed, rotating refresh tokens. Protect existing user APIs and bind Gmail OAuth and SignalR access to the authenticated Identity user.

**Tech Stack:** .NET 10, ASP.NET Core Identity, JWT bearer authentication, EF Core 10, PostgreSQL, MailKit 4.17.0, `IMemoryCache`, xUnit, `WebApplicationFactory`

---

### Task 1: Establish the feature branch and API contracts

- [x] Create `feat/email-otp-auth` from `main` after the Identity foundation was merged.
- [x] Add immutable request/response contracts for OTP request, OTP verification, token refresh, logout, and issued token pairs.
- [x] Add integration tests for request validation and generic accepted responses before implementing the endpoints.

### Task 2: Implement portable OTP services

- [x] Add `IOneTimeCodeGenerator`, `IOneTimeCodeHasher`, and `IOneTimeCodeStore`.
- [x] Generate six numeric digits with `RandomNumberGenerator`.
- [x] Hash with HMAC-SHA256 using a configured pepper and normalized destination/purpose.
- [x] Store only hashes in `IMemoryCache` with five-minute expiry, five attempts, 60-second resend cooldown, newest-code replacement, and atomic one-time consumption.
- [x] Inject `TimeProvider` and test generation, binding, expiry, attempts, cooldown, replacement, and concurrency.

### Task 3: Add MailKit OTP delivery

- [x] Add `IEmailSender` and a MailKit implementation using `smtp.gmail.com:587` with STARTTLS.
- [x] Configure sender `nathandang73@gmail.com` and app-password authentication through startup-validated options.
- [x] Keep credentials in user secrets or environment variables and ensure logs never include the OTP or SMTP credential.
- [x] Replace the sender with a capturing fake in integration tests and verify delivery failure removes the cached OTP.

### Task 4: Implement OTP request and verification

- [x] Add `POST /auth/otp/request` with email normalization, destination cooldown, and an IP limit of five requests per ten minutes.
- [x] Always return generic `202 Accepted` for successful delivery without revealing account existence.
- [x] Add `POST /auth/otp/verify` with generic `401 OTP_INVALID_OR_EXPIRED` failures.
- [x] Create a new Identity user only after successful verification, mark email confirmed, and handle concurrent first-user creation.
- [x] Set `RequireUniqueEmail = true` and add a filtered unique normalized-email index.

### Task 5: Add JWT and refresh-token sessions

- [x] Add startup-validated JWT options and configure JWT bearer authentication.
- [x] Issue 15-minute HS256 access tokens containing `sub`, `email`, and `jti`.
- [x] Add a refresh-token entity containing only token hash, user ID, family ID, expiry, replacement, and revocation metadata.
- [x] Return 30-day refresh tokens from successful OTP verification.
- [x] Add `POST /auth/token/refresh` with transactional rotation and replay-family revocation.
- [x] Add idempotent `POST /auth/logout` that revokes the token family and returns `204`.
- [x] Test claims, expiry, rotation, replay detection, revocation, and logout.

### Task 6: Enforce authenticated ownership

- [x] Require bearer authentication for applications, connections, sync operations, Gmail connection start, and SignalR.
- [x] Resolve user ID only from the validated `sub` claim.
- [x] Scope all application, connection, and sync-job reads and mutations to the authenticated user.
- [x] Return `404` for resources owned by another user.
- [x] Add user ID to application cache keys and require user ID in mutation service methods.
- [x] Authenticate SignalR and verify sync-job ownership before joining progress groups.
- [x] Test unauthenticated access and cross-user isolation for each protected surface.

### Task 7: Restore Gmail OAuth with authenticated ownership

- [x] Replace Gmail start with authenticated `POST /api/v1/mail-connect/gmail/start` returning an authorization URL.
- [x] Cache a cryptographically random, single-use OAuth state mapped to the authenticated user for ten minutes.
- [x] Keep the Google callback anonymous but require and consume valid state.
- [x] Attach or update the Gmail connection only for the mapped user.
- [x] Enforce global uniqueness of `(Provider, SubjectId)` and reject attempts to attach another user's Gmail identity.
- [x] Remove `userId` from callback redirects and test state expiry, reuse, ownership conflicts, and redirects.

### Task 8: Migration and full verification

- [x] Generate the PostgreSQL migration for refresh tokens, unique normalized email, and global Gmail subject ownership.
- [x] Inspect the migration and an idempotent SQL script for unintended data loss.
- [x] Run focused authentication and authorization tests.
- [x] Run `dotnet test api-web/api-web.slnx -m:1 /nodeReuse:false`.
- [x] Run `dotnet build api-web/api-web.slnx --no-restore -m:1 /nodeReuse:false`.
- [x] Run `git diff --check`.
- [x] Confirm no raw OTP, refresh token, JWT key, or SMTP credential is persisted or logged.

## Assumptions

- OTP cache is intentionally single-instance; distributed deployment requires a later shared-cache implementation.
- Refresh tokens are returned in JSON for web and mobile clients.
- New users initially have empty first and last names.
- Gmail SMTP initially uses an app password; `IEmailSender` remains compatible with a future OAuth2/XOAUTH2 or alternate-provider implementation.
