# Job Sync SignalR — Design Spec

## Overview

Job Sync tracks job applications by syncing Gmail inboxes via connection-scoped jobs. A user connects a Gmail account through OAuth. Sync runs for a selected `emailConnectionId`, fetches recent emails, classifies and deduplicates job-application confirmations with OpenAI, stores normalized results, and exposes status/progress through HTTP + SignalR.

## Decisions (Current)

- No platform authentication is implemented currently.
- PostgreSQL persistence via EF Core with soft-delete query filters.
- OAuth callback upserts by Google `SubjectId`.
- Access token is not persisted; Gmail token is refreshed at runtime using stored refresh token.
- Start Sync API is connection-scoped and accepts `emailConnectionId` only.
- One active sync job per connection (`Pending`/`Processing` guard).
- Results are persisted in both:
  - `SyncJob.Result` (`jsonb` snapshot).
  - `JobApplications` table (normalized records).
- SignalR progress and completion/failure events are provided; polling is supported.

## Data Model

### Users

| Column    | Type         | Notes       |
| --------- | ------------ | ----------- |
| Id        | UUID         | PK          |
| FirstName | varchar(100) |             |
| LastName  | varchar(100) |             |
| CreatedAt | timestamp    |             |
| UpdatedAt | timestamp?   |             |
| DeletedAt | timestamp?   | soft delete |

### EmailConnections

| Column        | Type        | Notes                                                  |
| ------------- | ----------- | ------------------------------------------------------ |
| Id            | UUID        | PK                                                     |
| UserId        | UUID        | FK → Users                                             |
| Email         | text        | connected mailbox email                                |
| SubjectId     | text        | Google `sub` claim                                     |
| RefreshToken  | text        | token source for runtime refresh                       |
| GrantedScopes | text        | scope string from OAuth exchange                       |
| Provider      | varchar(30) | enum-as-string (`Gmail`)                               |
| Status        | varchar(30) | enum-as-string (`Active`, `NeedsReconnect`, `Revoked`) |
| CreatedAt     | timestamp   |                                                        |
| UpdatedAt     | timestamp?  |                                                        |
| DeletedAt     | timestamp?  | soft delete                                            |

### SyncJobs

| Column            | Type          | Notes                                          |
| ----------------- | ------------- | ---------------------------------------------- |
| Id                | UUID          | PK                                             |
| UserId            | UUID          | FK → Users                                     |
| EmailConnectionId | UUID          | FK → EmailConnections                          |
| Status            | varchar(20)   | `Pending`, `Processing`, `Completed`, `Failed` |
| Progress          | int           | 0-100                                          |
| Stage             | varchar(100)  | progress label                                 |
| Result            | jsonb         | serialized final applications                  |
| Error             | varchar(2000) | failure message                                |
| CreatedAt         | timestamp     |                                                |
| UpdatedAt         | timestamp?    |                                                |
| DeletedAt         | timestamp?    | soft delete                                    |

### JobApplications

| Column            | Type        | Notes                                   |
| ----------------- | ----------- | --------------------------------------- |
| Id                | UUID        | PK                                      |
| MessageId         | text        | message identifier from source email    |
| CompanyName       | text        | required                                |
| JobRole           | text        | required                                |
| AppliedDate       | text        | `dd-MM-yyyy` format by current pipeline |
| Status            | varchar(30) | enum-as-string (`Applied`)              |
| EmailConnectionId | UUID        | FK → EmailConnections                   |
| CreatedAt         | timestamp   |                                         |
| UpdatedAt         | timestamp?  |                                         |
| DeletedAt         | timestamp?  | soft delete                             |

## API Endpoints (Current)

### Mail Connect

| Method | Endpoint                              | Description                                                                             |
| ------ | ------------------------------------- | --------------------------------------------------------------------------------------- |
| GET    | `/api/v1/mail-connect/gmail/start`    | Generates OAuth state in session and redirects to Google consent URL                    |
| GET    | `/api/v1/mail-connect/gmail/callback` | Validates state, exchanges code, creates/updates user+connection, redirects to frontend |

### Sync

| Method | Endpoint                      | Description                                                                             |
| ------ | ----------------------------- | --------------------------------------------------------------------------------------- |
| POST   | `/api/v1/sync`                | Starts sync with body `{ emailConnectionId }`, creates `SyncJob`, enqueues channel work |
| GET    | `/api/v1/sync/status/{jobId}` | Returns `{ jobId, status, progress, stage, result, error }`                             |

### Read Models

| Method | Endpoint               | Description                      |
| ------ | ---------------------- | -------------------------------- |
| GET    | `/api/v1/connections`  | Lists current email connections  |
| GET    | `/api/v1/applications` | Lists persisted job applications |

## Runtime Architecture

### Components

- API controllers: sync start/status, mail connect, list connections, list applications.
- `SyncJobChannel`: singleton in-memory `Channel<Guid>` used as dispatch queue.
- `SyncBackgroundService`: recovers orphaned active jobs at startup, processes queued jobs concurrently.
- `SyncOrchestrator`: fetches emails, classifies batches, deduplicates results, persists job applications.
- `SyncProgressReporter`: persists stage/progress and broadcasts SignalR updates.
- `SyncHubNotifier` + `SyncHub`: sends/receives job-group events.

### Concurrency Constraints

- One active job per `EmailConnectionId`.
- Jobs for different connections can run concurrently.
- Worker runs in-process with API.

## Sync Pipeline (Implemented)

1. Client calls `POST /api/v1/sync` with `emailConnectionId`.
2. API validates connection exists and is `Active`.
3. API rejects if a `Pending`/`Processing` job exists for the same connection.
4. API creates `SyncJob` (`Pending`) and writes `jobId` to channel.
5. Worker reads `jobId`, transitions job to `Processing`.
6. Orchestrator reports progress `Fetching emails` (5%).
7. Gmail service loads connection, refreshes token via refresh token, and fetches recent messages.
8. Orchestrator processes emails in batches of 20 and reports stage progress.
9. AI service classifies each batch and then runs final deduplication.
10. `JobApplicationService` persists new applications for the connection (dedupe by `MessageId`).
11. Worker stores final serialized result in `SyncJob.Result`, sets status `Completed`.
12. Reporter/notifier emits `SyncCompleted`.

### Email Fetch Window

Current implementation queries Gmail with `after:<unixTimestamp now-1 day>`. This is a 24-hour window, not 30 days.

## SignalR Contract

- Hub endpoint: `/hubs/sync`
- Client methods:
  - `JoinJob(jobId)`
  - `LeaveJob(jobId)`
- Server events:
  - `SyncProgress(stage, percent)`
  - `SyncCompleted`
  - `SyncFailed(error)`

## OAuth Flow (Implemented)

1. Client opens `GET /api/v1/mail-connect/gmail/start`.
2. API generates random OAuth `state`, stores it in session, redirects to Google.
3. Google returns to callback with `code` and `state`.
4. API validates state; invalid/missing state returns `400 BadRequest`.
5. API exchanges code via `IGoogleTokenExchanger`.
6. API upserts by `SubjectId`:
   - existing connection: update refresh token, scopes, email, and set status `Active`.
   - new connection: create `User` and `EmailConnection`.
7. API redirects to frontend with `userId` and `connectionId` query params.

## Error Handling (Current)

| Scenario                                                       | Behavior                                                                                           |
| -------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| Missing connection on start sync                               | `409` `{ code: "CONNECTION_REQUIRES_GRANT", error: "Email connection requires grant/reconnect." }` |
| Connection status is not `Active`                              | Same `409` reconnect-required payload                                                              |
| Active job already running for connection                      | `409` `{ error: "A sync job is already in progress for this email connection" }`                   |
| Gmail refresh returns `invalid_grant` or `unauthorized_client` | Connection set to `NeedsReconnect`; job fails with error                                           |
| Unhandled exception in worker                                  | Job marked `Failed`; error persisted and pushed through `SyncFailed`                               |

## Notes on Current Gaps

- No explicit retry policy is implemented for Gmail/OpenAI calls in current code.
- Unit tests for Gmail/OpenAI/orchestrator internals are not yet present; integration tests are present for controllers.
