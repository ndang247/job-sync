# Job Sync Implementation Plan

> Last updated: 2026-05-28 (aligned to current `api-web` code)

## Goal

Keep the implementation plan synchronized with the shipped .NET 10 API behavior so this file is a reliable execution and maintenance reference.

## Current Architecture (Implemented)

- Connection-scoped async pipeline: client starts sync with `emailConnectionId` only.
- API writes job IDs into an in-memory `Channel<Guid>`.
- Background worker (`BackgroundService`) consumes channel messages and processes jobs concurrently.
- Gmail access token is minted at runtime from stored refresh token.
- OpenAI classifies and deduplicates job applications.
- Sync results are persisted in two places:
  - `SyncJob.Result` (`jsonb`) as job snapshot output.
  - `JobApplications` table as normalized persisted rows.
- SignalR pushes real-time progress; HTTP status polling remains fallback.

## Tech Stack (Implemented)

- .NET 10 / ASP.NET Core Web API
- EF Core 10 + Npgsql (PostgreSQL)
- Google.Apis.Gmail.v1
- OpenAI .NET SDK (`OpenAI` package)
- BackgroundService + Channel<T>
- SignalR
- xUnit + WebApplicationFactory + NSubstitute (integration tests)

## Current File Structure Snapshot

```text
api-web/
├── api-contracts/
│   └── Requests/
│       └── StartSyncRequest.cs
├── core/
│   ├── Entities/
│   │   ├── BaseEntity.cs
│   │   ├── User.cs
│   │   ├── EmailConnection.cs
│   │   ├── SyncJob.cs
│   │   └── JobApplication.cs
│   ├── Enums/
│   │   ├── EmailConnectionProvider.cs
│   │   ├── EmailConnectionStatus.cs
│   │   ├── SyncJobStatus.cs
│   │   └── JobApplicationStatus.cs
│   ├── Interfaces/
│   │   ├── IEmailService.cs
│   │   ├── IAIService.cs
│   │   ├── IGoogleTokenExchanger.cs
│   │   ├── IJobApplicationService.cs
│   │   ├── ISyncOrchestrator.cs
│   │   ├── ISyncProgressReporter.cs
│   │   ├── ISyncHubNotifier.cs
│   │   └── ISyncJobChannel.cs
│   └── Models/
│       └── JobApplication.cs
├── infrastructure/
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Configurations/
│   │   │   ├── UserConfiguration.cs
│   │   │   ├── EmailConnectionConfiguration.cs
│   │   │   ├── SyncJobConfiguration.cs
│   │   │   └── JobApplicationConfiguration.cs
│   │   └── Migrations/
│   │       ├── 20260510042549_InitialCreate.cs
│   │       ├── 20260515130449_AddEmailConnections.cs
│   │       ├── 20260518135657_AddEmailConnectionProvider.cs
│   │       ├── 20260518135912_EmailConnectionProviderAsString.cs
│   │       └── 20260521053032_AddJobApplication.cs
│   └── Services/
│       ├── GmailService.cs
│       ├── OpenAIService.cs
│       ├── GoogleTokenExchanger.cs
│       ├── JobApplicationService.cs
│       ├── SyncOrchestrator.cs
│       ├── SyncProgressReporter.cs
│       └── SyncJobChannel.cs
├── web-api/
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Controllers/
│   │   ├── MailConnectController.cs
│   │   ├── SyncController.cs
│   │   ├── ConnectionsController.cs
│   │   └── ApplicationsController.cs
│   ├── Hubs/
│   │   └── SyncHub.cs
│   └── Services/
│       └── SyncHubNotifier.cs
├── worker/
│   └── SyncBackgroundService.cs
└── tests/
        └── web-api.IntegrationTests/
                ├── Controllers/
                │   ├── MailConnectControllerTests.cs
                │   └── SyncControllerTests.cs
                └── CustomWebApplicationFactory.cs
```

## Implementation Status

### Platform and Wiring

- [x] Solution and project graph are in place.
- [x] Program startup wires controllers, SignalR, session, CORS, EF Core, channel, and hosted worker.
- [x] Swagger is enabled in Development.

### Domain and Persistence

- [x] `User`, `EmailConnection`, `SyncJob`, `JobApplication` entities are implemented.
- [x] Email connection provider/status enums are implemented.
- [x] Sync jobs are connection-scoped (`EmailConnectionId`).
- [x] `JobApplications` are persisted and deduplicated by `MessageId` per connection in service logic.

### OAuth and Connections

- [x] Gmail OAuth start + callback endpoints implemented.
- [x] OAuth `state` is generated and validated using session.
- [x] Callback uses `IGoogleTokenExchanger` and upserts by `SubjectId`.
- [x] Existing connection reconnect path sets status back to `Active` on successful callback.

### Sync Runtime

- [x] Start sync endpoint accepts `emailConnectionId` and enqueues channel work.
- [x] Active-job conflict guard enforced per connection (`Pending` or `Processing`).
- [x] Worker recovers orphaned jobs on startup and processes each job in its own DI scope.
- [x] Progress stages and SignalR events are emitted.
- [x] Invalid grant in Gmail refresh flow marks connection `NeedsReconnect` and fails the job.

### Read APIs

- [x] `GET /api/v1/sync/status/{jobId}` implemented.
- [x] `GET /api/v1/connections` implemented.
- [x] `GET /api/v1/applications` implemented.

### Test Coverage (Current)

- [x] Integration tests cover mail connect start/callback, subject-id upsert behavior, and sync start/status conflicts.
- [x] Integration tests cover connection-scoped active-job conflicts and concurrent sync across different connections.
- [ ] Focused unit tests are still missing for:
  - `GmailService` token/grant error edges.
  - `OpenAIService` malformed response handling.
  - `SyncOrchestrator` progress/batch behavior.

## Open Work Backlog

### Task 20: Verification Pass

- [ ] Run full test suite and record results in this document.
- [ ] Start the API locally and verify Swagger responds.
- [ ] Resolve any regressions and update this plan after verification.

### Task 21: Unit Test Additions

- [ ] Add `infrastructure`-level unit tests for Gmail invalid grant and reconnect transition.
- [ ] Add OpenAI response parse failure tests.
- [ ] Add orchestrator batch/progress unit tests.

### Task 22: Documentation Guardrail

- [ ] On each behavior change, update both `docs/superpowers/plans/2026-05-06-job-sync-implementation.md` and `docs/superpowers/specs/2026-05-06-job-sync-design.md` in the same PR.
