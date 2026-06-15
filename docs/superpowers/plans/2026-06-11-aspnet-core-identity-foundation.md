# ASP.NET Core Identity Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ASP.NET Core Identity persistence and services while preserving all non-user data and deferring passwordless OTP/JWT behavior.

**Architecture:** Extend the existing `User` entity from `IdentityUser<Guid>` so current ownership relationships keep one user type and one EF Core context. Use `IdentityUserContext<User, Guid>` without roles, keep user ownership required with cascade deletes, and temporarily block Gmail OAuth until authenticated ownership exists.

**Tech Stack:** .NET 10, ASP.NET Core Identity, EF Core 10, Npgsql, PostgreSQL, xUnit, `WebApplicationFactory`

---

### Task 1: Add Identity model and store tests

**Files:**
- Create: `api-web/tests/web-api.IntegrationTests/Identity/IdentityStoreTests.cs`

- [x] Write tests proving `UserManager<User>` can create and retrieve a passwordless user.
- [x] Assert normalized username/email and automatic `CreatedAt`.
- [x] Assert claims, external-login, and authentication-token stores work.
- [x] Run the focused tests and confirm they fail because Identity services and schema are absent.

### Task 2: Add Identity domain and EF foundation

**Files:**
- Create: `api-web/core/Entities/IAuditableEntity.cs`
- Modify: `api-web/core/Entities/BaseEntity.cs`
- Modify: `api-web/core/Entities/User.cs`
- Modify: `api-web/core/core.csproj`
- Modify: `api-web/infrastructure/Data/AppDbContext.cs`
- Modify: `api-web/infrastructure/Data/Configurations/UserConfiguration.cs`
- Modify: `api-web/infrastructure/infrastructure.csproj`
- Modify: `api-web/web-api/Program.cs`

- [x] Add Identity packages aligned to .NET runtime version `10.0.8`.
- [x] Make `User` inherit `IdentityUser<Guid>` and implement timestamp/soft-delete fields through `IAuditableEntity`.
- [x] Make `BaseEntity` implement the same audit contract.
- [x] Make `AppDbContext` inherit `IdentityUserContext<User, Guid>`.
- [x] Call `base.OnModelCreating(modelBuilder)` before application configurations.
- [x] Keep Identity users mapped to `Users`; retain names, query filter, and email-connection navigation.
- [x] Register `AddIdentityCore<User>()`, EF stores, and default token providers; do not add roles, cookies, JWT, UI, or passkeys.
- [x] Generalize timestamp assignment to tracked `IAuditableEntity` objects.
- [x] Run focused Identity tests and confirm they pass.

### Task 3: Keep user ownership required

**Files:**
- Modify: `api-web/core/Entities/EmailConnection.cs`
- Modify: `api-web/core/Entities/SyncJob.cs`
- Modify: `api-web/infrastructure/Data/Configurations/UserConfiguration.cs`
- Modify: `api-web/infrastructure/Data/Configurations/SyncJobConfiguration.cs`
- Modify: `api-web/web-api/Controllers/SyncController.cs`
- Modify: affected integration-test seed helpers

- [x] Add a failing metadata test proving both user relationships are required and cascade on delete.
- [x] Keep both ownership foreign keys as `Guid` with required navigations.
- [x] Configure `ON DELETE CASCADE`.
- [x] Cascade `EmailConnection` deletion to its `SyncJob` rows.
- [x] Return the existing reconnect conflict for the defensive `Guid.Empty` case.
- [x] Run focused relationship and sync tests.

### Task 4: Block Gmail OAuth until platform auth exists

**Files:**
- Modify: `api-web/web-api/Controllers/MailConnectController.cs`
- Modify: `api-web/tests/web-api.IntegrationTests/Controllers/MailConnectControllerTests.cs`

- [x] Replace callback/provisioning tests with failing tests for `503 Service Unavailable`.
- [x] Return `{ code: "PLATFORM_AUTH_REQUIRED", error: "Platform authentication must be enabled before connecting Gmail." }` from both Gmail endpoints.
- [x] Remove now-unused OAuth provisioning dependencies from the controller.
- [x] Run focused controller tests.

### Task 5: Generate Identity migration

**Files:**
- Create: `api-web/infrastructure/Data/Migrations/*_AddAspNetCoreIdentity.cs`
- Modify: `api-web/infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs`

- [x] Generate migration with `dotnet ef migrations add AddAspNetCoreIdentity`.
- [x] Keep existing ownership columns and foreign keys required; add only Identity columns, tables, and indexes.
- [x] Ensure claims, logins, and tokens exist; roles and role joins do not.
- [x] Keep `Down` reversible because the migration deletes no application data.
- [x] Generate an idempotent Npgsql SQL script and inspect it for unintended table/data deletion.

### Task 6: Full verification

- [x] Run `dotnet test api-web/api-web.slnx -m:1 /nodeReuse:false`.
- [x] Run `dotnet build api-web/api-web.slnx --no-restore -m:1 /nodeReuse:false`.
- [x] Run `git diff --check`.
- [x] Inspect migration and final diff against this plan.

## Deferred Passwordless Branch

Create `feat/passwordless-otp-auth` from this branch later. That plan will define email/SMS sender selection, OTP generation and throttling, JWT access tokens, rotating refresh tokens, endpoint authorization, resource ownership, and Gmail connection claiming using the authenticated user ID.
