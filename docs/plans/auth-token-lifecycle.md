# Implementation Plan: Access/Refresh Token Lifecycle

## Architecture Decisions

- Dual transport: HttpOnly cookie remains authoritative on Web/legacy; optional JSON refresh token enables native SecureStore.
- Session IDs are server-generated per login and preserved across rotation. Legacy null-session rows receive a session ID on first successful refresh.
- Rotation is revoke-old + insert-new in one repository transaction. A filtered unique index enforces one unrevoked token per non-null session.
- Cleanup runs as one hosted job, not in request paths.

## Tasks

- [x] Backend contract/model migration
  - Acceptance: session lineage, expiry metadata, safe indexes, nullable legacy backfill strategy.
  - Verify: model/migration tests and build.
- [x] Backend rotation/reuse/logout/cleanup
  - Acceptance: atomic single winner, reuse revokes session, logout is device-local, retained cleanup only.
  - Verify: focused service tests, then backend suite.
- [x] Client secure token lifecycle
  - Acceptance: native stores refresh separately in SecureStore; Web is cookie-only; refresh saves both tokens.
  - Verify: storage and auth contract tests.
- [x] Client automatic refresh lifecycle
  - Acceptance: single-flight 401 handling, one retry, startup refresh, invalid-session clearing, one realtime restart.
  - Verify: coordinator/bootstrap/contract tests, full client suite.
- [x] Operational documentation and review
  - Acceptance: safe production audit/cleanup SQL, root-cause handoff, auth/realtime invariants.
  - Verify: migration script, build/typecheck/lint, five-axis security review.

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Legacy rows have no session identity | Keep `SessionId` nullable; never deduplicate active rows by account |
| Concurrent rotation creates two active rows | Conditional revoke in transaction plus filtered unique index |
| Web exposes refresh credential | Do not persist response refresh token on Web; retain HttpOnly cookie |
| Native cookie jar loses rotated cookie | Send/store refresh token through JSON + SecureStore |
| Realtime reconnect storm | Restart only inside the single refresh operation; token factories read current storage |
| Cleanup removes usable sessions | Delete only rows whose expiry/revocation is older than retention |
