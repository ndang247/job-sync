# ENG-9 Backend Sync Date Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add backend support for an optional sync date or date range, defaulting to today when no range is supplied.

**Architecture:** Normalize the requested calendar range in `SyncController`, persist the resolved UTC window on `SyncJob`, and pass that exact window through the worker, orchestrator, and Gmail service. Gmail `users.messages.list` accepts Gmail search syntax through `q`; use Unix-second `after:` and `before:` bounds for timezone-correct whole-day filtering.

**Tech Stack:** .NET 10, ASP.NET Core Web API, EF Core 10, PostgreSQL, Google.Apis.Gmail.v1, xUnit, NSubstitute.

---

## Gmail API Findings

- Gmail API `users.messages.list` has a `q` parameter that uses Gmail search-box syntax.
- Gmail search supports `after:` and `before:` operators.
- Google notes PST-based interpretation for date strings and recommends Unix seconds for accurate timezone boundaries.
- Backend implementation must use `after:<startUnixSeconds> before:<endExclusiveUnixSeconds>`.
- User-facing end date is inclusive; Gmail `before:` is exclusive.

Sources:
- https://developers.google.com/workspace/gmail/api/guides/filtering
- https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/list
- https://support.google.com/mail/answer/7190

## Contract

Request body for `POST /api/v1/sync`:

```json
{
  "emailConnectionId": "00000000-0000-0000-0000-000000000000",
  "dateRange": {
    "startDate": "2026-06-16",
    "endDate": "2026-06-18",
    "timeZone": "Australia/Sydney"
  }
}
```

- `dateRange` is optional for backward compatibility.
- `startDate` and `endDate` use ISO `yyyy-MM-dd`.
- `endDate` is optional and defaults to `startDate`.
- `timeZone` is optional and defaults to server local timezone.
- Missing `dateRange` defaults to today's whole local day.
- Resolved persisted range is `[local startDate 00:00, local endDate + 1 day 00:00)` converted to UTC.
- Invalid input returns `400` with `code = "INVALID_SYNC_DATE_RANGE"`.

## Files

- Modify `api-web/api-contracts/Requests/StartSyncRequest.cs`: add nested optional `SyncDateRangeRequest`.
- Modify `api-web/core/Entities/SyncJob.cs`: add `SyncStartUtc`, `SyncEndUtcExclusive`, and `SyncTimeZone`.
- Modify `api-web/infrastructure/Data/Configurations/SyncJobConfiguration.cs`: map required UTC columns and timezone length.
- Create EF migration in `api-web/infrastructure/Data/Migrations/`: add sync window columns.
- Modify `api-web/web-api/Controllers/SyncController.cs`: validate and normalize requested range.
- Modify `api-web/core/Interfaces/IEmailService.cs`: accept UTC window.
- Modify `api-web/core/Interfaces/ISyncOrchestrator.cs`: accept UTC window.
- Modify `api-web/infrastructure/Services/SyncOrchestrator.cs`: pass UTC window to email service.
- Modify `api-web/infrastructure/Services/GmailService.cs`: build timestamp-based Gmail query.
- Modify `api-web/worker/SyncBackgroundService.cs`: pass persisted window from job to orchestrator.
- Modify `api-web/tests/web-api.IntegrationTests/Controllers/SyncControllerTests.cs`: cover contract and persistence behavior.
- Create `api-web/tests/web-api.IntegrationTests/Services/GmailSearchQueryTests.cs`: cover query formatting.
- Update `docs/specs/core-sync-architecture.md`: replace previous-24-hours wording with selected window.

## Tasks

### Task 1: Request Contract And Normalization

- [x] Add tests for missing `dateRange`, single-day range, multi-day range, reversed range, invalid date, and invalid timezone.
- [x] Add `SyncDateRangeRequest` to `StartSyncRequest`.
- [x] Add controller helper to normalize date range into UTC instants.
- [x] Persist normalized UTC instants on new `SyncJob`.
- [x] Verify focused sync controller tests fail before implementation and pass after implementation.

### Task 2: Persist Sync Window

- [x] Add `SyncStartUtc`, `SyncEndUtcExclusive`, and `SyncTimeZone` to `SyncJob`.
- [x] Map columns in `SyncJobConfiguration`.
- [x] Generate migration `AddSyncDateRangeToSyncJobs`.
- [x] Ensure migration backfills existing rows with a valid current-day UTC window.
- [x] Verify sync controller tests.

### Task 3: Gmail Timestamp Query

- [x] Add `GmailSearchQueryTests` for timestamp query output.
- [x] Update `IEmailService` and `ISyncOrchestrator` signatures to accept UTC window.
- [x] Pass window through `SyncBackgroundService` and `SyncOrchestrator`.
- [x] Replace `UtcNow.AddDays(-1)` query with `after:<startUnix> before:<endUnix>`.
- [x] Verify focused Gmail query and sync controller tests.

### Task 4: Docs And Linear

- [x] Update `docs/specs/core-sync-architecture.md`.
- [x] Add concise Linear ENG-9 comment describing backend intent and FE deferral.
- [x] Run full backend test suite.

## Test Plan

Run focused tests:

```bash
dotnet test api-web/tests/web-api.IntegrationTests/web-api.IntegrationTests.csproj --filter "FullyQualifiedName~SyncControllerTests"
dotnet test api-web/tests/web-api.IntegrationTests/web-api.IntegrationTests.csproj --filter "FullyQualifiedName~GmailSearchQueryTests"
```

Run full backend tests:

```bash
dotnet test api-web/api-web.slnx
```

## Assumptions

- Frontend calendar UI comes later.
- Existing clients remain valid because `dateRange` is optional.
- Server local timezone default is acceptable only for omitted `dateRange`; frontend later should send browser timezone.
- API persists UTC instants, not raw dates, so recovered jobs rerun against the same original window.
