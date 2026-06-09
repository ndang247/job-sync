# Job Sync SignalR Implementation Plan

## Goal

Implementation of async job sync process via SignalR according to the design spec.

## Current Architecture

- Connection-scoped async pipeline: client starts sync with `emailConnectionId` only.
- API writes job IDs into an in-memory `Channel<Guid>`.
- Background worker (`BackgroundService`) consumes channel messages and processes jobs concurrently.
- Gmail access token is minted at runtime from stored refresh token.
- OpenAI classifies and deduplicates job applications.
- Sync results are persisted in two places:
  - `SyncJob.Result` (`jsonb`) as job snapshot output.
  - `JobApplications` table as normalized persisted rows.
- SignalR pushes real-time progress; HTTP status polling remains fallback.

## Tech Stack

- .NET 10 / ASP.NET Core Web API
- EF Core 10 + Npgsql (PostgreSQL)
- Google.Apis.Gmail.v1
- OpenAI .NET SDK (`OpenAI` package)
- BackgroundService + Channel<T>
- SignalR
- xUnit + WebApplicationFactory + NSubstitute (integration tests)

## Implementation

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
