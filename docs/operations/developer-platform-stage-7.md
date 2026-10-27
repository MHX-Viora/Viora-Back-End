# Stage 7 review and publication operations

The module remains disabled by default (`MiniAppReview:Enabled=false`). Local
acceptance and outstanding items are authoritative in the
[handoff](../handoffs/developer-platform-stage-7.md). This document does not
authorize deployment, application-database migration, email or Stage 8 work.

## Coordinated rollout

1. Review the Stage 7 [spec](../specs/developer-platform-stage-7.md),
   [plan](../plans/developer-platform-stage-7.md), security map and final acceptance.
   Deploy matching backend, Developer BFF/Portal and Admin Portal contracts while
   the module is disabled. Admin DTO validation deliberately rejects unknown or
   missing fields; refresh old cached bundles during a coordinated rollout.
2. Review generated SQL, back up the intended database and retain the existing
   encryption/signing keys. Stage 7 adds four version/review/publication/inbox
   migrations and `ScopeMiniAppDomainProofsByHostname`, following Stage 6's
   `AddSandboxExecutorLaunchBinding`. No automatic startup migration is enabled.
3. Inventory legacy Published apps. They do not become reviewed or approved
   versions. Explicitly quarantine/unpublish or onboard them through the workflow;
   do not manufacture approvals from `MiniApp.Status`.
4. Configure the already existing SSO, domain checks and trusted Sandbox executor.
   Verify deployed TLS, DNS, Portal/BFF Origins, actual CSP/frame headers and
   partner embedding policies separately from local fixture evidence.
5. Configure the existing mail transport and verify a separately authorized test
   recipient. Review inbox/outbox has Developer recipients; social notifications
   and User IDs are not its transport. SMTP acknowledgement can be ambiguous:
   persist the original event, inspect delivery leases/attempts, avoid blind resend.
6. Enable the module only after the operator approves acceptance and all matching
   artifacts are present. Verify real Admin password/session, owner/version
   confirmation, domain readiness, inbox and exact-version Production issuance.

## State and recovery

- Draft content, review decision, publication and operational state are separate.
  Approve does not publish. A later working edit cannot replace the frozen target.
- A lost command response must be retried with the original request ID, target,
  expected revisions and payload. After 409, reload and review the current state;
  do not generate IDs in a blind retry loop. No client-side status edit grants
  authority. Only the assigned live Admin can decide an InReview request.
- Owner unpublish removes only this app's Production authority. Admin suspension
  closes its publication, cancels/revokes its Sandbox runtime authority and revokes
  its scoped OAuth grants. Other apps and identity sessions must remain intact.
- Restore requires live safety and leaves the app unpublished. Old tokens and
  sessions stay invalid. Owner must explicitly publish an approved target again;
  user consent must be obtained anew.
- Missing SMTP is an explicit failed delivery, not a successful email. The worker
  uses bounded batches, five attempts and owned processing leases. Use existing
  audit/event IDs and delivery state for diagnosis; never export secrets, full
  command journals, protocol tokens or private fixture profiles into reports.

## Rollback boundaries

Before first enablement, the disabled default preserves the earlier paths. After
Stage 7 has been used, **turning the flag off alone is not a safe rollback**: it
re-enables earlier mutation/runtime behavior. Prefer roll-forward with the review
authority intact. For a separately approved rollback, first stop new issuance,
explicitly unpublish/quarantine affected apps and revoke their projections/grants
using the current services. Verify no active Stage 7 publication or legacy
Published app remains before reverting binaries or schema.

Down migrations discard Stage 7 history. The publication guard acquires exclusive
MiniApps/Publications locks before checking state; execute the downgrade as one
transaction, as EF does. Do not run `--no-transactions` SQL without an enclosing
transaction. Keep locks through all operations until commit/rollback. Backups,
app metadata and encrypted credential keys must be recovered together; do not
resurrect tokens or mark old closed publication rows Published manually.

## Reproducing isolated verification

Local acceptance completed on 2026-10-22: both Public/Confidential A–D runners
exited 0 after cleanup. Final logs:
`.codex-tmp/stage7-resume-e2e-{public,confidential}.log`.
The browser harness waits for all 18 server checks to complete before requesting
readiness; a trusted executor result alone does not mean the server run is done.

The E2E host clears configuration sources. It starts its own loopback PostgreSQL
cluster, applies real migrations, generates isolated TLS/encryption material and seeds
only fixture identities/configuration/domain proof. It creates no seeded Stage 7
Approved/Published rows. Actual Developer BFF login and Admin password login drive
the module. The SSO social identity uses the fixture's explicit CSRF bootstrap.
Admin bundle uses `envDir:false` and a relative API, never an application `.env`.

From the backend root, with the existing local SDK or equivalent installed .NET 8:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .codex-tmp/sdk8-check/sdk).Path
$env:MSBUILDDISABLENODEREUSE = '1'
& "$env:DOTNET_ROOT/dotnet.exe" build tools/SsoE2EHost --no-restore -m:1 -p:UseSharedCompilation=false
node tools/MiniAppReviewE2E/fixture.test.mjs
node tools/MiniAppReviewE2E/run-fixture.mjs --all-flows
node tools/MiniAppReviewE2E/run-fixture.mjs --all-flows --confidential-partner
```

Run variants sequentially: they own fixed loopback HTTPS ports 443/5490/5493/5494/
5495 and fresh Chrome profiles. The existing PostgreSQL 18 and Chrome installation
are used; no package or browser download occurs. On this workstation, process
execution may need an elevated tool context. Source builds/types/lint and full
suite results are listed in the handoff. Do not package `.codex-tmp`, keys, TRX,
databases, SDK copies or browser profiles as application delivery.

The runner has a bounded deadline, cooperative stdin/IPC shutdown and a child
termination fallback. Browser handles and temporary-profile cleanup belong only
to that run. The isolated lifecycle test verifies normal cooperative cleanup.
Deliberately lost submit replies are transport faults after a real commit; they
never manufacture a business result. Supplemental rejected-target HTTP probes
reuse the real BFF-authenticated Developer source entirely inside fixture memory.
Stale-domain/client controls modify only that fixture's seeded source safety;
restoring a domain resets its original local proof, never a review/publication.

Local evidence does not prove public domain ownership, provider-deployed headers,
real SMTP delivery, native WebView behavior or application-production rollout.
Those remain explicit prerequisites to operator launch and any Stage 8 planning.
