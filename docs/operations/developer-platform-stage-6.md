# Stage 6 Sandbox operations — implementation checkpoint

Stage 6 is incomplete. Tasks 15–20 provide persistence/diagnostics/retention,
test-run coordination and configuration readiness; Tasks 21–32 and final real browser/Portal acceptance remain pending. Canonical
[plan](../plans/developer-platform-stage-6.md), [spec](../specs/developer-platform-stage-6.md),
[handoff](../handoffs/developer-platform-stage-6.md).

## Before enabling

Sandbox stays off by default (`MiniAppSandbox:Enabled=false`). Operator-approved
migration/deployment is separate work. This checkpoint migrated only disposable
PostgreSQL fixtures, never a configured application DB.

The enabled backend requires existing Stage 5 schema plus:

- `20261017071740_AddMiniAppSandboxSessions`
- `20261019080250_AddMiniAppSandboxEvidence`
- `20261019081045_AddMiniAppSandboxDiagnosticCounter`

Review generated SQL and back up the intended database before a separately
authorized rollout. Down migrations delete only their Sandbox objects/data; that
still discards Sandbox history. No automatic migration is performed by the worker.
EF design validation can use `VIORA_EF_DESIGN_CONNECTION` pointing to a synthetic
unreachable database; it must not use application credentials for offline checks.

Configure distinct HTTPS Portal/host/issuer authorities with cookie-isolating
hostnames, consistent with Stage 5 issuer. Runtime/management/source-session and
verified-domain eligibility remain live checks. Admin access requires signed Admin
role and an explicit `MiniAppSandbox:AdminTesterIds` allowlist. Empty denies all.
FE `ANKT_SANDBOX_HOST_ORIGIN` must match BE; see FE handoff for scoped Vercel CSP.
Browser routing/CSP/cookie/COOP acceptance must still be executed before rollout.

## Diagnostic contracts

Owned Developer GET:

`/api/developer/mini-apps/{appId}/sandbox/diagnostics?sessionId={guid}&category=Bridge&limit=50`

Admin equivalent uses `/api/admin/.../sandbox/diagnostics`, existing Admin auth and
tester allowlist. Historical reads require a currently authorized actor and an
owned session; another actor/session does not inherit access. Categories are
Runtime, SSO, Bridge, Configuration, Network, Permission. Limit 1–100, default 50.
`nextCursor` is a 32-hex diagnostic row identifier, scoped by session/category;
it grants no authority. A removed/mismatched cursor returns 400; restart pagination.
Response contains fixed-code items, source/time, `nextCursor`, `droppedCount`.

FE/BFF permits GET only; exact validated query fields survive both local and
Vercel rewrite paths. Unknown/duplicate fields, invalid session/category/cursor or
limit return 400. BFF never proxies runtime, Admin or trusted evidence submissions.

Host-only POST `/sandbox-runtime/sdk-observations` and `/host-observations` require
the host runtime HttpOnly cookie and exact host Origin. Body:

```json
{"events":[{"category":"Bridge","code":"BRIDGE_READY","count":1}]}
```

Optional `testRunId` must match the same live session/snapshot and an active run
before its deadline. Each route stamps its source on the server. Requests cannot
choose actor, owner, source, level, result status, message or credential fields.
No raw console output, exception/stack, URL, authorization code/token or HTML is
stored. Catalog: SDK_INITIALIZED, RUNTIME_READY/ERROR, SSO_SUCCEEDED/FAILED,
BRIDGE_READY/ERROR, NETWORK_ERROR, PERMISSION_DENIED, with their matching category.
`count` is optional 0–1,000,000. SDK v2 forwards only catalog-shaped requests;
the host reports its own successful handshake separately, once per navigation.

Bounds: 32 KiB JSON, depth 4, 1–20 events/batch, 64 events/minute/session,
500 events/session. Unknown fields and duplicate property names at every nesting
level are rejected. Invalid/over-quota batches persist no partial events. 429
persists a saturating dropped-event counter (max 1,000,000); 409 indicates a
serialization conflict and is not automatically retried. SDK maps quota/conflict
to BRIDGE_BUSY while retaining platform control permissions. Host/SDK observations
are explicitly browser-reported, including SSO_SUCCEEDED, never trusted proof.

## Retention and cancellation

Enabled worker sweeps once per minute, first sweep delayed one minute. Disabled
worker performs no cleanup. Each mutation selects at most 100 rows and commits in
a serializable transaction; later sweeps continue remaining dependent batches.
Provider conflicts/cancellation roll back; safe warning logs do not include row
content, SQL parameters or raw exception details.

- Expired sessions lose runtime credentials; expired/ended grants are consumed.
- Deadline/session-ended active runs transition to Failed/Cancelled. Explicit
  session stop cancels its active run atomically with revocation.
- Ended-session diagnostics age out at 7 days; active session rows are preserved.
- Closed run evidence/results/summaries and ended sessions age out at 30 days.
  Dependents purge first; bounded batches may require multiple sweeps.
- Identity, OAuth clients, consent and Production state are never cleanup targets.

No trusted browser executor, protocol observation hooks or readiness aggregation
is implemented yet. Enum/source constraints protect storage integrity
but do not authenticate a trusted runner. No external endpoint accepts Passed.
The Portal diagnostics/results panel is still Task 29; typed API access now exists.

## Test-run controls

Owned base `/api/developer/mini-apps/{appId}/sandbox/test-runs`; Admin equivalent
uses existing Admin role/tester allowlist and its own actor-bound sessions.

- POST base: `{"sessionId":"GUID","checks":["domain","runtime"]}` returns 202
  summary and Location. Omit checks for the fixed catalog; no status/result/source
  fields accepted. Unknown/duplicate/numeric keys fail 400. Backend body max 4 KiB;
  BFF retains its stricter 1 KiB Sandbox request limit (all 18 keys fit).
- GET base: required sessionId, optional 32-hex cursor and limit 1–100 (default20).
- GET `/{runId}`: owned summary plus at most 18 check results and provenance/time.
- DELETE `/{runId}`: idempotent cancellation, 204. Completed/Failed/Cancelled stay
  terminal; cancellation does not revoke ANKT login, partner login or consent.

BFF permits these four operations only, not result/executor submissions. Typed
FE createRun/runs/run/cancelRun operations exist; controls/results UI is pending.

Check keys: logo, name, category, description, website, domain, supportEmail,
callbacks, privacyPolicy, termsOfService, environment, ssoClient, discovery,
runtime, bridge, consent, token, userInfo. Queue capacity is 64 Queued/Running jobs
globally and one active job/session; 429 is full queue, 409 is active run/conflict.
Deadline is server-selected 120 seconds capped by session expiry. Workers claim
and complete against a concurrency version; cancel/stop/expiry invalidate it.
Unexecuted checks settle as Skipped/MissingEvidence when their run ends; this
also repairs terminal pending rows in bounded retention sweeps.

Worker starts after one second and processes one scoped run at a time; no
unbounded in-memory work queue. Local cancellation interrupts waiting execution;
another process enforces persisted status/version/deadline before writing. Running
jobs are not automatically replayed after restart; abandoned jobs become Failed
on deadline reconciliation by the run worker or retention sweep. Queued jobs
persist. No cross-instance startup cancellation or fabricated successful recovery.

Default executor now evaluates real configuration checks using the same service
as readiness below. Runtime/bridge and applicable protocol checks still record
NotImplemented/MissingEvidence. Completed means processing finished, not checks
passed. A disabled persisted SSO capability returns Skipped/CheckNotApplicable for
callbacks/client/discovery/consent/token/UserInfo. Preview still requires its own
enabled valid Sandbox client independently of whether partner login is enabled.

## Configuration readiness

GET `/api/developer/mini-apps/{appId}/sandbox/readiness` or the equivalent explicitly
allowlisted Admin route. No query/body/environment selector or supplied evidence.
An active Sandbox session is not required. Existing ownership/source/lifecycle
checks apply; responses are no-store. Legacy `/mini-apps/{appId}/readiness` remains
unchanged. BFF exposes only the new GET operation, without Admin/runtime routes.

Response: environment, configurationFingerprint, configurationReady,
readyForSandbox, readyForReview, evaluatedAt, checks, blockingIssues, warnings.
All 18 keys above have applicable/status/messageCode/evidenceSource/evidenceAt.
The first ten metadata/legal keys plus environment preserve all 11 configuration
requirements. Extra SSO client/discovery gates reuse Stage 5. Blockers are key names;
raw fetched pages, JSON, credentials and exception details never appear in results.

Launch gates: reachable public HTTPS website, current matching verified domain,
enabled valid Sandbox client/isolated partner origin and applicable valid callbacks,
SSO projection/credential eligibility and actual discovery/JWKS. Metadata/support/
legal fields additionally gate configuration and eventual review. Discovery requires
the configured issuer, current server endpoints, code/S256/RS256 and public usable
RSA signing keys. DNS-pinned credential-free fetch rejects redirects, private IPs,
invalid TLS/media, compressed or oversized responses. JSON max 64 KiB/depth 8,
JWKS max 16 keys; duplicate properties/private key material fail. It never fetches
an arbitrary URL from discovery. Metadata/legal fetches retain the 256 KiB cap.

Readiness rechecks actor/configuration/proof after I/O and current SSO eligibility;
snapshot/proof changes return 409 and stale success cannot be committed by a run.
Evaluation deadline is 30 seconds (504 SANDBOX_CHECK_TIMEOUT); scoped BFF/client
timeouts are 35/40 seconds. Public HTTPS fetch retains its 8-second deadline.
readyForReview stays false until independently verified fresh runtime/bridge and
applicable protocol evidence are implemented and aggregated in Tasks 25–28.
Neither ready flag changes publication or replaces Admin approval.

## Reproduce local checks

From backend root (PostgreSQL fixtures create their own disposable clusters):

```powershell
dotnet build viora-BE.sln --no-restore -m:1 -nodeReuse:false --verbosity quiet
dotnet test Viora.Application.Tests --no-restore --filter FullyQualifiedName~MiniAppSandbox -m:1 -nodeReuse:false --verbosity quiet
node --test --experimental-test-isolation=none tools/SandboxHostTests/*.test.mjs
```

From FE root:

```powershell
node --import tsx --test --experimental-test-isolation=none server/developer/*.test.cjs features/developer/tests/*.test.ts
npx tsc --noEmit
npm run build:web
```

Windows sandbox may block child-process spawning; use an authorized test context
for MSBuild/tsx/Metro and disposable PostgreSQL. Do not replace fixture connection
strings with application database configuration. These checks do not validate real
browser policies, native behavior, public DNS or deployed Vercel routing.
