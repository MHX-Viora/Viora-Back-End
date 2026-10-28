# Developer Platform Stage 7 — implementation checkpoint

Current status (2026-10-22): **Tasks 1–53 complete within local acceptance scope.**
Real-host Public/Confidential A–D passed, with exit 0 and successful cleanup for
both runners. See the 17-category
[acceptance record](developer-platform-stage-7-acceptance.md) for final results.
Historical checkpoints below retain their original scope and counts.

Historical initial checkpoint, 2026-10-21. The user request authorized the
existing [spec](../specs/developer-platform-stage-7.md) and
[53-task plan](../plans/developer-platform-stage-7.md). Stage 6's final
[acceptance](developer-platform-stage-6-acceptance.md) supersedes its older
incomplete handoff entries. Preserve all pre-existing BE/FE working-tree changes.

## Tasks 1–8: persistence foundation

- Added version, review request/action, publication, operational state, Developer
  inbox and email delivery entities, typed boundary records and EF mappings.
- Separate version/review/publication/operational states. No approval/publication
  is seeded from the legacy `MiniApp.Status` field.
- Unique app/version code, one draft/app, one active review/app and version, one
  active publication/app. Composite FKs prevent cross-app/version association;
  publication must reference the exact version targeted by its approved review.
- Migration-only PostgreSQL triggers seal submitted version identity/configuration,
  review evidence/target and terminal decisions; audit is append-only. Publication
  targets and closed history cannot change or be reopened. Receipt identity is
  fixed, inbox read timestamps cannot be reset, sent delivery cannot be requeued.
- Managed-logo FK retains version-pinned uploads; Task 17 now also protects the
  remote deletion boundary (see the continuation below).
- Inbox recipients are Developer accounts; no synthetic social User/Notification
  recipient. Outbox has event/recipient/channel deduplication, bounded attempts and
  consistent processing leases. No email transport/worker has been added yet.
- Explicit publication downgrade guard rejects active publications and any legacy
  Published app until an operator explicitly unpublishes/quarantines it. Removing
  these migrations destroys the corresponding Stage 7 history.

Migrations (authored SQL plus EF-generated Designer/snapshot):

1. `20261021011642_AddMiniAppVersions`
2. `20261021012227_AddMiniAppReviews`
3. `20261021012630_AddMiniAppPublications`
4. `20261021013047_AddMiniAppReviewNotifications`

All migration execution so far is against internally created, disposable loopback
PostgreSQL clusters. **No application database migration, deployment, actual
publication, email, commit or Stage 8 work.** Do not deploy this checkpoint: runtime
publication authority and review workflow services are not integrated yet.

## Tasks 9–10: authorization and canonical snapshot

- `MiniAppReviewAccess` rechecks owned app, live Developer session/absolute expiry,
  active/unlocked Developer account; foreign/deleted resources are 404. Live Admin
  access requires Admin=2, active/nondeleted account and nonrevoked, unexpired
  refresh-token row matching signed `sid`. No Sandbox tester authority is granted.
- `MiniAppReviewRegistration` defines Read/Decide/Suspend Bearer-only policies with
  signed access-token/role claims plus the live database requirement. They are
  ready for later endpoint registration; existing Program/authentication is unchanged.
  Actual signed JWT HTTP tests cover revoked session, stale role, missing/wrong sid,
  Moderator, Developer authentication scheme and all three named policies.
- Typed snapshot is schema 1 configuration only. Ordinal arrays are sorted/deduped;
  URI handling follows existing Stage 5 normalization and exact callback comparison.
  Client identities stay opaque. No credential/protocol envelope is accepted.
- Canonical UTF-8 JSON is capped at 64 KiB, SHA-256 hashed with lowercase hex and
  revalidated on read. Unknown/duplicate JSON fields, nested secret fields,
  noncanonical serialization, invalid type/scope/domain/client identity and
  mismatched hash fail with a safe stable error. Incomplete/SsoDisabled draft
  configurations remain representable without manufacturing clients or readiness.

## Verification and recovery

- RED: new persistence types absent; each new migration absent before scaffolding.
- GREEN: initial persistence checkpoint had 29 focused tests; SQLite mapping/constraint/concurrency checks plus actual
  PostgreSQL Up/Down/Up, immutable SQL, duplicate receipt and parallel review tests.
- EF `has-pending-model-changes`: none, using a synthetic port-1 connection via
  `VIORA_EF_DESIGN_CONNECTION`. Design-time commands never load application secrets.
- Default SDK 9 build stalled; cancelled the task's invocation and used the existing
  isolated SDK 8.0.426 at `.codex-tmp/sdk8-check/sdk/dotnet.exe`, `-m:1`,
  `-p:UseSharedCompilation=false`, `MSBUILDDISABLENODEREUSE=1`.
- PostgreSQL child startup requires the tool's elevated execution context here;
  it still binds loopback and the existing `IsolatedPostgres` owns its temp cluster.
- Initial full regression: 514 pass / 2 fail / 1 existing MoneyAudit skip. Both
  failures were predecessor migration tests using `GetMigrations().Last()` after
  new migrations were added. Pinned their Stage 6 end to
  `AddSandboxExecutorLaunchBinding`; all existing protocol/preservation assertions
  remain. See `MiniAppSandboxPostgresTests` and `MiniAppSandboxExecutorTests`.
- Checkpoint A full regression: **516 pass / 1 existing MoneyAudit skip**;
  .NET 8.0.426 Release solution build: **zero warnings/errors**.
- Tasks 9–10: **30** focused contract/auth tests. Added PostgreSQL byte/object/hash
  constraints and nonempty encrypted-client/social-session metadata preservation
  across the version migration; these fixture values are not usable credentials.
- Code review found a downgrade/publish race between the guard query and DDL.
  Added a real PostgreSQL NOWAIT reproduction (RED: writer lock succeeded), then
  acquire MiniApps/Publications ACCESS EXCLUSIVE locks before checking state.
  Downgrade **must run transactionally**, as EF migrations do; do not execute a
  generated no-transactions script without an explicit transaction. The locks must
  remain until all downgrade operations commit/rollback. Tests wrap expected failed
  SQL in transactions and roll back before continuing.
- Final foundation regression after the downgrade fix: **548 passed / 1 existing
  unrelated MoneyAudit skip**, 549 total (61 Stage 7 tests added). No failures.
  Final SDK **8.0.426 Release solution build: zero warnings/errors**. EF model
  matches the fourth migration; no model changes followed that successful check.
- Reviewed persistence invariants, authorization, bounded DTOs/canonical JSON,
  migration scope, rollback and existing dirty edits using code-review-and-quality.
  Fixed the downgrade race above; no outstanding finding within Tasks 1–10.
  This review does not approve deployment or imply Tasks 11–53 are implemented.
- Tracked diff whitespace check passes with Windows CRLF handling; 28 authored C#
  files also have no trailing whitespace. No package, FE, Admin UI, global login,
  deployment configuration or existing Stage 6 protocol assertion was changed.

Local artifacts: `.codex-tmp/stage7-{version,review,publication,inbox}-*.log`,
`stage7-persistence-{checkpoint,regression,build,model,release}.log`,
`Viora.Application.Tests/TestResults/stage7-persistence-regression.trx`.
Foundation/lock artifacts: `stage7-{access,auth,snapshot,downgrade}-*.log`,
`stage7-foundation-{contracts,build,regression,release,final,final-release}.log`,
`Viora.Application.Tests/TestResults/stage7-foundation-final.trx`.
Never include logs, fixture profiles, keys, databases or SDK copies in delivery.

Reproduce from backend root with the existing local SDK (or an installed .NET 8
SDK); PostgreSQL tests may require elevated local process execution:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .codex-tmp/sdk8-check/sdk).Path
$env:MSBUILDDISABLENODEREUSE = '1'
$stage7Dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
& $stage7Dotnet build Viora.Application.Tests --no-restore -m:1 -p:UseSharedCompilation=false
& $stage7Dotnet test Viora.Application.Tests --no-build --no-restore
& $stage7Dotnet build viora-BE.sln --no-restore -c Release -m:1 -p:UseSharedCompilation=false
```

## Tasks 11–13 continuation — 2026-10-21

Implemented working version service and exact owned HTTP/BFF routes. Task 12 was
split into bounded backend wiring/fixture work, then BFF transport. Task 13 includes
the existing integration editor and redirect editor, since both mutate snapshot
inputs; omitting either would silently leave drafts stale.

- `MiniAppVersionService` allocates version codes server-side, captures only typed
  current metadata/Sandbox configuration, preserves desired Production intent and
  returns historical content from its stored snapshot. One working draft/app;
  sealed versions cannot be edited. Editing after submission creates the next code.
- Create checks app revision; update targets an explicit owned `versionId` and
  requires the draft revision. Serializable transactions + conditional app/version
  updates make parallel allocation single-winner. Actor/requestId/payload hash
  journal stores only a bounded original summary for replay. Replays still require
  a live source and ownership. No credentials enter snapshots, journals or DTOs.
- App revision, version and audit commit together; injected audit failure and
  source expiry during the command roll back all writes. Source is rechecked
  immediately before commit. Journal infrastructure remains local to version
  commands until Task 21 introduces the shared review audit service.
- API: GET/POST `.../versions`, GET/PUT `.../versions/{versionId}`. No-store,
  strict nested JSON/duplicate-field rejection, explicit `ssoEnabled` on updates,
  1 KiB create / 16 KiB update budgets; page/pageSize default 1/20, max 100000/100.
  Unknown/duplicate/invalid query fields are rejected. Actor and client identity
  cannot be supplied. Owner/foreign/deleted/source checks stay on the backend.
- Module reads default to 120/minute, mutations 30, future credential preparation
  5; bounded configuration and per-Developer 429 behavior are tested.
- BFF permits only this exact method/path catalog and the future Task 20 POST
  `.../versions/{versionId}/production-credentials`. That backend route remains
  unmapped/404. No Admin/runtime/evidence/result passthrough. CSRF, cookie/token
  separation, no-store/no-redirect fetches and route body limits are preserved.
  Both direct and Vercel rewrites preserve/reject full query parameters.
- Metadata, redirects, integration and Sandbox SSO editors synchronize an enrolled
  working draft inside their owning transaction. The first version is explicitly
  created through POST /versions; existing apps are not automatically enrolled.
  Corrupt snapshot/failed synchronization rolls back the original editor and audit.
  With the module enabled, legacy PUT `/sso/Production` returns
  `409 PRODUCTION_DRAFT_REQUIRED` before changing the live client or secret.
- New `MiniAppReview:Enabled` defaults **false**. Routes and version-sync hooks are
  enabled only for isolated fixtures here. When false, existing Stage 5/6 editors
  retain their behavior. **Keep it off for rollout until the remaining runtime,
  domain/asset and review/publication guards are complete.** Registering policies
  does not itself expose Admin review endpoints or grant Sandbox tester access.
- Initial broader regression exposed Npgsql wrapping SQLSTATE 40001 in
  InvalidOperationException. Added deterministic RED cases for the provider wrapper,
  then map only known uniqueness/serialization/deadlock/optimistic conflicts to
  safe 409 responses. Both version commands and the legacy editor filter handle
  the provider form. Other errors still propagate; no blind retry was introduced.

Verification at this continuation: 46 FE/Developer/BFF tests pass; focused backend
results and final full regression/Release after the provider fix are recorded below.
Logs: BE `.codex-tmp/stage7-version-{service-*,api-*,mutation-*,boundary-*,expiry-*,
required-*,transient-*,regression,release}.log`; FE
`.codex-tmp/stage7-version-{proxy-*,bff-regression}.log`. Backend HTTP tests use
actual isolated Developer register/verify/login; BFF rewrite tests use a mock
upstream. These are not the four full review/Production SSO E2E flows.

No new migration or dependency in Tasks 11–13; no UI, real mail, application DB,
deployment, commit or publication. Existing unrelated working-tree changes remain.

## Continuation: Tasks 14-20 (checkpoint B)

- Hostname-scoped proof uniqueness replaces one-proof-per-app. Working/Sandbox
  consumers always select their actual website hostname; Production selects the
  immutable published target. Website edits retain an old proof while a pending,
  approved or published target references it. Explicit revoke, regeneration and
  expiry remain effective. No proof TTL is extended by retention.
- Migration `20261021023658_ScopeMiniAppDomainProofsByHostname` changes only the
  active-proof index. Down locks the table and rejects multiple active hostnames
  until an operator explicitly resolves them. PostgreSQL Up/Down/Up preserves
  prior proof, client and session data. EF reports no pending model changes.
- Logo cleanup excludes every version-pinned asset, locks/rechecks each candidate
  before the fake/real storage boundary, handles retries and limits each batch to
  100. The upload claim also rechecks expiry in its conditional update. Tests use
  fake deletion only; no remote images were deleted.
- `MiniAppPublicationPolicy` now requires the actual current publication, its
  exact Approved review/version, canonical hash, live unlocked Developer, Active
  operational state and the bound unrevoked Production client. Missing state or
  legacy `MiniApp.Status=Published` fails closed. Runtime also checks the target's
  exact live hostname proof and Confidential credential, while Sandbox retains
  its existing working configuration and eligibility rules.
- Production type/scopes/callbacks/name/logo/SSO flag, browser origin, CORS and
  OpenIddict projection come from the approved snapshot. Editing the working app
  cannot change consent branding or Production configuration. Existing protocol
  fixtures were updated with explicit controlled policy metadata; this does not
  replace the required real review/publish E2E flows.
- Direct PUT `/sso/Production` now always returns `PRODUCTION_DRAFT_REQUIRED`,
  including when the module flag is false. Runtime publication checks are also
  unconditional. **The earlier Tasks 11-13 flag-off compatibility note is
  superseded for Production: keep deployment gated until all Stage 7 workflow/UI
  and rollout requirements are complete.** No legacy approvals are synthesized.
- Public draft intents reserve an inert candidate identity without a secret.
  POST `.../versions/{versionId}/production-credentials` explicitly prepares a
  Confidential candidate, binds the draft and returns an owner-only encrypted-store
  credential through a no-store response. Existing valid credentials are reused;
  draft edits do not revoke them or change the live client type/projection.
  Expired credentials require a new explicit command. Replay stores only safe
  identity/revision/credential IDs and rechecks live owner/source and credential.
  Strict 1 KiB request, per-owner 5/minute budget and BFF catalog are enforced.
- Credential preparation is transactional with snapshot/CAS/audit. Source expiry,
  failed audit and concurrent PostgreSQL writers cannot leave a partial candidate,
  duplicate active secret or duplicate journal. No review/history DTO contains a
  plaintext or encrypted credential.

Verification: Tasks 11-13 full backend 565 total / 564 passed / 1 pre-existing
MoneyAudit skip. Tasks 14-19 full backend 594 total / 593 passed / same skip.
Task 20 focused backend 25/25 passed; checkpoint B full backend 607 total /
606 passed / same skip; Developer/FE/BFF 46/46 passed. Release build
after Task 20 succeeded with zero warnings/errors. Final checkpoint B full results
are recorded in `.codex-tmp/stage7-checkpoint-b.log` and
`Viora.Application.Tests/TestResults/stage7-checkpoint-b.trx`.
Other logs: `.codex-tmp/stage7-{domain-isolation,domain-migration-green,assets-green,
production-green,production-regression,credentials-api,credentials-final,
checkpoint-b-release,domain-model}.log`. FE log:
`../viora/.codex-tmp/stage7-checkpoint-b-bff.log`.

No application DB migration, real email/storage operation, dependency change,
deployment, commit, business approval or application publication was performed.

## Continuation: Tasks 21-23 (transactional audit and delivery)

- Added shared typed-actor review audit, bounded JSON validation, payload-bound
  replay and correlation/before/after history. Version and candidate credential
  commands use it inside their owning transaction. It never grants permission or
  commits a command. Unsafe credential/token keys and duplicate JSON keys are
  rejected; callers supply only safe typed results. Existing append-only database
  triggers and actor FKs remain authoritative.
- Admin actions mirror to AdminLog only when the actual Admin Account has a real
  social User mapping. The mirror uses that User ID and a safe action/correlation
  summary. No synthetic social user or Developer recipient was introduced.
- Notification enqueue derives recipient and references from a persisted audit
  event, joins the same transaction and creates one Developer inbox receipt and
  one email outbox row per event/recipient. Duplicate enqueue returns the original
  receipt; rollback removes audit/inbox/outbox together. No mail is sent here.
- Added a module-gated background worker using the existing IDeveloperEmailSender
  SMTP boundary. It claims committed rows with CAS leases, limits each sweep to
  100 and each delivery to five attempts, retries transient errors with backoff,
  explicitly fails missing configuration/rejected recipients, and refuses to
  acknowledge an expired/replaced lease. It cannot dispatch inside a business
  transaction. SMTP timeout is 15 seconds; leases last two minutes.
- Tests prove two real PostgreSQL workers cannot share a live lease, uncommitted
  events are invisible, lost leases do not overwrite a new worker, batch/retry
  limits hold and no duplicate inbox is created. SMTP is always fake in tests.
  Delivery is at-least-once: an SMTP acceptance followed by process/DB failure can
  result in a retry email. Inbox/event deduplication remains exact; the existing
  SMTP interface supplies no exactly-once provider receipt.

Focused verification: shared audit integration 33/33; audit/inbox/outbox 17/17.
Full backend regression: **624 total, 623 passed, 1 pre-existing MoneyAudit skip**.
Release build: **zero warnings/errors**. Developer/FE/BFF remains **46/46 passed**.
Final full regression and Release logs are
`.codex-tmp/stage7-audit-outbox-{regression,release}.log`; TRX:
`Viora.Application.Tests/TestResults/stage7-audit-outbox-regression.trx`.
At the end of Tasks 21-23 no review submission/decision/publication endpoints or UI
had been added. The next continuation supersedes that backend status.

## Continuation: Tasks 24-30 (readiness, submission and Admin review)

- Review readiness adapts the real Stage 6 configuration/evidence reader. It
  compares the canonical working snapshot and managed logo, pins the newest
  completed run, requires the exact current Developer source session, and checks
  trusted browser/server-protocol provenance. Missing executor, expired proof,
  configuration drift, active review or missing candidate credentials blocks
  submission. SSO-off targets do not invent a Production client requirement.
- Network preflight has the existing 30-second Stage 6 budget and cannot join a
  business transaction. Database-only revalidation checks the same snapshot/hash,
  app/version revisions, proof/client/credential/operational fingerprints, run,
  original evidence timestamps and live actor again immediately before commit.
- POST owned `/reviews` seals the version and a bounded schema-1 attestation,
  then inserts review/action/inbox/outbox in one serializable transaction. The app
  row is locked without changing its configuration Version, preserving the
  fingerprint used by the submitted run. Source/evidence expiry or failed audit
  rolls back everything; payload-bound replay returns the original safe receipt.
- Owned GET review history/detail is paginated and returns frozen configuration,
  reason/change items, validated attestation and at most 100 safe action summaries.
  It does not expose credentials or raw audit results. Sealed evidence remains
  readable after the original run's five-minute window ends.
- Exact BFF catalog now includes owned readiness and review list/detail/submit.
  Both direct and Vercel routes preserve all query fields for validation. Page
  limits, duplicate/unknown filters, strict 1 KiB submit bodies, no-store, CSRF and
  Developer-session isolation are preserved; no Admin/evidence mutation proxy.
- Added Bearer-only Admin overview, filtered queue, safe detail/capabilities and
  Mini App list/detail. Queue names/config come from the sealed version; current
  working app and publication information are separate. Lists/pages/history are
  bounded; per-Admin read/mutation budgets use a separate actor partition.
  Every service also rechecks the live Admin role/session. Sandbox tester access
  is not required or granted by these review policies.
- Start atomically claims PendingReview. Explicit reassignment requires a live
  Admin target, expected review revision and a reason. Only the current assignee
  can decide. Reassignment is audited; only the first start creates a Started
  owner notification, and replay creates no duplicate event.
- Reject/ChangesRequested require a 10-2000 character reason; changes require
  1-20 bounded items from the field catalog. Terminal decisions use CAS and are
  immutable. Subsequent edits use a new version code; rejection does not change
  or revoke an earlier publication.
- Approve validates the exact sealed snapshot and attestation, then probes the
  target's public pages outside the transaction and rechecks live safety before
  and after audit/notification. It does not compare against the latest draft or
  require the old Sandbox source/run to remain live. Domain expiry, developer
  suspension/lock, corrupt evidence/hash or changed proof/client/credential blocks
  approval. Approval does not create a publication or modify the OAuth registry.
- Real isolated PostgreSQL races cover double submit, two Admin claimants,
  competing terminal decisions and double approval; each commits one winner and
  one corresponding audit/notification event. PostgreSQL triggers keep sealed
  configuration/evidence and terminal history immutable.
- The first broad regression exposed four new PostgreSQL fixture failures: wall
  clock timestamps created after slow signed-executor writes were ahead of the
  fixture's frozen clock. Synchronizing the fixture clock before reading proof
  fixed all four; no production freshness/future-time/provenance guard was relaxed.

Verification: backend Tasks 24-26 regression 643 total / 642 passed / 1 existing
skip; focused Admin reads 3/3, assignment 4/4, decisions 8/8 and approval 9/9.
Developer/FE/BFF **48/48 passed**. Final full backend regression: **664 total,
663 passed, 1 pre-existing MoneyAudit skip**. Release: **zero warnings/errors**.
Network-preflight transaction guard also has a failing-before/fixed-after test.
Final full workflow and Release evidence:
`.codex-tmp/stage7-review-workflow-final.log`,
`.codex-tmp/stage7-review-workflow-release.log` and
`Viora.Application.Tests/TestResults/stage7-review-workflow-final.trx`.
Focused RED/GREEN logs use `stage7-{readiness,submission,review-read,review-api,
review-proxy,admin-read,admin-api,admin-apps,assignment,decision,approval,
preflight-transaction,review-clock}-*.log` in BE/FE `.codex-tmp`.

Limits: policy/service tests use real submission and signed executor fixtures;
HTTP boundary tests use real Developer register/verify/login and a signed seeded
Admin account/session. These are **not** the four final browser/business E2E flows
or actual Admin-password-login acceptance. Checkpoint C E2E evidence remains
pending. No new schema migration/dependency, real mail, live application DB,
deployment, commit, business approval or application publication was performed.

## Tasks 31–33: exact-target publication and owner unpublish

- `MiniAppReadinessService.PublicationAsync` requires an explicit owned version,
  Approved review, canonical frozen snapshot and original sealed attestation.
  Rechecks owner/session, app/publication/operational revisions and target safety
  after public-page network probes. A new draft or expired original five-minute
  evidence window does not invalidate an approved target. SSO-off needs no client;
  SSO-on validates desired Production identity and live credential, without a
  circular requirement for a previously usable Production runtime.
- `MiniAppPublishingService` uses serializable transactions, app/publication CAS,
  payload-bound request replay and final live checks. Initializes Active state on
  first publish, closes previous publication as Archived before inserting the
  next, applies only approved Production type/scopes/callbacks, and keeps working
  metadata/Sandbox configuration separate. Audit/inbox/outbox commit atomically.
- `MiniAppOidcRegistry` verifies exact resulting type, callback/permission set,
  PKCE, consent, approved display name, registry/environment properties and the
  prepared Confidential secret. Missing runtime, secret decryption or projection
  failure cannot produce a successful publication. Public projection removes its
  old secret; registry credentials remain explicitly managed and are not rotated
  or revoked by publication/unpublication.
- `MiniAppPublicationRevocation` revokes tokens/authorizations by this app's
  Production application IDs and its Production consent rows only. Runs before
  replacing a publication as well as unpublishing. No social/Developer/Admin,
  Sandbox or other-application logout. Unpublish closes the exact row once,
  removes its Production projection, keeps Active state and allows a later
  explicit validated publication with a new history row.
- Added owned no-store readiness/history/publish/unpublish APIs behind the module
  flag with strict query/body limits and existing rate limits. BFF allows only
  the matching methods and preserves all direct/Vercel query fields for strict
  validation. Typed revisions/payloads are documented in the specification.
- PostgreSQL tests use the real submission/start/approve workflow and actual
  Stage 7 migration guards to prove one winner, exact replay, closed-history
  immutability and atomic receipts. The repeated-review executor fixture now
  updates only the current run's results and explicitly closes the old Sandbox
  session. Projection tests use a clearly labeled SQLite-only frozen SSO fixture;
  these do not replace actual SSO-enabled business/protocol E2E acceptance.

Focused checks: readiness 6/6; publication 3/3; projection 3/3; PostgreSQL publish
race/replacement 1/1; unpublish 3/3; scoped revocation 1/1; PostgreSQL unpublish
race 1/1. Developer/FE/BFF regression: 50/50. Full backend: 682 total / 681 passed /
1 pre-existing MoneyAudit skip. Release: zero warnings/errors.
Logs use `.codex-tmp/stage7-{publishing-readiness,
publish,publication,unpublish}-*.log`; full TRX is
`Viora.Application.Tests/TestResults/stage7-publication-final.trx`.

No application DB migration, real mail, deployment or commit. Both Portal UIs,
moderation/inbox APIs and final browser/business E2E acceptance remain pending.

## Tasks 34–36: Admin moderation and owned Developer inbox

- `MiniAppModerationService` and named Suspend-policy Admin APIs recheck actual
  signed Admin/session, reason and app/operational/publication revisions. Suspend
  initializes or updates authoritative state, closes active publication with its
  immutable reason/time, cancels queued/running Sandbox runs, consumes pending
  launch grants and removes runtime credentials. All state/audit/inbox/outbox
  changes use one serializable transaction with final source checks.
- Extended scoped revocation to both OAuth environments only for the suspended
  app. Removes its projections and revokes its consents/tokens/authorizations;
  retains unrelated applications and identity sessions. Runtime access and SSO
  eligibility/projection synchronization now consult the independent operational
  state, so editing legacy/draft status cannot bypass suspension.
- Restore probes the last suspended publication's exact approved target (or
  latest approved target if never published), outside the business transaction;
  checks frozen evidence and live developer/domain/credential safety again before
  state change and commit. Suspended state is the only intentionally ignored
  target blocker during recovery. Apps never reviewed/published still require a
  live unlocked Developer. Restore leaves every old publication closed, old
  grants/sessions revoked and Production projection absent. Owner must publish
  explicitly after new readiness. New Sandbox projection is permitted without
  restoring old consent or runtime authority.
- Added live Developer-session inbox reads and idempotent mark-read. Recipient
  comes only from claims; lists/counts are bounded and exclude foreign/deleted
  apps. ReadAt updates once and cannot predate the receipt if the clock lags.
  DTOs contain safe internal Portal links and references, no email-delivery
  state, journal payloads, credentials or partner redirects. BFF validates all
  direct/Vercel query fields and allows only list GET / individual read POST.
  The previously unused cursor scaffold now matches the approved page/pageSize
  API; no existing inbox endpoint/client was changed.

Focused checks: suspend 4/4; restore 4/4; moderation HTTP boundary 1/1;
both-environment revocation/restore projection 1/1; real PostgreSQL
suspend/publish race and restore 1/1; inbox service/HTTP boundary 2/2; inbox BFF
2/2. Full Developer/FE/BFF regression 52/52. Final backend: 695 total / 694 passed /
1 pre-existing MoneyAudit skip. Release: zero warnings/errors. EF model matches
the existing migrations (only the existing ReportReason sentinel advisory).
Tracked-file whitespace checks passed in both repositories.
Logs: `.codex-tmp/stage7-{suspend,moderation,
moderation-inbox,inbox}-*.log` and
`Viora.Application.Tests/TestResults/stage7-moderation-inbox-final.trx`.

These remain service/HTTP/library fixtures, including an explicitly synthetic
SQLite SSO target for projection tests. Actual Admin password login, the four
complete browser/business scenarios and the final 17-category report are still
required. No Portal action UI has been exposed and Checkpoint C/D E2E acceptance
is not claimed. No live DB migration, actual email, deployment or commit.

## Tasks 37–41: Developer Portal review, publishing and inbox

- Added strict typed version/review/readiness/publication/inbox clients in
  `viora/features/developer/lib/`. Reject unknown credential/actor/status fields,
  malformed IDs, unsafe integers, oversized collections and unsafe deep links.
  Validate exact response target IDs and frozen evidence references. Stage 6
  configuration fingerprints are uppercase hex; snapshot hashes remain lowercase.
- Replaced the review placeholder with separate draft/submitted/live summaries,
  selectable immutable snapshots, server-authoritative submit checklist and
  publication readiness. Checklist/details use accessible disclosures to keep
  primary actions visible. History has bounded pagination, decision reasons,
  escaped change items, links to configuration and new-draft/resubmit guidance.
- Submit/publish/unpublish use explicit exact-version confirmation dialogs,
  synchronous double-click guards, captured revisions/request IDs, safe errors
  and retry of the original payload after an ambiguous response. Generation
  guards discard stale or unmounted responses. Refresh after mutation reads
  current publications and the app status; the header cannot retain old Draft
  state after publish. Publication readiness must match the viewed snapshot hash.
- Production editing now writes only the version draft API. The old SSO tab's
  Production controls are read-only and lead to the draft editor; Sandbox retains
  its existing save flow. Confidential credential preparation requires explicit
  owner confirmation. Plaintext stays in the private component only, is cleared
  on hide/unmount/visibility loss and is never accepted from a late background
  response, persisted, logged or added to snapshots/journals. Clipboard copy is
  explicit. Unsaved/busy state participates in existing editor navigation guards.
- Added the authenticated Console inbox route/navigation with unread filter,
  counts, bounded pagination, idempotent read state and safe exact app/review/
  version links. Links with query strings now preserve full document navigation
  and unsaved/CSP boundaries. Invalid or mismatched focus cannot grant write UI
  capabilities. Existing social notifications remain untouched.
- Increased existing account transport budgets to 45s for the bounded review/
  publication network preflights. No package, backend API or schema changes.

Verification: Developer/FE/BFF **62/62**, TypeScript no-emit clean, targeted ESLint
zero errors/warnings, Expo Web export passed. Real Chrome UI fixture checks
covered submit, exact request retry after a lost reply, inbox/read/deep-link,
approved publish/unpublish, changes/new draft, Production draft editing and
credential preparation/clearing on real tab visibility loss. Four widths
320/768/1024/1440 had no horizontal overflow; console had zero runtime errors or
warnings. Screenshots were inspected at 320/1440. A failing browser regression
reproduced the stale header before its fix. Initial MCP screenshots stalled;
the fallback uses a fresh temporary Chrome profile and only that profile's
ephemeral debug port, then closes and safely removes it.

Evidence in `viora/.codex-tmp/`: `stage7-developer-final-{tests,types,lint,export}.log`,
`stage7-review-browser-final.log`, `stage7-header-publication-red.log` and
`stage7-review-{320,1440}.png`. Reusable UI fixture/harness:
`features/developer/tests/serve-review-ui.cjs` and `review-ui.browser.cjs`.
See the [Frontend handoff](../../../viora/docs/handoffs/developer-platform-stage-7.md).

Limits: browser data/transport are explicitly simulated and use no real account,
business approval, publication or protocol credential. They do not satisfy the
four final business E2E flows or actual Admin-password-login acceptance. Backend
verification remains the unchanged Task 36 baseline, 694 passed / 1 existing
MoneyAudit skip; it was not rerun for these frontend-only changes. Default module
flag remains off; backend authority/feature gating is preserved. No live DB,
actual email, deployment, commit or Stage 8 work. Admin Portal Tasks 42–47 and
final acceptance Tasks 48–53 are still required.

## Final continuation: Tasks 42–53

- Admin module: strict contracts/client/hooks, overview/queue/history and frozen
  detail, assignment/decision dialogs, publication/app history and reasoned
  suspend/restore. Existing login/navigation/refresh paths retained. Server-side
  app filtering precedes count/page; capabilities and live sessions govern actions.
- Security: all 22 cases map to executed tests in the
  [security record](../testing/developer-platform-stage-7-security.md).
- E2E: real Developer BFF and Admin password/live-session login; actual Stage 6
  executor, submission, decisions, inbox and exact-target Production OIDC. Both
  Public and Confidential A–D passed on 2026-10-22, each with five fresh 18-check
  runs. Lost-reply submit replay retained the exact request. Confidential also
  exercised competing Admin dialogs: stale start returned 409 and restored focus.
- Changes/rejection: escaped malicious reasons, fresh drafts/checks/resubmission,
  rejected-target publish denial, and unchanged old review/publication/Production
  display while editing passed.
- Moderation: suspension denies new authorize, held-code exchange and old-token
  UserInfo (plus Confidential introspection); unrelated identity state preserved.
  Restore stays unpublished, explicit republish needs fresh consent and cannot
  revive old tokens. Expired domain and revoked client fail closed.
- Browser: zero Admin runtime issues; no overflow at 320/375/768/1440. Native
  dialog focus, Escape, stale-state recovery and screenshots verified.

The initial 2026-10-22 Public run reproduced a readiness timeout during the second
Sandbox run. `approved.e2e.mjs` now waits for the server coordinator's 18/18
completion before requesting readiness; executor completion alone is insufficient.
`browser-helpers.mjs` includes at most 18 nonpassing table rows in timeout
diagnostics. No application guard or acceptance assertion was removed.

Final browser commands (backend root):

```powershell
node tools/MiniAppReviewE2E/run-fixture.mjs --all-flows
node tools/MiniAppReviewE2E/run-fixture.mjs --all-flows --confidential-partner
```

Both exited 0 with cleanup; logs are
`.codex-tmp/stage7-resume-e2e-{public,confidential}.log`. The unchanged application
baseline remains 695 backend passed / one existing MoneyAudit skip, Developer/BFF
63, Admin 18, SDK 37, host/executor 23, clean Release/model/types/lint/Portal builds.
These regression artifacts were verified from 2026-10-21 and not rerun for the
E2E-only fix. Syntax checks passed for both changed harness modules.

Stage 7 local implementation and acceptance are complete. Keep the feature flag
off until separately approved rollout and deployed TLS/domain/header/SMTP checks.
No application database migration, real mail, deployment, live publication,
automatic commit or Stage 8 work was performed. Existing unrelated edits remain.
