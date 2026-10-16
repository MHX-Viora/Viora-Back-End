# Stage 3: Mini App management and client credentials

## Objective and boundaries
Implement the supplied Stage 3 specification against the existing DeveloperSession identity, shared AppDbContext and integrated Expo web portal. The supplied request authorizes schema/code additions and local verification. No production migration, deployment, ANKT auth changes, domain verification, token endpoint, sandbox runtime or review submission.

## Existing architecture
- BE: .NET 8, EF Core 8/Npgsql; independent verified DeveloperAccounts and opaque sessions.
- FE: Expo Router/React web under `/developers`; same-origin Node BFF with HttpOnly session cookies and origin/custom-header CSRF checks. Historical Next portal is superseded.
- Storage: existing Cloudinary account; signed server uploads. Historical Mini App tables were removed in `20261015030504_RemoveMiniAppPlatform`; no active Mini App entities.
- Conventions: explicit DTOs, DeveloperAuthException, no-store responses, developer ownership from authenticated claims. Preserve unrelated dirty FE files.

## Decisions
- Add MiniApps, MiniAppOAuthClients, MiniAppClientSecrets, MiniAppRedirectUris, MiniAppAuditLogs and temporary MiniAppLogoUploads; restrictive FKs, unique public identifiers, UTC, soft deletion.
- AES-256-GCM authenticated encryption with external versioned configuration keys; missing keys fail closed. Credential endpoints only decrypt after ownership checks. One active secret per client; rotation revokes old secret atomically.
- Create request ID persisted per owner with input fingerprint; transaction serializes quota checks and creates app/client/secret/audit together. Replays return the original app; mismatched replay returns 409.
- Real logo upload with bounded bytes, MIME/signature checks, Cloudinary decode/format validation and square transform; temporary upload tickets scoped to owner. Background cleanup retries abandoned uploads.
- Server-computed Draft/ConfigurationIncomplete; request DTOs never accept privileged status or identifiers. HTTPS callbacks, no wildcard/userinfo/fragment; HTTP localhost only in Development.
- Routes retain the existing `/developers` prefix. Creation is a modal opened from sidebar/dashboard. Secrets exist only in client memory on the integration tab.

## Ordered tasks and verification
1. Schema/contracts and validation tests: bounded inputs, URL policy, ownership, migration additive/reversible.
2. Create/list/detail: atomic credentials, idempotency, quota, list without secrets; real authenticated HTTP tests.
3. Update/delete/callback/activity/rotation: persistence, IDOR on every operation, immutable IDs/status, audit without secrets, rollback and concurrency tests.
4. Logo storage and cleanup: invalid MIME/content/size rejection, owner-bound tickets, square Cloudinary transformation and cleanup retry.
5. BFF and dashboard: allowlisted APIs, CSRF, request bounds, create dialog, loading/empty/error/retry and mobile drawer.
6. Detail editor: persisted drafts, dirty navigation warning, callbacks, copy and confirmed rotation, sandbox/review placeholders.
7. Verify backend suite, FE typecheck/build/tests, real loopback SMTP/BFF integration and browser UI. Document any unavailable external storage/production checks honestly.

## Commands
BE build: `dotnet build viora-BE.sln --no-restore`
BE tests: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore`
Migration: `dotnet ef migrations add AddDeveloperMiniAppManagement --project Viora.Infrastructure --startup-project viora-BE` (generate only).
Local integration host: `dotnet run --project tools/DeveloperE2EHost`
FE: `npm run web`, `npx tsc --noEmit`, `npm run test:developer`, `npm run build:web`.

## Acceptance
Authenticated developer can upload/create/list/edit/reload/delete own apps, view/copy/rotate encrypted credentials and persist valid callbacks. Other developers cannot access any resource; no secret appears in list/detail/audit/storage/log/SSR. Transaction errors leave no partial app. Required tests and actual E2E results recorded in handoff; unrun checks remain explicitly unverified.

## Completion evidence
All seven implementation tasks are implemented. BE 107 passed/1 existing skipped; FE 22 passed; strict TypeScript, focused lint, API/host build, migration-model/DDL checks and web export passed. Real SMTP/BFF/BE/SQLite/Cloudinary E2E passed three times. Browser workflow verified without printing secrets. See `docs/handoffs/developer-platform-stage-3.md` for exact scope and unverified production/file-picker/long-running-cleanup checks.
