# ENG-11 Sync Modal Date Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add start and end date selection to the Angular sync modal and send the selected API-compatible date range when starting a sync.

**Architecture:** Keep date selection local to `SyncModal` with a reactive form, pass a validated date range into `ApplicationsService.runSync`, and preserve the existing SignalR sync flow. The service remains responsible for the HTTP request and posts the backend contract shape directly.

**Tech Stack:** Angular 21, TypeScript 5.9, Reactive Forms, Vitest, Angular TestBed.

---

## Summary

Add start and end date controls to the sync modal, prefilled to today-to-today. The modal validates `yyyy-MM-dd`, prevents end dates before start dates, and calls `POST /api/v1/sync` with `dateRange.startDate`, `dateRange.endDate`, and the browser timezone.

## Implementation Changes

- Update `web/job-sync/src/app/pages/applications/sync-modal/sync-modal.ts` to import `ReactiveFormsModule`, manage a `FormGroup` with `startDate` and `endDate`, initialize both controls to the user's current local date when the modal opens, and submit only when the form is valid.
- Add validators for required date values, exact `yyyy-MM-dd` format, real calendar dates, and `endDate >= startDate`.
- Update `web/job-sync/src/app/pages/applications/sync-modal/sync-modal.html` to render native `type="date"` inputs between account selection and modal actions, with accessible inline errors and disabled submit behavior while invalid or when no account exists.
- Update `web/job-sync/src/app/pages/applications/sync-modal/sync-modal.css` to keep the modal consistent with `DESIGN.md`: sharp-corner controls, mono uppercase labels, bordered fields, clay focus/error accents, two-column date layout on desktop, and stacked controls on mobile.
- Update `web/job-sync/src/app/services/applications.ts` to add a `SyncDateRange` interface and change `runSync(accountId, dateRange)` so the sync request body includes:

```ts
{
  emailConnectionId: account.id,
  dateRange: {
    startDate: '2026-06-16',
    endDate: '2026-06-16',
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone
  }
}
```

## Test Plan

- Add `web/job-sync/src/app/pages/applications/sync-modal/sync-modal.spec.ts` covering:
  - modal opens with both date fields prefilled to the user’s local today,
  - valid start/end dates call `runSync` with `startDate`, `endDate`, and browser timezone,
  - invalid `yyyy-MM-dd` values show an error and do not call `runSync`,
  - end date before start date shows an error and blocks submit,
  - no connected accounts keeps Start sync disabled.
- Update `web/job-sync/src/app/services/applications.spec.ts` to verify `runSync` posts the date range payload.
- Run:

```bash
cd web/job-sync
npm test -- --watch=false
npm run build
```

## Assumptions

- The date controls initialize to today-to-today every time the modal opens.
- The backend already accepts `dateRange.startDate`, `dateRange.endDate`, and optional `dateRange.timeZone`.
- The frontend should send the browser timezone so the backend resolves the user's intended local calendar day.
- Native date inputs should still be backed by explicit Angular validators because the API requires exact `yyyy-MM-dd` values.
