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
| UpdatedAt      | timestamp    |                              |
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
| UpdatedAt | timestamp    |                                        |
| DeletedAt | timestamp?   | soft delete                            |

### Result JSON Shape

```json
[
  {
    "companyName": "Atlassian",
    "jobRole": "Senior Software Engineer",
    "appliedDate": "2026-04-15",
    "status": "applied"
  }
]
```

## API Endpoints

### Auth/User

| Method | Endpoint                  | Description                                                 |
| ------ | ------------------------- | ----------------------------------------------------------- |
| GET    | `/api/auth/gmail/url`     | Return Gmail OAuth consent URL for client to redirect       |
| POST   | `/api/auth/gmail/connect` | Receive OAuth code, exchange for tokens, create/update user |

### Sync

| Method | Endpoint            | Description                                              |
| ------ | ------------------- | -------------------------------------------------------- |
| POST   | `/api/sync`         | Start sync job (body: userId), return job ID immediately |
| GET    | `/api/sync/{jobId}` | Poll job status + results when completed                 |

## Architecture

### Components

- **API Layer** — REST controllers, request validation
- **Gmail Service** — handles OAuth token refresh, fetches emails via Gmail API
- **Gemini Service** — batches emails, sends to Gemini, parses structured response
- **Sync Orchestrator** — coordinates the pipeline (fetch → batch → process → merge → store result)
- **Background Worker** — `BackgroundService` / `IHostedService`, picks up pending SyncJobs, runs orchestrator

### Sync Pipeline Flow

1. Client calls `POST /api/sync` with userId
2. Controller creates SyncJob (status=pending), returns jobId
3. Background worker picks up pending job, sets status=processing
4. Gmail Service refreshes token if needed, fetches last 30 days of emails
5. Sync Orchestrator batches emails (20 per batch)
6. Each batch → Gemini Service: identify job applications, deduplicate within batch, return structured JSON
7. After all batches complete, final merge pass → Gemini: deduplicate across all batch results
8. Store final result JSON in SyncJob, set status=completed
9. Client polls `GET /api/sync/{jobId}`, receives results when ready

### OAuth Connect Flow

1. Client calls `GET /api/auth/gmail/url`
2. Backend returns Google OAuth consent URL
3. Client redirects user to Google
4. User grants consent, Google redirects back with auth code
5. Client calls `POST /api/auth/gmail/connect` with code
6. Backend exchanges code for access + refresh tokens
7. Backend creates/updates User record with encrypted tokens
8. Returns userId to client

### Error Flow

- Any failure during processing → store error message in SyncJob, set status=failed
- Client polling receives status=failed with error message

## Project Structure

```
JobSync/
├── JobSync.Api/              # REST controllers, startup, DI config
├── JobSync.Core/             # Domain models, interfaces, DTOs
├── JobSync.Infrastructure/   # Gmail service, Gemini service, EF Core DbContext
└── JobSync.Worker/           # Background hosted service (or same project as Api)
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

1. Client connects to `/hubs/sync`
2. Client calls `POST /api/sync` → gets jobId
3. Client invokes `JoinJob(jobId)`
4. Backend pushes `SyncProgress` events as processing happens
5. Backend pushes `SyncCompleted` when done
6. Client calls `GET /api/sync/{jobId}` to fetch results
7. Client invokes `LeaveJob(jobId)` or disconnects

### Fallback

Polling endpoint `GET /api/sync/{jobId}` still works — response includes `stage` and `percent` fields for clients that can't use SignalR.

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
