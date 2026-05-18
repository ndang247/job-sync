# Job Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a .NET 10 REST API that syncs job applications from multiple email connections per user via Gemini AI classification.

**Architecture:** Connection-scoped async pipeline — client starts sync with `userId` + `emailConnectionId`, API enqueues a sync job, channel-based background worker processes jobs concurrently, mints short-lived Gmail access tokens from stored refresh tokens at runtime, classifies/deduplicates emails with Gemini, and persists results in PostgreSQL. SignalR provides real-time progress; status polling remains a fallback. Reconnect-required conditions (missing/revoked/invalid grant) are surfaced via API conflict responses and failed job state.

**Tech Stack:** .NET 10, ASP.NET Core Web API, EF Core + Npgsql (PostgreSQL), Google.Apis.Gmail.v1, Google Generative AI SDK (Gemini 2.0 Flash), BackgroundService + Channel<T>, SignalR, xUnit + WebApplicationFactory + NSubstitute (integration tests)

---

## File Structure

```
api-web/
├── api-web.slnx
├── .githooks/
│   ├── pre-commit
│   ├── pre-push
│   └── run-checks.sh
├── api-contracts/
│   ├── Requests/
│   │   └── StartSyncRequest.cs
│   └── api-contracts.csproj
├── web-api/
│   ├── Program.cs
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Controllers/
│   │   ├── MailConnectController.cs
│   │   └── SyncController.cs
│   ├── Hubs/
│   │   └── SyncHub.cs
│   ├── Services/
│   │   └── SyncHubNotifier.cs
│   └── web-api.csproj
├── core/
│   ├── Entities/
│   │   ├── BaseEntity.cs
│   │   ├── User.cs
│   │   ├── EmailConnection.cs
│   │   └── SyncJob.cs
│   ├── Enums/
│   │   ├── EmailConnectionStatus.cs
│   │   └── SyncJobStatus.cs
│   ├── Models/
│   │   └── JobApplication.cs
│   ├── Interfaces/
│   │   ├── IGmailService.cs
│   │   ├── IGeminiService.cs
│   │   ├── IGoogleTokenExchanger.cs
│   │   ├── ISyncOrchestrator.cs
│   │   ├── ISyncProgressReporter.cs
│   │   ├── ISyncHubNotifier.cs
│   │   └── ISyncJobChannel.cs
│   └── core.csproj
├── infrastructure/
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Configurations/
│   │   │   ├── UserConfiguration.cs
│   │   │   ├── EmailConnectionConfiguration.cs
│   │   │   └── SyncJobConfiguration.cs
│   │   └── Migrations/
│   │       ├── 20260510042549_InitialCreate.cs
│   │       ├── 20260510042549_InitialCreate.Designer.cs
│   │       ├── 20260515130449_AddEmailConnections.cs
│   │       ├── 20260515130449_AddEmailConnections.Designer.cs
│   │       └── AppDbContextModelSnapshot.cs
│   ├── Services/
│   │   ├── GmailService.cs
│   │   ├── GeminiService.cs
│   │   ├── GoogleTokenExchanger.cs
│   │   ├── SyncOrchestrator.cs
│   │   ├── SyncProgressReporter.cs
│   │   └── SyncJobChannel.cs
│   └── infrastructure.csproj
├── scripts/
│   └── setup-git-hooks.sh
├── tests/
│   └── web-api.IntegrationTests/
│       ├── Controllers/
│       │   ├── MailConnectControllerTests.cs
│       │   └── SyncControllerTests.cs
│       ├── CustomWebApplicationFactory.cs
│       └── web-api.IntegrationTests.csproj
└── worker/
    ├── SyncBackgroundService.cs
    └── worker.csproj
```

---

## Task 1: Solution Scaffolding ✅ COMPLETED

- [x] Scaffold API, domain, infrastructure, and worker projects.
- [x] Add all projects to `api-web.slnx`.
- [x] Confirm project references follow API -> worker -> infrastructure -> core.

---

## Task 2: Core Domain Models ✅ COMPLETED

- [x] Create base entities and job application model.
- [x] Add sync job status enum.
- [x] Keep namespaces and folder structure consistent with `core` conventions.

---

## Task 3: Core Interfaces ✅ COMPLETED

- [x] Define core service contracts for Gmail, Gemini, orchestration, and progress reporting.
- [x] Keep orchestrator contract aligned with job-scoped execution.

---

## Task 4: Database Context and Entity Configuration ✅ COMPLETED

- [x] Create `AppDbContext` with required DbSets.
- [x] Add entity configurations for user, sync job, and related mappings.
- [x] Ensure EF mappings align with current domain model.

---

## Task 5: PostgreSQL Wiring and Migrations ✅ COMPLETED

- [x] Configure connection string + EF Core Npgsql in API startup.
- [x] Initialize user secrets for local development.
- [x] Add EF design-time tooling.
- [x] Create and apply initial migration.
- [x] Verify DB connectivity and successful build.

---

## Task 6: Gmail Service (Runtime Token Flow) ✅ COMPLETED

- [x] Implement Gmail fetch flow with OAuth refresh handling, paging, and MIME/body parsing.
- [x] Limit fetch scope to recent mail window used by sync pipeline.
- [x] Add/keep Gmail API package dependency.
- [ ] Add focused unit tests for error handling and token/grant edge cases.

---

## Task 7: Gemini Service ✅ COMPLETED

- [x] Implement batch classification and final deduplication flow.
- [x] Standardize prompt/response handling to JSON-only parsing with safe fallback.
- [x] Add/keep Gemini SDK dependency.
- [ ] Add focused unit tests for empty inputs and malformed response handling.

---

## Task 8: Sync Orchestrator with Progress Reporting ✅ COMPLETED

- [x] Implement batch orchestration across Gmail fetch, Gemini classify, and deduplicate.
- [x] Emit stage-based progress updates throughout job execution.
- [ ] Add orchestrator unit tests for batch behavior and progress events.

---

## Task 9: Channel-Based Background Worker ✅ COMPLETED

- [x] Introduce job channel abstraction and implementation.
- [x] Move worker from polling to channel-driven processing.
- [x] Process jobs in isolated DI scopes and recover orphaned active jobs at startup.
- [x] Persist success/failure state and notify progress channel.

---

## Task 10: Mail Connect Controller ✅ COMPLETED

- [x] Expose `gmail/start` (redirect to Google) and `gmail/callback` (receive code, exchange tokens, redirect to frontend) endpoints.
- [x] Backend-driven OAuth flow: user identity (name, email) extracted from Google ID token — no frontend form needed.
- [x] User deduplication by `SubjectId` — same Google account always maps to the same user.
- [x] Decouple token exchange via `IGoogleTokenExchanger` for testability.

---

## Task 11: Sync Controller ✅ COMPLETED

- [x] Create sync job endpoint with user validation.
- [x] Enforce active-job conflict handling.
- [x] Enqueue jobs to channel for immediate background processing.
- [x] Expose status endpoint with `status`, `progress`, `stage`, `result`, and `error`.

---

## Task 12: Application Configuration ✅ COMPLETED

- [x] Add Google and database config sections in app settings.
- [x] Keep secret values externalized via user secrets/environment.

---

## Task 13: SignalR Hub and Progress Notification ✅ COMPLETED

- [x] Add sync hub with job-group join/leave behavior.
- [x] Implement notifier abstraction and SignalR-backed notifier.
- [x] Implement progress reporter that persists progress and broadcasts updates.

---

## Task 14: Program Startup and DI Wiring ✅ COMPLETED

- [x] Register controllers, EF Core, SignalR, CORS, and hosted worker.
- [x] Register Gmail/Gemini/orchestrator/progress/notifier/channel services.
- [x] Register token exchanger and expose partial `Program` for integration tests.

---

## Task 15: Controller Integration Tests ✅ COMPLETED

- [x] Create integration test project with in-memory DB host customization.
- [x] Add mail connect endpoint integration tests.
- [x] Add sync endpoint integration tests (start/status/conflict/not-found scenarios).
- [x] Ensure test host replaces external dependencies (token exchanger/channel/worker behavior).

---

## Task 16: Multi-Connection Schema Foundation ✅ COMPLETED

- [x] Add `EmailConnection` entity and status enum.
- [x] Add email connection configuration and relationships.
- [x] Add migration for email connection schema changes.

---

## Task 17: Backlog Cleanup and Commit Hygiene

- [ ] Close or remove stale checkboxes that no longer represent real work.
- [ ] Keep only actionable remaining items in open-state tasks.
- [ ] Group future work under Task 19+ to avoid duplicate tracking.

---

## Task 18: Final Integration Verification

- [ ] Run full test suite and confirm all tests pass.
- [ ] Start the API locally and verify Swagger endpoint responds successfully.
- [ ] Fix any failing tests or startup issues before final verification.
- [ ] Create verification commit after all checks are green.

---

## Task 19: Multi-Connection + Security Uplift (2026-05-15)

> Update existing implementation to support multiple Gmail connections per user and remove persisted access tokens. Sync must run against a selected `emailConnectionId`, mint access token at runtime from refresh token, and return reconnect-required errors when connection is missing/revoked/expired.

- [ ] Add/adjust integration tests first for connection creation/upsert, ownership validation, reconnect-required conflicts, and per-connection job conflict behavior.
- [ ] Run focused integration tests and confirm expected RED state before implementation.
- [ ] Update domain model to remove persisted access tokens from users and model email connections explicitly.
- [ ] Update schema/mappings/migrations for `EmailConnection` and connection-scoped sync jobs.
- [ ] Update API contracts and controllers for `emailConnectionId`-scoped sync and reconnect-required responses.
- [ ] Update Gmail runtime auth flow to mint access tokens from refresh token only.
- [ ] Ensure invalid grant/revoked token paths mark connection as `NeedsReconnect` and fail job with clear error.
- [ ] Run focused integration tests, then full tests/build, and confirm GREEN.
- [ ] Commit after verification passes.
