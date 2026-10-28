# Mini App platform removal — 2026-10-15

User explicitly requested removal of the implementation and data on the configured
database, in preparation for rebuilding the platform.

## Removed

- Backend Mini App and Developer entities/configurations/DbSets, contracts,
  services, cleanup jobs, credential and domain verification, partner APIs,
  OpenIddict context/middleware/controllers and Developer-specific Google route.
- Admin Mini App review/editorial/policy pages, Developer Portal/auth/docs,
  routes, navigation, services/types and Firebase dependency used only by the portal.
- ANKT Mini App runtime/bridge/catalog, Developer routes and public docs, profile
  hints, Developer auth return hooks, dedicated SDK dependencies and CI docs step.
- SDKs, partner examples, deployment snippets, old feature docs/tests and old
  local diagnostics that could restore the retired source.

Shared ANKT account/login/Google, chat, wallet, notifications and unrelated working
changes are preserved. Historical AppDbContext migrations and SQL archives remain
for migration continuity; they are not active feature code.

## Database operation completed

Target: Neon `neondb`, host
`ep-damp-meadow-b3spr6v7-pooler.c-4.ap-southeast-1.aws.neon.tech`.
Both application and former OpenIddict contexts used DefaultConnection.

- Initial inspection: 26 existing feature tables, including one Mini App and one
  Developer. Feature membership tables from the pending upgrade did not yet exist.
- Created a compressed SQL schema/data backup outside the repositories:
  `C:\Users\minec\AppData\Local\Temp\ankt-mini-app-backups\mini-app-20261015-030822.sql.gz`.
  Includes the account-owned MiniAppHistoryClearedBefore values, if any.
- Locked feature tables; verified backup by dropping/restoring them inside a
  savepoint and checking every feature table's row count; rolled back the probe.
- Applied pending `20261013072528_UpgradeDeveloperPlatform` and new
  `20261015030504_RemoveMiniAppPlatform` in the same transaction. This keeps EF
  history consistent rather than leaving an older pending feature migration.
- New migration uses an explicit 28-table allowlist with `DROP TABLE IF EXISTS`
  and `RESTRICT`, including OpenIddict tables/history and the two temporary
  membership tables introduced by the pending upgrade. It removes only the
  MiniAppHistoryClearedBefore column from Accounts. Generic EmailVerifiedAt stays.
- Compared row counts for Accounts, Users, Posts, Conversations, Messages, Wallets,
  WalletTransactions, Payments and Withdrawals before committing: unchanged.
- Post-operation read-only verification: zero feature tables; zero pending migrations.

Down intentionally throws: deletion cannot restore rows. Restore the backup and
reconcile EF migration history before any future platform reintroduction. The
backup lives in a temporary directory; move it to approved durable storage if needed.

## Verification

- Backend restore/build and full remaining test suite: 63 pass, 1 skip
  (unconfigured isolated PostgreSQL money-audit test DB).
- Runtime model/API retirement guards: core entities/controllers retained;
  feature entities/controllers absent; destructive migration restricted to allowlist.
- EF has-pending-model-changes: no pending model changes.
- Admin production build, ESLint and 21 remaining frontend tests: pass.
- ANKT TypeScript, full Expo lint and web export: pass.
- Shared-file diff checks: pass. Source scan finds no feature runtime references;
  remaining names belong to historical migrations and retirement guards/docs.
- Browser runtime verification unavailable: Chrome DevTools profile already in use;
  no user browser was closed to bypass it.

## Delivery status

Database deletion is already applied. Code changes remain local and uncommitted;
no push/deployment or native rebuild was performed. Deploy all three updated codebases
together; older deployed Mini App clients/endpoints no longer have supporting tables.
