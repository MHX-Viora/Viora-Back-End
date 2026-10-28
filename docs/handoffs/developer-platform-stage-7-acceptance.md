# Stage 7 acceptance record

2026-10-22. **Stage 7 complete within local acceptance scope.** Public and
Confidential A–D workflows both passed; each runner exited 0 after cleanup.
Scope is the accepted 53-task
[plan](../plans/developer-platform-stage-7.md); no Stage 8 implementation.

| # | Area | Implementation / verification |
| --- | --- | --- |
| 1 | Architecture | Separate immutable version, review, publication and operational dimensions; existing Developer, Admin, Sandbox and OpenIddict boundaries retained. |
| 2 | Schema | Exact app/version/review FKs, one working draft/active review/publication, append-only decisions/audit and idempotent receipts; Developer inbox/outbox uses its own identity. |
| 3 | Migration | Existing Stage 7 migrations exercised on disposable PostgreSQL, including Up/Down/Up, immutable triggers and guarded transactional downgrade. Offline EF comparison reports no pending model changes. No application database migrated. |
| 4 | API | Owned version/review/readiness/publishing/inbox routes; live signed Admin/session policies and bounded list/detail/actions. Admin app filters apply before count/page. Strict allowlisted clients reject malformed, wrong-target and secret-bearing responses. |
| 5 | Developer UI | Draft/submitted/live views, frozen evidence, explicit submit/publish/unpublish, captured-revision retries, private credential preparation, inbox/deep links and existing unsaved-navigation guards. |
| 6 | Admin UI | Queue/overview/history, grouped frozen detail/timeline, start/assign/approve/request changes/reject, app/publication history and reasoned suspend/restore. One existing navigation module; server capabilities govern actions. |
| 7 | State machine | One active review and one terminal decision; approval never publishes. Exact approved target required for publication. Suspension closes publication/revokes authority; restore leaves it unpublished and requires explicit republish/fresh consent. |
| 8 | Versioning | Canonical bounded snapshot/hash, sealed evidence, retained logo/domain targets and candidate Production credentials; mutable draft cannot replace reviewed/published configuration. |
| 9 | Notifications | Transactional recipient-scoped Developer inbox/outbox, safe links, monotonic read state, bounded leased email retries and deduplication. Fixture mail transport sends no real mail; SMTP ambiguity remains documented. |
| 10 | Audit | Typed actors, payload-bound request IDs, before/after/correlation and append-only records in the same transaction as state/inbox/outbox. Forced failure tests prove rollback. |
| 11 | Security | All 22 requirements map to executed assertions in the [security record](../testing/developer-platform-stage-7-security.md); IDOR/CSRF/role/session/CAS/secret/source/retention bypass coverage. Actual browser malicious reasons remain escaped text. |
| 12 | Unit tests | Backend 695 passed, one existing unrelated MoneyAudit skip; Developer/BFF 63; Admin 18; SDK 37; host/executor 23. Frontend TypeScript/lint and Admin production build pass. |
| 13 | Integration | Actual API/service/EF/OpenIddict suites, transaction rollback, source revalidation, scoped revocation, BFF budgets and strict DTO checks. Release solution: zero warnings/errors. |
| 14 | E2E | Public and Confidential A–D passed: actual Developer BFF login, Admin password/live session, five fresh 18-check Stage 6 runs per variant, decision/inbox/publication and Production PKCE/code/token/ID-token/UserInfo. Changes/new drafts, rejected-target 409, suspension/held-code/token revocation, restore-unpublished/explicit republish/fresh consent, immutable draft isolation and stale domain/client denial passed. Lost-reply exact replay, stale Admin dialog conflict and responsive/keyboard/runtime checks passed. No seeded Stage 7 Approved/Published rows. |
| 15 | PostgreSQL | All full-suite tests use owned disposable clusters; real migration, uniqueness, immutability, assignment/decision/publish/suspend races. Lifecycle test verifies cooperative stop and deletion of its fixture. |
| 16 | Remaining issues | No open Stage 7 local acceptance blocker. Existing Admin >500 kB bundle warning, EF ReportReason sentinel advisory and one unrelated MoneyAudit skip remain. Standalone historical account E2E was not executable without its separate DeveloperE2EHost/Portal; it failed connection before business assertions. Stage 7 auth is exercised by its own real-host workflows. Production rollout checks remain separate prerequisites. |
| 17 | Stage 8 prerequisites | Operator-reviewed rollout/backups/feature flag, real domain/header/SMTP checks and deployed Production acceptance; decide Stage 8 scope separately. Keep feature flag off by default. No deployment, real mail, live publication, auto commit or Stage 8 work performed. |

## Final verification artifacts

- Backend: `.codex-tmp/stage7-final-{backend-tests,release,model}.log`,
  `Viora.Application.Tests/TestResults/stage7-final.trx`.
- Developer: `../viora/.codex-tmp/stage7-final-{tests,types,lint,sdk}.log`;
  existing `.codex-tmp/stage7-developer-final-export` is the reviewed Expo UI served
  by the real fixture. BFF remains real, including its 45s review preflight budget.
- Admin: `../viora-admin/.codex-tmp/stage7-admin-final-{tests,build,lint}.log`.
- Host/executor: `.codex-tmp/stage7-final-host-executor.log`.
- Browser: `.codex-tmp/stage7-resume-e2e-{public,confidential}.log`,
  `.codex-tmp/stage7-admin-screenshots/review-{320,375,768,1440}.png`.

Backend/Developer/Admin/SDK/host regression and build evidence above is the verified
2026-10-21 baseline. The 2026-10-22 continuation changed only E2E coordination,
bounded timeout diagnostics and documentation; application/schema source did not
change, so those passing suites were not repeated. Both full browser variants ran
against the real host/BFF and freshly built isolated Admin bundle on 2026-10-22.
The initial continuation reproduced a Sandbox readiness timeout: trusted-browser
completion preceded server-check completion. The harness now waits for all 18
server checks before reading readiness; final A–D runs pass without relaxing guards.

Logs, screenshots, TRX, SDK copies and disposable keys/databases are local evidence,
not application assets. [Operations](../operations/developer-platform-stage-7.md)
contains reproduction, rollout and rollback procedures. Turning the flag off alone
after use can expose legacy paths; roll forward or explicitly quarantine/unpublish
and revoke before downgrade. Never restore old tokens/consents as part of recovery.

The fixture clears application configuration, uses generated keys/TLS and only
loopback services, discards mail and creates fresh task-owned Chrome profiles.
Seeded identity/ownership/domain proof and explicit social-login bootstrap are
test infrastructure. No deployed-provider, public-DNS, native WebView or real SMTP
assurance is claimed. Current branch remains uncommitted for user review.
