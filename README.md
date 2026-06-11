# Job Sync

## What

Job Sync is an automated job application tracker that turns an email inbox into a structured application history.

Instead of entering every application into a spreadsheet or tracker by hand, Job Sync connects to email with read-only access, finds initial job application confirmation emails, extracts details such as the company, role, and application date, and stores them in one place.

The aim is simple: keep a useful record of job applications without creating another manual task.

## Why

Applying for multiple roles each day already takes significant time and energy. Re-entering the same information into another table adds repetitive work at the point when that energy is better spent elsewhere.

I'm building Job Sync to remove that duplication. Once an application is submitted, its confirmation email should be enough to update the tracker. Automating that step gives me more time to focus on learning, building, interview preparation, and the next application.

## How

Job Sync uses an asynchronous sync pipeline to read recent email messages, identify application confirmations with AI, deduplicate the results, persist them in PostgreSQL, and report progress to the web client in real time.

The complete technology stack, component architecture, sync sequence, persistence model, and failure behavior are documented in the [core sync architecture](docs/specs/core-sync-architecture.md).
