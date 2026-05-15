# Job Sync — Design Spec

## Overview

Job Sync tracks job applications by syncing from a user's Gmail inbox. Users connect their Gmail account via OAuth, then trigger a sync that fetches emails from the last 30 days, processes them through Gemini AI to identify and deduplicate job application emails, and returns structured results.

## Decisions

- No authentication — anyone can use the app, connects own Gmail
- PostgreSQL — user table + sync jobs with `jsonb` for results
- Job applications returned via sync job results (not separately persisted)
- Google AI SDK (Gemini) for email classification and deduplication
- Batch emails to Gemini, deduplicate within batch + final merge pass across batches
- REST API with controllers, .NET 10
- Frontend-agnostic API (React Native/Expo likely, but API works with any client)
- Async processing with SignalR real-time progress (polling as fallback)

## Data Model

### Users

| Column         | Type         | Notes                        |
| -------------- | ------------ | ---------------------------- |
| Id             | UUID         | PK                           |
| FirstName      | varchar(100) |                              |
| LastName       | varchar(100) |                              |
| AccessToken    | text         | encrypted, provider-agnostic |
| RefreshToken   | text         | encrypted, provider-agnostic |
| TokenExpiresAt | timestamp    |                              |
| CreatedAt      | timestamp    |                              |
| UpdatedAt      | timestamp?   |                              |
| DeletedAt      | timestamp?   | soft delete                  |

### SyncJobs

| Column    | Type         | Notes                                  |
| --------- | ------------ | -------------------------------------- |
| Id        | UUID         | PK                                     |
| UserId    | UUID         | FK → Users                             |
| Status    | varchar(20)  | pending, processing, completed, failed |
| Progress  | int          | 0-100 percentage                       |
| Stage     | varchar(100) | current stage label                    |
| Result    | jsonb        | array of job applications              |
| Error     | text         | error message if failed                |
| CreatedAt | timestamp    |                                        |
| UpdatedAt | timestamp?   |                                        |
| DeletedAt | timestamp?   | soft delete                            |

### Result JSON Shape

```json
[
  {
    "companyName": "Atlassian",
    "jobRole": "Senior Software Engineer",
    "appliedDate": "15-04-2026",
    "status": "applied"
  }
]
```

## API Endpoints

### Mail Connect

| Method | Endpoint                          | Description                                                 |
| ------ | --------------------------------- | ----------------------------------------------------------- |
| GET    | `/api/mail-connect/gmail/url`     | Return Gmail OAuth consent URL for client to redirect       |
| POST   | `/api/mail-connect/gmail/connect` | Receive OAuth code, exchange for tokens, create/update user |

### Sync

| Method | Endpoint                   | Description                                              |
| ------ | -------------------------- | -------------------------------------------------------- |
| POST   | `/api/sync`                | Start sync job (body: userId), return job ID immediately |
| GET    | `/api/sync/status/{jobId}` | Poll job status + results when completed                 |

## Architecture

### Components

- **API Layer** — REST controllers, request validation
- **Gmail Service** — handles OAuth token refresh, fetches emails via Gmail API
- **Gemini Service** — batches emails, sends to Gemini, parses structured response
- **Sync Orchestrator** — coordinates the pipeline (fetch → batch → process → merge → store result)
- **Progress Reporter** — persists progress to DB + delegates to ISyncHubNotifier for SignalR push
- **Hub Notifier** — sends SignalR events (SyncProgress, SyncCompleted, SyncFailed) via IHubContext
- **Sync Job Channel** — in-memory `Channel<Guid>` (singleton) connecting the API (producer) to the worker (consumer) for immediate job dispatch
- **Background Worker** — `BackgroundService` / `IHostedService`, reads from SyncJobChannel, processes each job concurrently in its own Task with its own DI scope

### Concurrency Constraints

- One active sync job per user — API returns 409 Conflict if user already has a Pending/Processing job
- No global concurrency limit — all users are served concurrently
- On startup, worker recovers orphaned Pending/Processing jobs from DB and re-queues them via the channel
- Worker runs in-process with the web API (Channel<T> is in-memory)

### Sync Pipeline Flow

1. Client calls `POST /api/sync` with userId
2. Controller checks for existing Pending/Processing job for this user → 409 Conflict if exists
3. Controller creates SyncJob (status=pending), writes jobId to SyncJobChannel, returns jobId
4. Background worker reads jobId from channel, spawns a new Task with its own DI scope
5. Worker loads job from DB, sets status=processing
6. Gmail Service refreshes token if needed, fetches last 30 days of emails
7. Sync Orchestrator batches emails (20 per batch)
8. Each batch → Gemini Service: identify job applications, deduplicate within batch, return structured JSON
9. After all batches complete, final merge pass → Gemini: deduplicate across all batch results
10. Store final result JSON in SyncJob, set status=completed
11. Client receives results via SignalR events or polls `GET /api/sync/status/{jobId}`

### Recommended Client Flow

1. Client calls `POST /api/sync` → gets `jobId`, processing begins immediately
2. Client connects to SignalR, calls `JoinJob(jobId)` — may miss first 1-2 progress events
3. Client calls `GET /api/sync/status/{jobId}` once to catch up on current `stage`/`progress`
4. Client receives remaining `SyncProgress` events in real-time
5. On `SyncCompleted`, client calls `GET /api/sync/status/{jobId}` to fetch final results

### OAuth Connect Flow

1. Client calls `GET /api/mail-connect/gmail/url`
2. Backend returns Google OAuth consent URL
3. Client redirects user to Google
4. User grants consent, Google redirects back with auth code
5. Client calls `POST /api/mail-connect/gmail/connect` with code
6. Backend exchanges code for access + refresh tokens
7. Backend creates/updates User record with encrypted tokens
8. Returns userId to client

### Error Flow

- Any failure during processing → store error message in SyncJob, set status=failed
- Client polling receives status=failed with error message

## Project Structure

```
api-web/
├── web-api/                  # REST controllers, Program.cs, DI config, SignalR hub, SyncHubNotifier
├── core/                     # Domain entities, interfaces (incl. ISyncHubNotifier, ISyncJobChannel), models, enums
├── infrastructure/           # Gmail service, Gemini service, EF Core DbContext, SyncOrchestrator, SyncProgressReporter, SyncJobChannel
├── worker/                   # SyncBackgroundService (BackgroundService)
└── api-contracts/            # Request DTOs (GmailConnectRequest, StartSyncRequest)
```

## Tech Stack

- .NET 10, C#
- Entity Framework Core with Npgsql (PostgreSQL)
- Google.Apis.Gmail.v1 (Gmail SDK)
- Google AI SDK for .NET (Gemini)
- `BackgroundService` for async job processing
- `Microsoft.AspNetCore.SignalR` for real-time progress

## Real-time Progress (SignalR)

### Hub

Endpoint: `/hubs/sync`

### Client → Server Methods

| Method   | Params       | Description                                  |
| -------- | ------------ | -------------------------------------------- |
| JoinJob  | jobId (Guid) | Join group `sync-{jobId}` to receive updates |
| LeaveJob | jobId (Guid) | Leave group                                  |

### Server → Client Methods

| Method        | Params                        | Description                               |
| ------------- | ----------------------------- | ----------------------------------------- |
| SyncProgress  | stage (string), percent (int) | Progress update                           |
| SyncCompleted | —                             | Job done, client fetches results via HTTP |
| SyncFailed    | error (string)                | Job failed                                |

### Progress Stages

| Stage                 | Percent              |
| --------------------- | -------------------- |
| Fetching emails       | 5%                   |
| Processing batch N/M  | Evenly split 10%–90% |
| Deduplicating results | 90%                  |
| Done                  | 100%                 |

### Flow

1. Client calls `POST /api/sync` → gets `jobId`, processing begins immediately
2. Client connects to `/hubs/sync`
3. Client invokes `JoinJob(jobId)` — may miss first 1-2 progress events
4. Client calls `GET /api/sync/status/{jobId}` once to catch up on current `stage`/`progress`
5. Backend pushes remaining `SyncProgress` events in real-time
6. Backend pushes `SyncCompleted` when done
7. Client calls `GET /api/sync/status/{jobId}` to fetch final results
8. Client invokes `LeaveJob(jobId)` or disconnects

### Fallback

Polling endpoint `GET /api/sync/status/{jobId}` still works — response includes `stage` and `progress` fields for clients that can’t use SignalR.

## Gemini Prompt Strategy

### Per-Batch Prompt

> Given these emails, identify which are job application related. For duplicates about the same application (e.g. platform confirmation like Seek.com.au + company auto-reply), return only one entry. Return a JSON array with objects containing: companyName, jobRole, appliedDate (use the email date), status (always "applied").

### Final Merge Prompt

> Given these job application results from multiple batches, deduplicate entries for the same company+role combination. Return the final consolidated JSON array.

## Error Handling

| Scenario              | Behavior                                                |
| --------------------- | ------------------------------------------------------- |
| Token refresh failure | Mark job failed, error = "Gmail authentication expired" |
| Gmail API error       | Retry once, then mark failed                            |
| Gemini API error      | Retry batch once, then mark failed                      |
| Unhandled exception   | Catch-all, mark job failed with message                 |
