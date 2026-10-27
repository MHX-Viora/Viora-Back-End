# Developer Platform Stage 6 — local acceptance complete

Updated 2026-10-20. Spec/plan approved by user instruction to start implementation.
Canonical [spec](../specs/developer-platform-stage-6.md) and
[32-task plan/evidence](../plans/developer-platform-stage-6.md).

Tasks 1–32 are complete within the approved local web/controlled-fixture scope.
The final [acceptance report](developer-platform-stage-6-acceptance.md) records all
18 categories, 25 security cases, 20 E2E steps and reproduction artifacts. It
supersedes historical pending statements in the checkpoints below. Real
Public/Confidential Portal/BFF/SSO and full readiness pass; cookie/CSP/navigation/
delay failures remain explicit. Public deployment/DNS/TLS/deployed headers/native
runtime remain separate. No application DB migration, commit, publication or
Stage 7 was performed.

## Final Tasks 25–32 handoff

- Protocol correlation uses optional `sandbox_run`, server-private code claims,
  generated internal token IDs and actual validated UserInfo. Missing/stale hints
  cannot authorize requests or create proof; ordinary SSO stays compatible.
- Separate authenticated native-CDP executor signs bounded per-run/check proofs.
  Run-scoped runtime cookies preserve the owner's preview credential. Latest
  additive migration binds executor grants to run/version with restricted FKs;
  retention deletes bound grants before runs and rollback consumes those grants.
- Readiness pins one newest completed run, current configuration/live source,
  matching persisted proof and five-minute freshness. No owner-authenticated
  result-writing endpoint; no readiness flag publishes or approves an app.
- Portal provides selected checks, create/cancel/history/retry, provenance,
  diagnostics pagination/filtering and allowlisted JSON export. Run hint is visible;
  SDK `loginPopup({sandboxRunId})` and Confidential library parameters can bind it.
  Focus restoration waits for React commit. Device switching and responsive
  keyboard launch/logs/reload/stop are exercised in the actual exported Portal.
- Final validation: backend 487 passed / 1 existing MoneyAudit skip; SDK 37,
  host/executor 23, FE/BFF 42; FE types/lint/export, .NET8 Release and EF model pass.
  Real Public/Confidential SSO, cookie restriction, CSP refusal, origin redirect
  and delayed-SDK fixtures pass their positive/negative assertions.
- Provisioning, API/HMAC details, migration precautions and executable commands:
  [operations](../operations/developer-platform-stage-6.md). Detailed evidence and
  limits: [final acceptance](developer-platform-stage-6-acceptance.md).

Implemented: `MiniAppSandboxOptions`/validator, typed server actor and live access
policy, session/grant entities/configuration in shared AppDbContext; additive
`20261017071740_AddMiniAppSandboxSessions`; session lifecycle and snapshot/quotas;
Developer and explicitly allowlisted Admin routes; browser binding + short one-use
grant redemption; host-only HttpOnly runtime cookie, context/heartbeat/close and
dedicated host authority boundary/shell. Sandbox remains opt-in/totally disabled
without configuration. `MiniAppSandbox` options bind at startup; enabled host
hostname differs from Portal/issuer, issuer matches enabled Stage 5 server.

Developer routes `/api/developer/mini-apps/{appId}/sandbox/sessions` POST,
`/{sessionId}` GET/DELETE and `/{sessionId}/launch-grants` POST. Equivalent Admin
routes under `/api/admin/mini-apps/{appId}/sandbox` use signed Bearer Admin role 2,
live account/source session and `MiniAppSandbox:AdminTesterIds`; empty denies all.
Runtime-only `/sandbox-runtime/{prepare,redeem,context,heartbeat,close}` validates
exact configured host and POST Origin, binding/scoped cookie; no management CORS.
No Developer/social token is forwarded to host. Never put the transient grant in
a URL or log. Dedicated host rejects all platform auth/management surfaces.

The host shell `/sandbox-host/{sessionId}` redeems from an exact Portal parent/opener;
`/run` verifies cookie/session and installs per-partner CSP. The host now loads the
registry-sourced partner iframe with bounded permissions, heartbeat and cooperative
bridge timeout. A shell/session/bridge connection never means trusted runtime Passed.
Stop is idempotent; configuration/source account/session/proof changes fail closed.
Sandbox close does not end ANKT/partner sessions or revoke user consent.

Initial Tasks 1–8 verification: 22 focused Sandbox tests; full backend 351 passed/1 existing unrelated
MoneyAudit skip; clean solution build; no EF model drift. Isolated PostgreSQL
migration/rollback/constraints/quota and grant races pass. Nested provider conflict
is deterministic 409 with no retry. Existing source-session/auth regressions pass.
SDK 24 unit tests and build, root FE TypeScript pass. See plan for real Chrome
cookie/CSP/popup/COOP and actual Public/Confidential OIDC feasibility evidence.

Controlled browser setup: backend `dotnet build tools/SsoE2EHost --no-restore`,
then `dotnet run --project tools/SsoE2EHost --no-build --no-restore --no-launch-profile -- --sandbox-feasibility`.
SDK root: `node examples/run-fixture.mjs GENERATED_DIRECTORY`; negative cookie
variant appends `--restrict-partner-cookie`. The generated private file stays out
of logs/source. Open `https://portal.ankt.localhost:5494/fixture/sandbox`; accept
only local certificate exceptions, never change system trust. Fixture user/domain
seeding does not prove public-domain, external social provider or native behavior.
Stop partners then issuer and remove only its verified temporary GUID directory.

## Earlier continuation — 2026-10-19

Reviewed existing uncommitted SDK/host/BFF/Expo work without replacing unrelated
notification/theme changes. SDK v2 is explicit opt-in; v1 remains default. Host
advertises only context/environment/inside/close. External URL and diagnostic
requests remain unsupported until implemented with their later security checks.

Fixed concurrent bridge closes, replay protection surviving navigation, missing own envelope fields, immediate iframe
removal during pending revocation, late initialization after stop, bounded new
handshake waits after navigation and SDK disposal after successful close. Host
close failure permits explicit retry and never claims confirmed revocation.

FE includes the protected integrated Sandbox route, typed client, owner-only BFF
allowlist and nonce/source/origin-bound one-use transport. Vercel now uses
`../viora/vercel.cjs` with the unchanged base configuration relocated to
`../viora/server/developer/vercel-base.json`. Optional build-time
`ANKT_SANDBOX_HOST_ORIGIN` permits only that exact HTTPS host on the Sandbox route;
requires explicit Portal/issuer origins and rejects equal hostnames/unsafe URLs.
Otherwise all Developer routes retain frame-src none. Entry AND exit use document
navigation so the previous page's CSP cannot persist across the route boundary.
See FE handoff for deployment configuration and verification commands.

Executed this continuation: backend full suite 357 passed / 1 pre-existing skip
(28 Sandbox tests in focused run), SDK 28 passed/build, host Node unit suite 12
passed, FE/BFF 33 passed, TypeScript and focused ESLint pass, Expo Web export pass.
Backend full-suite evidence: `.codex-tmp/stage6-continuation-tests.log`.
Host tests use controlled VM DOM/network doubles; they are NOT browser evidence.
Final asset rebuild/focused endpoint verification is recorded in the plan.

Browser acceptance unavailable: Chrome DevTools reports its profile already in
use; Browser runtime discovery returned no browsers. No user browser was stopped.
Do not promote Tasks 9–14 to browser-verified or call Stage 6 complete. Check
effective headers with the actual Vercel routing engine, CSP/iframe/COOP/cookies,
launch/stop/reload/expiry and real Developer login before starting dependent work.
All 25 security cases and the 20-step final E2E remain pending until executed.
No application DB migration, deployment, package publication, commit or Stage 7.

## Persistence, diagnostics and retention — foundation checkpoint

Approved scope continued independently of the unavailable browser. Added shared
run/result/diagnostic/protocol-evidence storage and two additive migrations:
`20261019080250_AddMiniAppSandboxEvidence` and
`20261019081045_AddMiniAppSandboxDiagnosticCounter`. The model binds run/session
fingerprint and diagnostic/session/run, unique check keys and one active run;
bounded enums/times/counters and source restrictions reject untrusted Passed.
Protocol IDs are internal row identifiers only. Actual trusted executor and
protocol-event validation remain Tasks 25–28, not just a source label.

Fixed-code ingestion/query now supports separate SDK/host source stamps, atomic
batch validation, persisted 64/minute and 500/session quotas, and a saturating
dropped counter. Unknown/duplicate nested JSON and credential fields are rejected.
Owner/Admin diagnostic reads require live actor access and owned session binding;
paginated results preserve source/time. v2 diagnostics.report is implemented in
the real host; host handshake reports HostReported separately. Busy diagnostics
retain platform controls. openExternalUrl remains unsupported. BFF exposes GET
diagnostics only with exact filters through both local and Vercel rewritten paths;
typed FE access is ready, but the diagnostics panel remains Task 29.

The enabled-only cleanup worker sweeps after one minute in batches <=100 per
mutation. It revokes expired runtime credentials, consumes ended grants, ends
active runs, removes 7-day ended-session diagnostics and 30-day closed summaries/
sessions in restricted-FK order. Active data, identity, clients and consent remain.
Explicit stop cancels its active run inside the same revocation transaction.

Executed latest verification: full backend **393 passed / 1 existing MoneyAudit
skip**; Sandbox focused run 63 passed before the final added retention-boundary
case (that case plus endpoint/retention regression: 7 passed); real PostgreSQL
migration up/down/up, rollback, active-run and diagnostic quota races, pagination
and retention pass. Host Node suite **15 passed**, FE/BFF **35 passed**, FE
TypeScript/focused ESLint and Expo Web export pass. Solution build has 0 warnings/
errors; EF reports no pending model
changes. Existing ReportReason sentinel design warning is unrelated and preserved.

Evidence: BE `.codex-tmp/stage6-foundation-final-full-tests.log`,
`stage6-foundation-tests.log`, `stage6-final-retention-endpoint-tests.log`,
`stage6-host-tests.log`; FE `.codex-tmp/stage6-foundation-portal-tests.log`,
`stage6-foundation-web-build.log`. Controlled VM tests remain unit evidence, not
browser policy validation. See [operations](../operations/developer-platform-stage-6.md)
for contracts, quotas/retention and schema prerequisites before enabling Sandbox.

Remaining: Tasks 19–32, browser/header/Portal acceptance, actual protocol evidence,
trusted executor/readiness, production SDK popup flow and final security/E2E matrix.
Browser retry still found a busy Chrome DevTools profile and no Browser runtime
instances. No browser was killed. No configured application DB access/migration,
deployment, commit, publication or Stage 7. Existing unrelated work preserved.

## Task 19 — previous checkpoint

Implemented server-owned persisted test-run queue/worker, create/list/get/cancel
APIs for owned Developer and explicitly allowlisted Admin sessions, exact BFF
allowlist and typed FE client. No new schema or migration. Queue capacity is 64
Queued/Running jobs globally, one active run/session; server deadline is 120 seconds
capped by session expiry. Requests choose session/check keys only; unknown,
duplicate, numeric keys and submitted status/results/source are rejected.

Serializable claims atomically change Queued to Running with a new concurrency
version. Every result/completion checks current status/version/deadline and live
session/configuration/source eligibility. Local cancellation interrupts even a
check that ignores cancellation; late results cannot write after cancel/stop.
Session stop commits cancellation before signalling the bounded local registry.
Stop, expiry and restart reconciliation also settle unexecuted checks as
Skipped/MissingEvidence, so terminal runs do not retain a misleading pending code.
Cross-process workers enforce the same persisted guard before writes. Restart
does not replay Running jobs or cancel another instance's live work; abandoned
jobs fail on deadline reconciliation, while persisted queued work survives.

The default executor deliberately returns NotImplemented/MissingEvidence until
Task 20 provides actual configuration checks; protocol/browser capabilities need
their later real evidence. Completed means processing ended, not checks Passed.
No Developer-submitted result, production mutation, fake SSO or readiness shortcut.
Run controls/results UI remains Task 29; typed API operations are now available.

Verification: full backend **413 passed / 1 existing skip**; focused run/session/
HTTP/retention suite **36 passed**, including real PostgreSQL concurrent create/claim,
global queue-cap race and cancel/session-stop versus non-cooperative late check.
Deterministic restart/deadline/ownership/invalid-key cases pass. FE/BFF **37 passed**.
Additional execution guards cover revoked source, stale configuration, suspension
and expired sessions; checks are not invoked after these eligibility changes.
TypeScript, focused ESLint and Expo Web export pass. No model change was needed.
Logs: BE `.codex-tmp/stage6-run-full-tests.log`,
`stage6-run-final-focused-tests.log`; FE `.codex-tmp/stage6-run-portal-tests.log`,
`stage6-run-web-build.log`. See operations for contracts/restart/cancel behavior.

Full-suite load also exposed a shared-connection SQLite fixture collision between
worker and request scopes. A deterministic transaction test reproduced it; the
Sandbox test host now keeps its isolated named-memory DB alive while each scope
owns a separate connection. Worker was not disabled and no failing test removed.
Final full TRX: `.codex-tmp/test-results/stage6-run-full.trx`.

Remaining: Tasks 20–32 and existing real browser/Portal/header/security/E2E
acceptance. No deploy, configured application DB access/migration, commit,
publication or Stage 7; unrelated working-tree edits preserved.

## Task 20 — latest checkpoint

Implemented `MiniAppSandboxConfigurationService` and the real default run checker.
Checks reuse Stage 3 metadata/URL/email validation, current domain proof and Stage 5
`MiniAppSsoService` eligibility/projection/credential checks. Website and legal pages
are fetched through the existing pinned public HTTPS transport. New JSON mode
shares its DNS, TLS, no-redirect, no-credential, byte/time and media limits.
Discovery validates the configured issuer, exact existing endpoints, code/S256/RS256
capabilities and actual public RSA JWKS; it cannot redirect fetching to an endpoint
from untrusted discovery. Duplicate JSON properties, private keys, unusable signing
keys and oversized key sets fail with fixed catalog results. No fetched content is
returned or persisted. Discovery/configuration success is Server evidence only.

New owned/allowlisted-Admin GET `.../{appId}/sandbox/readiness` works before launch,
rejects query input and returns all 18 check keys with applicability, status/code,
source/time, Sandbox environment, current fingerprint, flags and blocking issues.
The ten metadata/legal checks plus explicit environment are represented. Launch
requires public website, current proof, enabled valid Sandbox client and applicable
callback/SSO/discovery gates. Metadata/support/legal additionally gate configuration
and eventual review. Persisted SSO-disabled makes callbacks/client/discovery/protocol
inapplicable; environment still requires the Sandbox preview client. Runtime/bridge
stay NotImplemented/MissingEvidence and readyForReview is false pending Tasks 25–28.
This endpoint preserves legacy Stage 3 readiness and never changes publication.

Actor, fingerprint and expiring proof are rechecked after network I/O; SSO projection
and credential eligibility are re-evaluated. The same checker revalidates live session
before/after each run check, preventing stale fetched success from being persisted.
Valid readiness and session snapshots share one fingerprint implementation.
BFF adds only GET readiness with no query/status/evidence passthrough. Typed FE
`sandboxClient.readiness(appId)` is available; panels remain Task 29. Readiness has
a 30-second evaluation deadline, 35-second BFF timeout and 40-second client timeout.

TDD: missing checker/JSON interface and missing BFF route reproduced first. Final
full backend **444 passed / 1 existing MoneyAudit skip**; FE/BFF **39 passed**;
solution build, TypeScript, focused ESLint and Expo Web export pass. Tests exercise
actual OpenIddict discovery/JWKS, real registry/projection corruption and credential
expiry, pinned controlled HTTPS JSON requests, ownership/Admin boundaries,
SSO applicability, stale/expired snapshot rejection and actual run persistence.
Logs: BE `.codex-tmp/stage6-config-full-tests.log`,
`.codex-tmp/test-results/stage6-config-full.trx`, `stage6-config-build.log`;
FE `.codex-tmp/stage6-config-portal-tests.log`, `stage6-config-types.log`,
`stage6-config-lint.log`, `stage6-config-web-build.log`.

Next: Task 21; Tasks 21–32 and real browser/Portal/header/security/E2E acceptance
remain open. No schema/migration, application DB access, deployment, commit,
publication or Stage 7. Unrelated working-tree changes preserved.

## Deployment compiler compatibility

The Dockerfile builds with SDK 8.0; the local default SDK was 9.0.305. Release
publish with an isolated official SDK 8.0.426 reproduced all four reported CS1003
errors at SsoConsentService:99 and SsoTokenService:35. The SDK 8 parser rejected
the conditional collection expressions in SetDestinations. Both branches now use
explicit `new[] { ... }` arrays, preserving AccessToken/IdentityToken destinations.
Release publish then passed using SDK 8.0.426. `.codex-tmp/` is excluded from the
Docker context, including the isolated SDK and publish/test output.

Evidence: `.codex-tmp/deploy-sdk8-red.log` and `deploy-sdk8-publish.log`.
Full Release regression on SDK 8.0.426: 444 passed / 1 existing MoneyAudit skip;
`.codex-tmp/deploy-sdk8-regression.log` and
`.codex-tmp/test-results/deploy-sdk8-regression.trx`.
For deployment verification, check `dotnet --version` and publish Release using
SDK 8; a successful local SDK 9 build does not establish Docker compiler parity.
Docker CLI is unavailable locally; no Linux container build or redeployment was run.

## Tasks 21–24 — 2026-10-20 checkpoint

Stage 6 remains incomplete. Tasks 21–24 now have implementation and targeted
automated/browser evidence. Next is Task 25 (actual protocol/run correlation),
then Tasks 26–32. Authenticated Developer Portal lifecycle, deployed Vercel CSP,
trusted executor/readiness, final 20-step E2E and security matrix remain open.

Host v2 grants `platform.openExternalUrl` only when its real action is wired.
Host-only POST `/sandbox-runtime/external-links` accepts exactly `{url}` within
4 KiB, rejects queries and reuses exact host Origin/runtime-cookie authorization.
It checks the session partner origin, HTTPS, public DNS, no credentials/query/
fragment, reserved auth destinations and current registered callback paths.
Live source/session/configuration are rechecked after DNS I/O; no arbitrary URL
is fetched. Returned URL/session expiry feed a host-owned modal with textContent,
native keyboard/focus handling, a 15-second maximum and noopener/noreferrer link.
Only a trusted user click can confirm. Reset/dispose/close abort pending dialogs;
one dialog is allowed at a time. v1 permissions stay unchanged. SDK external-link
requests wait 30 seconds to cover authorization plus the bounded confirmation.
DNS validation is an eligibility gate, not control over later browser redirects.

SDK `auth.loginPopup({prompt?,timeoutMs?})` is opt-in and opens synchronously from
the caller's click before awaiting discovery. A known popup source, exact partner
origin, exact callback schema/path and per-attempt state gate the response before
the existing library validates PKCE/nonce/signature and exchanges code. No auth
response/token/verifier crosses the ANKT host bridge. Default top-level login is
unchanged. Callback helper `completePopupRedirect(registeredRedirectUri)` clears
query before relaying directly to the partner opener; unavailable opener returns
false. Blocked/closed/timeout, storage failure, logout/new-login/pagehide and late
completion fail closed. Tokens remain in the SDK instance's memory.

Public example selects the adapter inside an iframe and revalidates UserInfo.
Confidential example binds a completion-only signal to its known popup/random
attempt, exchanges server-side and then calls `/session` from the iframe for
fresh server UserInfo. A top-level cookie alone cannot imply embedded success.
Consent HTML/JSON now derives `environment` from the eligible registered client;
query parameters cannot select the label. Sandbox has a visible test notice;
Production is tested separately. Existing approval/denial behavior is retained.

The disposable HTTPS fixture needed its current integration/SSO service
registrations restored after the intervening Sandbox features. Its new Public SDK
button tests the actual adapter rather than the earlier feasibility prototype.
Executed Chrome DevTools observations on nested Portal → host → partner:

- Public actual SDK popup: first login, consent, code/token/JWKS/UserInfo success;
  forced consent denial returns unauthenticated. Consent shows Sandbox.
- Confidential actual popup: same-origin completion followed by successful
  embedded `/session`/UserInfo and authenticated iframe with SameSite=None.
- Separate SameSite=Lax fixture: popup login succeeds, iframe explicitly reports
  `embedded_session_unavailable`. This is SameSite coverage, not a claim of every
  global third-party-cookie/partitioned-storage policy.
- Host/Portal fixture cookies stay separated. Standalone production external-link
  module UI: confirmation, Escape cancellation/focus return and new tab with both
  opener/referrer absent. UI fixture is not authenticated host/runtime evidence.
- COOP-like closed handles/timeouts/source spoofing/state forgery, pending close,
  navigation cleanup and real signed SDK exchange are covered by focused tests.
  New adapter's real-browser COOP/storage-denial matrix still needs final E2E.

Verification: full backend **459 passed / 1 existing MoneyAudit skip** (including
disposable PostgreSQL migrations/races), SDK **36 passed/build**, host Node **18
passed**, FE/BFF **39 passed**, FE TypeScript and focused SDK ESLint pass. Existing
HTTPS Public/Confidential protocol E2E **1 passed**. No new dependency/schema/
migration, configured DB access, deployment, publication, commit or Stage 7.
Unrelated working-tree edits are preserved.
Full Release solution build with isolated SDK **8.0.426** passes with zero
warnings/errors (`.codex-tmp/stage6-popup-sdk8-build.log`).

Evidence: `.codex-tmp/stage6-popup-full-tests.log`,
`.codex-tmp/test-results/stage6-popup-full.trx`, `stage6-popup-final-focused.log`,
`stage6-popup-sdk.log`, `stage6-popup-host.log`, `stage6-popup-sdk-lint.log`,
`stage6-popup-protocol-e2e.log`, `stage6-popup-positive-browser.txt`,
`stage6-popup-restricted-browser.txt`; FE `.codex-tmp/stage6-popup-portal-tests.log`
and `stage6-popup-types.log`. Logs are local artifacts, not readiness authority.
