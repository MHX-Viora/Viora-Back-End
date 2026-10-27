# Stage 10 — PostgreSQL migration and recovery evidence

2026-10-23. PostgreSQL 18, test-owned loopback clusters, generated names/paths.
No configured application connection string, service or data was used.

`Stage10DatabaseRecoveryTests.OldDeveloperDataSurvivesFullUpgradeAndIndependentDumpRestore`
executes the real 71-migration chain through
`20261022043902_AddMiniAppProductionAudit`:

1. Migrate a new owned cluster to `AddIndependentDeveloperAccounts`.
2. Persist a synthetic Developer account; retain identity, email and security revision.
3. Apply all remaining migrations and verify no pending migrations/data loss.
4. Create a separate owned database and perform real custom-format `pg_dump` and
   `pg_restore --exit-on-error --no-owner`, using explicit libpq connection flags.
5. Compare 2,561 column/index/constraint/trigger entries and migration history.
   PG18 rewrites array casts/BETWEEN when reparsing restored DDL; CHECK definitions
   on both sides are canonicalized by PostgreSQL in empty temporary tables.
   Expressions are not dropped or regex-normalized to force equality.
6. Compare recovered account data/security revision and prove a foreign-owner
   session insert still fails the restored foreign key.
7. Delete the generated archive and stop/delete only the owned GUID cluster.

Focused successful drill (`recovery.trx`): backup **309 ms**, restore **910 ms**.
These small synthetic local timings are not production RPO/RTO. No live key or
production-volume restoration was attempted. Each recovery run records its own
timing; later regression timing need not match this sample.

Existing full-suite PG tests also verify uniqueness, immutable review/version/
catalog/audit triggers, one-winner publish/launch/redeem races, row locks,
source revocation, ownership, failed transaction rollback and retention guards.
Relevant classes: `MiniAppPublishingPostgresTests`, `MiniAppProductionRacePostgresTests`,
`MiniAppProductionGrantPostgresTests`, `MiniAppReviewMigrationTests`,
`MiniAppProductionCatalogMigrationTests`, `MiniAppProductionRetentionPostgresTests`.
SQL-generation migration tests inspect additive DDL and authorization projection
boundaries. Historical `Down` tests are fixture checks, not production rollback advice.

Limits: the old checkpoint contains synthetic Developer data, not a representative
production-size Social/Wallet/media dataset or every historical deployment version.
Live database roles, pool sizing, storage encryption, retention/legal policy,
backup scheduling, encryption-key recovery and offsite restore remain unverified.
The separate MoneyAudit test is skipped without its explicitly isolated audit DB.
It was not pointed at application data to obtain a green result.

Reproduce from BE using the installed isolated SDK:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .codex-tmp/sdk8-check/sdk).Path
& "$env:DOTNET_ROOT/dotnet.exe" test Viora.Application.Tests/Viora.Application.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~Stage10DatabaseRecovery --logger 'trx;LogFileName=recovery.trx' --results-directory .codex-tmp/stage10
```

`ANKT_TEST_PG_BIN` may identify installed test binaries; the helper creates its own
database connection. Missing binaries/permissions are failures, never silent skips.
