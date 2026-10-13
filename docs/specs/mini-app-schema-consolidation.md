# Mini App schema consolidation

Implement the approved 28-to-25 table proposal in
`docs/plans/mini-app-schema-consolidation.md`. Editorial table merge is deferred.

Requirements:
- Remove custom launch-code persistence and retire internal custom authorization
  and exchange as HTTP 410-compatible failures. Keep active OIDC and runtime APIs.
- Store typed Developer review events in MiniAppAuditLogs. Preserve IDs, timestamps,
  statuses, version, reviewer, public reason and private notes. Keep public/admin
  history DTOs compatible and filter generic activity events out of review history.
- Store each account's clear-history cutoff on Accounts; preserve existing cutoffs,
  account isolation, new launches and audit/analytics data.
- Add new migrations rather than changing historical migration files. Copy and
  verify existing data before dropping the three superseded tables. Fail safely
  on ID collisions or unexpected active legacy codes.
- No production migration, database connection or secret changes during this task.
  Verify migrations on a dedicated local PostgreSQL database with realistic rows.
- Regression-test developer review privacy, runtime sessions, catalog history,
  OIDC PKCE/replay/revocation and schema migration preservation.

Deployment: stop application writers, take a verified backup, apply the new
AppDbContext migration, then start the matching BE release. OpenApiDbContext schema
is unchanged. A migration rollback cannot recover expired/retired legacy codes;
full restoration requires the predeployment backup.
