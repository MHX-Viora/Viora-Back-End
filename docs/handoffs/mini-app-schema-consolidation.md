# Mini App schema consolidation

Status: implemented and verified locally on 2026-10-13. Production not migrated.
This completes the consolidation already present in the working tree; unrelated
existing modifications were preserved.

## Result

Repository inventory: 24 platform tables + 4 OpenIddict tables becomes 21 + 4.
Accounts and EF migration history are outside this feature table count. No live
database inventory or production row counts were inspected.

| Removed table | Replacement |
| --- | --- |
| DeveloperReviewHistory | MiniAppAuditLogs with EventType = DeveloperReview and typed review fields |
| MiniAppHistoryStates | Accounts.MiniAppHistoryClearedBefore |
| MiniAppLaunchCodes | Retired custom protocol; existing OpenIddict authorization/token store |

Review IDs, timestamps, reviewer, version (including 0), previous/new status,
public reasons and private notes are copied exactly. Public review DTOs still
hide private notes; activity events do not appear in review history. History
clears affect discovery only, preserving launch/audit records and account isolation.
Runtime sessions, consent checks and current OIDC endpoints remain in use.

Keep banner/placement tables separate for now; merging them offers only one
fewer table with additional conditional constraints. Keep versions/drafts,
verification proofs, permission grants and protocol tables separate because
their lifecycles and constraints differ.

## Migration and rollback

New migration: `20261012094401_ConsolidateMiniAppSchema`.
SQL artifact: `docs/deployment/mini-app-schema-consolidation.sql`, generated
from the preceding media/discovery migration to this migration. It includes
the EF history entry and a transaction; it is a one-time upgrade, not an
idempotent baseline. Existing setup scripts describe earlier schema versions;
follow them with this upgrade when installing from those versions.

The migration locks source/target tables against writers, rejects colliding
review IDs and unused unexpired legacy codes, copies records, verifies every
review field and history cutoff, then drops superseded tables. Failure rolls
back the transaction. Apply with normal EF transactional migration execution;
do not split SQL statements into individually committed operations.

Deployment: stop writers; take and rehearse restoring a verified backup;
confirm the previous migration is applied; apply the new AppDbContext migration;
start the matching backend. No OpenApiDbContext migration is introduced here.
Existing historical migration modifications predate this task and must be
reviewed against deployed migration history separately.

Down restores review records, including decisions created after Up, and account
cutoffs before removing consolidated columns. It recreates empty launch-code
storage; expired/used legacy codes cannot be recovered. History-state row IDs
and CreatedAt are regenerated; their functional ClearedBefore is preserved.
Use the predeployment backup for an exact full restoration.

## Verification

- Backend: 197 passed, 1 unrelated money-audit PostgreSQL test skipped because
  its separate test DB variable was not configured; 0 failed.
- Three migration tests ran on an isolated local PostgreSQL 18 database:
  preservation and Up/Down/Up, collision rollback, active-code rollback.
- PostgreSQL catalog concurrency and runtime launch/close tests ran successfully.
- OIDC tests cover PKCE, code replay, runtime-session binding and revocation.
- SDK: 13 passed, 0 failed.
- EF model matches AppDbContext snapshot; SQL artifact generated successfully.
- `git diff --check` passed.

Reproduce backend tests with `VIORA_MINI_APP_TEST_DB` pointing to a dedicated
local database whose name contains `test`, then run:

```powershell
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false
node --test tests/mini-app-sdk.test.cjs examples/mini-app-sdk.test.mjs
```

No production migration or credential/configuration changes were performed.
