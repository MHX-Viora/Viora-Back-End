# Auth Bridge review and publication

## Objective and acceptance
Complete the existing Developer draft -> submit -> Admin decision -> publish ->
ANKT web/native catalog path using Auth Bridge, without legacy OIDC/executor setup.
Only complete metadata, a verified domain and a fresh, successfully exchanged,
owner/source/configuration-bound Sandbox Bridge session permit submission.
Approval remains separate from publication; unpublish/suspend revoke access.
No deployment, live migrations, commits or partner-app edits in this task.

## Structure and conventions
Extend `Viora.Infrastructure/Developers` services and existing Developer/Admin
parsers. Keep snapshot schema 1; evidence schema 2 identifies Bridge server proof,
while schema 1 retains legacy evidence validation. No database schema change.
Use typed records, bounded queries and `DeveloperAuthException`, e.g.
`throw MiniAppReadinessService.Changed();` for stale configuration.

## Ordered tasks and validation
1. Add failing xUnit tests for Bridge readiness, stale/foreign/revoked/config-drift
   evidence and the complete submit/approve/publish/catalog/revoke flow.
2. Implement server evidence checks and transaction revalidation; enable review
   by default when Auth Bridge is enabled, respecting explicit opt-out.
3. Accept evidence schema 2 in Developer/Admin; explain server evidence and offer
   Sandbox -> review navigation. Preserve existing UI and legacy schema 1 parsing.
4. Run focused backend/Node tests, backend build, frontend/Admin typechecks and
   browser verification where available. Record limitations in the handoff.

## Evidence semantics
Schema 2 runtime/bridge/token checks attest to a successful origin-bound S256
exchange recorded by the server; UserInfo checks server-side profile availability.
They do not claim browser executor observation or partner visual correctness.
All checks must be fresh (5 minutes); exact session/configuration is rechecked
inside submission's transaction. Historical sealed evidence remains readable
after the source session expires. No tokens or secret values enter attestations.

## Commands
Backend: `.codex-tmp/sdk8-check/sdk/dotnet.exe test Viora.Application.Tests --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~MiniAppBridgeReview`.
Frontend: `node --import tsx --test features/developer/tests/mini-app-review.test.ts`;
`npx tsc --noEmit`; Admin: `npm run build`.

## Boundaries
Always preserve pre-existing dirty work, ownership, concurrency, audit and sealed
version checks. Never bypass Admin review or trust frontend success messages.
Production configuration, device verification and deployment are separate work.
