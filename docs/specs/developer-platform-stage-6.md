# ANKT Developer Platform — Stage 6 specification

Status: APPROVED by the user's instruction to begin implementation, 2026-10-17.
Scope: supplied Stage 6 requirements I–XXVII; the four proposed architecture/policy
choices below are approved. Read with [the task plan](../plans/developer-platform-stage-6.md).

Implementation/local acceptance completed 2026-10-20; final scope, executed evidence
and deployment/browser limits are in the [acceptance report](../handoffs/developer-platform-stage-6-acceptance.md).

## Objective

An active owner opens a real registered partner website in an isolated web host,
tests the Stage 5 Sandbox OIDC flow with a separately authenticated ANKT user,
inspects bounded diagnostics and receives honest technical readiness results.
Preview is a real iframe, not a mock. Production publication/review is unaffected.

Proposed assumptions for approval: web-only runtime; a distinct first-party host
origin; no organization/team feature invented; Admin testing requires both existing
Admin authentication and an explicit testing allowlist. Browser-reported events
are observations, not independent proof. Public-domain and native validation
remain separate from controlled fixture evidence.

## Pre-flight audit and dependency gate

Audited BE commit `d57276ba`, FE commit `4c8a62f`. Stage 5 spec, complete 28-task
plan, handoff, security matrix and operations guide were read. Source and tests
confirm implemented dependencies; the current deployed application was not probed.

| Required audit | Evidence / outcome |
| --- | --- |
| 1. Stage 5 specification | `docs/specs/developer-platform-stage-5.md`: one OpenIddict server, registry authority, independent auth contexts. |
| 2. Stage 5 28-task plan | `docs/plans/developer-platform-stage-5.md`: all tasks completed, final evidence retained. |
| 3. Acceptance handoff | `docs/handoffs/developer-platform-stage-5.md`: 18-category report; local acceptance, public deployment/native limits explicit. |
| 4. OpenIddict runtime | `viora-BE/Sso/SsoRegistration.cs`: opt-in 7.7.2, EF stores, S256, opaque access tokens, independent RSA keys; live isolated host started. |
| 5. Authorization/token | Real public SDK and confidential backend exchanges passed again, including consent/reconsent, revocation and session lifecycle. |
| 6. Discovery | Library discovery consumed by both real clients; actual configured endpoints, HTTPS issuer. |
| 7. JWKS | Actual signed ID-token validation in E2E; overlap/expiry/signature regressions in backend/SDK suites. |
| 8. Developer Auth | `DeveloperAuthentication.cs`, existing BFF cookie/CSRF; DeveloperActive derives developer ID/session from server authentication. |
| 9. Client Registry | `MiniAppSsoService`, `MiniAppOidcRegistry`, encrypted current registry credentials and transactional library projection reused. |
| 10. Environment isolation | `SsoClientPolicy.Resource`: `ankt-miniapp-sso:Sandbox` / `Production`; client, callback, consent, token and subject checks covered. |
| 11. SDK | `../viora/packages/miniapp-sdk`; 23 tests/declaration build passed. Current login explicitly refuses iframe navigation: needs a planned adapter. |
| 12. Bridge harness | `tools/SsoBridgeHost` and SDK bridge v1; verified Stage 5 harness and spoofing tests. Only getContext/close exist; not the Stage 6 host. |
| 13. Expo Web | Existing `features/developer`, catch-all routes and BFF retained. Portal CSP currently `frame-src 'none'`; route policy must be deliberately scoped. |
| 14. Mini App APIs | Existing owner-bound CRUD/integration/SSO/domain/readiness. No Sandbox session/run/runtime APIs exist. |
| 15. Domain verification | Expiring/revocable app-bound proof, URL canonicalization and pinned public-network fetcher reused; proof checked against current website host. |

Executed pre-flight: backend 329 passed/1 existing MoneyAudit DB skip; Developer/BFF
27 + first-party bootstrap 7 passed; SDK 23 passed; isolated real HTTPS E2E 1
passed. SsoE2EHost build: 0 warnings/errors. Backend TRX:
`.codex-tmp/test-results/stage6-preflight.trx`. The E2E uses disposable certificates,
seeded verified domains, a clearly marked fixture user and real protocol handlers;
it is not public-domain, external-provider or native evidence. Prior Stage 5 Chrome
evidence remains historical; no new Stage 6 browser runtime has been tested.

Conclusion: Stage 5 implementation dependencies pass local re-verification. Stage
6 implementation is authorized by the user's subsequent approval; no OAuth
server replacement is needed.
Public enablement additionally needs real DNS/TLS, issuer, key provisioning and
approved migrations; no deployment is implied by this gate.

## Architecture

Keep the existing .NET API, Domain/Application/Infrastructure boundaries, EF
AppDbContext, OpenIddict, Expo portal and server BFF. Add a Sandbox session service,
test coordinator, diagnostics/readiness and a small host document/script served
by the existing backend on a dedicated configured HTTPS hostname. This is another
origin of the same platform, not another OAuth server, Portal or Next.js project.

Portal `/developers/console/apps/{appId}/sandbox` → existing BFF → owner-bound
Sandbox API → session service → isolated first-party host → partner iframe.
The requested `/console/apps/{appId}/sandbox` is the logical path within the
existing `/developers` mount; no second root Console is created.

Three browser trust boundaries: Portal origin, dedicated host origin and verified
partner origin. Host must differ from Portal, API/issuer and partner origins;
production host also uses a separate hostname for cookie isolation. Prefer the
same schemeful site as Portal so host cookies are usable in its preview iframe.
An unrelated-site host may fail under third-party-cookie restrictions; fail closed
and offer a separately authorized top-level host launch, never relax auth.

The host uses only its scoped Sandbox cookie, never Developer/social credentials.
Runtime routes require the configured host authority and trusted proxy handling;
they cannot be mounted as an arbitrary Host-header alias of management APIs.
Existing auth/management endpoints are unavailable on the dedicated host surface.
No partner assets are reverse-proxied. Test loopback exceptions belong exclusively
to fixtures with independent data/certificates, never production URL validators.

### Launch, expiry and revocation

1. BFF creates session from authenticated owner and current app/client/proof versions.
2. Empty host shell prepares a random browser nonce and a protected HttpOnly
   host-only binding cookie, then sends `host.ready` to its exact Portal parent.
3. Portal validates source/origin and requests a launch grant via CSRF-protected
   BFF, binding the session, nonce, host audience and source Developer session.
4. Portal sends grant in memory by exact-origin postMessage to that host only.
   Host redeems in HTTPS POST; cookie/nonce/audience/session must all match.
5. Database consumes the hashed grant once and issues a random scoped HttpOnly
   `__Host-AnktMiniAppSandbox` cookie; only its hash is stored. Load the frozen
   verified partner URL after successful redemption. No grant/cookie in URLs.
6. Runtime requests/heartbeats recheck expiry/revocation, active source Developer
   session, owner/app/client/proof and configuration snapshot. Changes invalidate
   the session; no silent environment/callback fallback.
7. Stop/close revokes Sandbox session and pending grants, cancels test work and
   disposes iframe/listeners. It does not log out ANKT, revoke user consent or
   delete an independent partner session. UI teardown occurs immediately; backend
   revocation retries are bounded and visible if offline, with expiry as fallback.

Proposed defaults: absolute session lifetime 15 minutes (configurable maximum 30),
grant 30 seconds, one active session per actor/app and at most three per actor,
heartbeat 15 seconds, no sliding expiry. Anonymous prepare is size/rate bounded
and reveals no app details. Session IDs and test-run IDs are non-authenticating
identifiers; knowing either never grants access. Grant issuance never returns a
Client Secret. Concurrent redeem has exactly one winner on PostgreSQL.

## Security Model

- Owner-only DeveloperActive management, owner derived server-side, app/session/run
  IDs checked together; deny another owner's existence uniformly. No client-supplied
  DeveloperAccountId/environment/status authority.
- Admin testing uses separate `/api/admin/mini-apps/{appId}/sandbox/...` routes,
  existing social Admin role `2`, live account/session and explicit configured
  tester IDs; empty allowlist denies all. Audit actual Admin actor separately from
  app owner. No Developer impersonation or new Admin publishing/review permissions.
- No organization/team membership model was found in the audited Developer domain;
  team access is unsupported until a separately approved membership policy exists.
- Runtime permission is distinct from user SSO. Sandbox client must be explicitly
  eligible; Production credentials/resources/consent never authorize Sandbox runs.
  Non-SSO apps may preview with valid Sandbox client/proof without enabling SSO.
- Rate limits per actor/session and unauthenticated remote IP, body bounds, no-store,
  no-referrer, safe structured errors and redacted audit events on all surfaces.
- Expiring/revoked proof, disabled actor, suspended/deleted/rejected app, stale
  config or revoked client fails closed on both launch and later runtime access.

### Iframe, navigation and browser limits

Server supplies the exact current HTTPS website URL; no arbitrary iframe URL field.
Reject credentials, privileged platform origins, private/special-use targets and
unverified domain substitutions. Backend probes use existing public-network pinned
transport with bounded bodies/timeouts/redirect validation, never a generic proxy.

Portal Sandbox route permits only the host origin in frame-src; other routes keep
their policy. Replace overlapping policies rather than adding a weaker CSP header
that still intersects with existing frame-src none. Host frame-src permits only the
verified partner origin. Partner frame-ancestors must permit every ancestor,
including Portal and host; do not defeat it or X-Frame-Options.

Partner iframe grants scripts + same-origin only for a partner origin distinct
from every privileged origin. Forms are enabled where needed. No top navigation,
downloads, camera, microphone, geolocation, payment or arbitrary device APIs.
Nested host iframe permissions must be equally bounded. no-referrer on frames;
both host and browser response CSP/Permissions-Policy constrain capabilities.

Cross-origin DOM, arbitrary same-origin partner paths, full network traffic and
script behavior cannot be monitored by the host. Parent frame-src constrains
document origins; it is not a network firewall for third-party scripts. Reload or
observed navigation invalidates the old bridge connection until a fresh handshake.
Unobservable internal navigation remains Unknown; never claim full interception.

Iframe load/onerror is not reliable proof of success or CSP failure. Use safe
server header observations plus bounded cooperative SDK handshake. When exact cause
is unobservable show an unresolved connection/embedding error with CSP/network
guidance. Only an observed policy block is labelled confirmed; no load event alone
marks Runtime Passed. Pages without SDK may be viewed but cannot satisfy the
cooperative runtime/bridge readiness check automatically.

### Bridge and external links

Reuse the Stage 5 protocol/client infrastructure. Preserve v1 getContext/close
behavior; add explicitly negotiated v2 schemas/capabilities for getEnvironment,
isInsideAnkt, close, openExternalUrl and bounded SDK diagnostic observations.
isInsideAnkt is true only after verified handshake; environment always Sandbox.
Validate exact source, origin, channel, version, random request ID, schema/length,
method permissions, pending cap and timeout. Unknown method: UnsupportedMethod;
malformed messages do not execute actions. Cleanup on close/reload/pagehide.

openExternalUrl accepts only validated public HTTPS origins from the session's
explicit verified allowlist (initially the partner origin), no credentials,
query/fragment or platform callback/auth URLs. Require host UI confirmation/user
gesture; open with noopener/noreferrer. URL changes require backend revalidation
and a new session. Never use a Mini App-supplied close/back destination.

## SSO integration and consent

Keep authorization/token/discovery/JWKS/UserInfo in Stage 5. No mock SSO and no
Developer-to-user session conversion. Add a visible Sandbox label to consent using
server-resolved client environment; query parameters cannot choose the label.
Maintain first/repeat/forced/deny consent and stable app/environment pairwise subject.

Proposed embedded Public flow requires explicit approval of a user-gesture popup
adapter: iframe SDK owns state/nonce/PKCE transaction; authorization occurs in a
top-level popup; registered partner callback relays its authorization response
directly to the same-origin partner opener, never through the ANKT host bridge.
Check exact callback origin, known popup source, per-attempt binding, state and
timeout before existing library code/signature validation. Remove callback query
immediately. No access/refresh/social token traverses either bridge. No verifier
is sent by the callback. Popup adapter is opt-in; current top-level SDK behavior
and the Stage 5 API remain compatible.

Confidential callback performs server-side exchange and creates its own cookie;
only a same-origin completion signal returns. Its backend keeps the secret. The
iframe must revalidate actual partner session/UserInfo before declaring success.
Third-party cookies, storage partitioning, popup blocking and COOP may prevent this;
record Warning/Skipped/Failed with a top-level partner test option, never pretend
an iframe session exists because top-level login worked.

Popup support needs narrowly enabled allow-popups/allow-popups-to-escape-sandbox
for SSO-capable sessions. These flags cannot restrict every popup opened by partner
scripts to one origin; openExternalUrl validation does not solve that browser limit.
This tradeoff is an explicit approval question below. If it is rejected, revise the
embedded SSO approach before coding; do not silently ship top-level-only Public
SSO as completion of the requested embedded flow. Never iframe the consent page.

## Database schema and retention

Add entities/configuration to shared AppDbContext, no second database. Proposed
tables: `MiniAppSandboxSessions` (actor/owner/app/source session, Sandbox-only,
Created/Expires/Revoked/LastActivity, configuration fingerprint, runtime credential
hash and concurrency version); `MiniAppSandboxLaunchGrants` (session, hash, nonce
binding hash, audience, expiry/consumed/version); `MiniAppSandboxTestRuns` (session,
fingerprint, Started/Completed, status and bounded summary); `...TestResults`
(run, unique TestKey, status, catalog MessageCode, bounded DurationMs/Created);
`MiniAppSandboxDiagnostics` (app/session/run, level/category/code/provenance/time).
Keep DeveloperAccountId as app owner; add actor type and optional Admin account
reference instead of putting an Admin ID in a Developer foreign key.

Use restricted FKs, unique hash/run-key indexes, Sandbox check constraints, expiry
indexes and optimistic concurrency. No raw launch grant, credential, code, token,
verifier, cookie, password, arbitrary HTML/stack trace or partner URL query stored.
Existing MiniAppAuditLog records owner actions; Admin/runtime records retain the
correct actor in bounded Sandbox audit metadata. No fake owner identity for Admin.

Diagnostics accept allowlisted codes/typed non-sensitive counters, not arbitrary
console text. Server maps codes to messages/remediation; unknown fields rejected.
Proposed bounds: 32 KiB request, 20 events/batch, 64 events/minute/session, 500
events/session; pagination max 100; diagnostics retained 7 days, run summaries and
closed sessions 30 days. Purge in bounded batches, preserve active rows, do not
delete identity/OIDC/consent history. Dropped-event counters remain bounded.
Migration is a separate additive task, SQL reviewed and up/down/up/rollback/races
tested on disposable PostgreSQL. Never migrate the configured application DB.

## API contracts

Management base `/api/developer/mini-apps/{appId}/sandbox`; all use existing
DeveloperActive + actor-bound rate limits/audit. BFF mirrors only explicit routes.

| Method/path | Result / boundary |
| --- | --- |
| POST `/sessions` | 201 metadata, current versions; no secret or user token. |
| GET `/sessions/{sessionId}` | Owned metadata/liveness and bounded activity. |
| DELETE `/sessions/{sessionId}` | Idempotent revoke; stops pending grants/runs. |
| POST `/sessions/{sessionId}/launch-grants` | Body host nonce; one-use host-bound launch grant, no-store response. |
| POST `/test-runs` | 202 run ID; caller selects supported check keys/session only, never status/result. |
| GET `/test-runs`, `/{runId}` | Paginated owned runs/details; provenance and snapshot. |
| DELETE `/test-runs/{runId}` | Idempotent owned cancellation; terminal runs stay terminal; no login/consent revocation. |
| GET `/diagnostics` | Bounded owned session/category/cursor query. |
| GET `/readiness` | readyForSandbox, readyForReview, checks, blockingIssues, warnings, evaluatedAt/fingerprint. |

Additional host-only `/sandbox-runtime` prepare/redeem/context/heartbeat/observations
and close routes use exact host Origin, binding/scoped cookie and browser CSRF
protection, not permissive management CORS. Launch grant is redeemed only here.
Admin route equivalents reuse services with an explicit typed actor and reduced
test permissions; no Admin UI rewrite. Internal trusted-runner evidence submission
is separately authenticated and never exposed through Developer BFF or bridge.
Error envelope follows existing `{error:{code,message}}`: 400 validation, 401 auth,
403 denied, uniform 404 ownership, 409 stale/conflict, 410 expired/revoked session,
413 oversize, 429 rate limit. No automatic transaction retry for one-use proofs.

## Diagnostics, test runner and readiness

Categories Runtime/SSO/Bridge/Configuration/Network/Permission. Every result shows
Passed/Failed/Warning/Skipped/NotImplemented plus evidence source/time. Persist
queued/running/completed/failed/cancelled run transitions. A bounded hosted worker
reuses current services; no unbounded Task.Run or arbitrary developer code executed
server-side. Only one active run/session; bound queue, deadline and cancellation.

Server checks: metadata, proof/HTTPS, legal/support fields, explicit environment,
callbacks and real discovery/JWKS. A config match never proves consent/token/
UserInfo. Correlate Sandbox protocol tests to an active server-issued run using a
non-authenticating run ID; actual Stage 5 server handlers recheck client/environment
and record internal authorization/token IDs and fixed outcome codes, not credentials.
Record UserInfo only after actual validation, with matching run/token provenance.
Keep existing OIDC behavior unaffected when no diagnostic run is present.

SDK/host observations (initialization, render signal, handshake, client errors) are
labelled browser-reported; a developer can tamper with their browser. Independent
runtime test Passed requires a configured trusted browser test executor with
authenticated evidence bound to run/session/fingerprint/check/expiry and replay
protection. Implement its contract and controlled real-browser executor as a
separate slice; without it, report observation/Skipped and block readyForReview
where proof is required. Never expose an API for Developer to assert Passed.

readyForSandbox uses current non-runtime launch prerequisites; readyForReview adds
legal/support metadata and current successful required runtime/bridge/SSO evidence.
SSO/bridge checks may be inapplicable only when that capability is disabled in the
server-owned snapshot. Failed/missing/stale required evidence blocks; warnings are
explicit. No readiness field sets app Published or substitutes for Admin review.

## Developer Portal UI

Reuse ConsoleGuard/session/layout/API/theme; add Sandbox navigation from app detail.
Header logo/name/App ID/Sandbox status/back; toolbar launch/reload/stop/device/SSO/
logs/config; prominent real preview with compact host header/close; accessible
diagnostics and technical checklist; footer expiry/connection/environment.
Device presets 375×812 and 768×1024 plus responsive desktop; fit/scroll controls
on small screens without claiming device-native behavior. Keyboard focus returns
to launch after closing; status announcements, empty/loading/retry/conflict/error/
timeout/expired/revoked/blocked-policy states; no raw database table UI or popup spam.
Show observed versus independently verified results in normal product language.

## Tech Stack, Project Structure and Code Style

.NET 8 / EF Core 8 / Npgsql, existing OpenIddict 7.7.2 and xUnit fixtures;
Expo/React Native Web + TypeScript + existing BFF; private SDK/openid-client 6.8.8.
Use existing components and test tools; any browser-executor dependency must be
proposed/pinned during its task, not silently installed or deployed.

- `Viora.Domain/Developers/`: Sandbox entities; `Viora.Application/Developers/` DTOs.
- `Viora.Infrastructure/Developers/`: access/session/run/readiness/diagnostic services.
- `Viora.Infrastructure/Persistence/Configurations/` and `Migrations/`: shared schema.
- `viora-BE/Developer/`: management/Admin/runtime routes; host assets in existing API.
- `Viora.Application.Tests/`: focused HTTP/service/PostgreSQL/security tests.
- `../viora/features/developer/`: Sandbox components/client/CSS/tests and route map.
- `../viora/server/developer/`: explicit BFF allowlist; `packages/miniapp-sdk/`: SDK.
- Existing `tools/SsoE2EHost` / test tools: controlled fixture extensions only.

Thin endpoints, explicit schemes/actor, DTO isolation, boundary validation,
cancellation and transaction boundaries. Example proposed public DTO:
`public sealed record SandboxSessionDto(Guid Id, string AppId, string Environment,
DateTime ExpiresAt, DateTime? RevokedAt);`. Use enums/catalog codes internally;
stable serialized contracts, immutable server-owned identity and typed TS errors.

## Commands and Testing Strategy

Existing commands (BE root unless stated):

```powershell
dotnet build viora-BE.sln --no-restore --verbosity quiet -m:1 -p:UseSharedCompilation=false
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-build --no-restore --verbosity quiet -m:1
dotnet build tools/SsoE2EHost/SsoE2EHost.csproj --no-restore --verbosity quiet -m:1 -p:UseSharedCompilation=false
dotnet run --project tools/SsoE2EHost --no-build --no-restore --no-launch-profile
```

FE root: `npm run test:developer`,
`node --import tsx --test features/sso/bootstrap.test.ts`, `npx tsc --noEmit`,
`npm run build:web`. SDK root: `npm test`, `npm run example:fixture -- GENERATED_DIR`,
then `npm run test:sso:e2e` with the generated root CA as documented in Stage 5.
Stage 6 focused suites/executor scripts do not exist yet; add and record exact
executable commands during implementation. Do not report proposed scripts as run.

TDD each slice; deterministic clock/expiry, actual DB concurrency/one-use races;
browser tests for nesting/CSP/permissions/cookies/popups/COOP/cleanup and UI states.
Map all 25 requested security cases to executed tests/explicit limits in the plan.
Complete the requested 20-step real fixture E2E from Developer login to revoked
Sandbox session, proving Production unchanged. Test both public/confidential SSO,
first/repeat/denied consent, iframe-blocked and browser-restriction failure cases.
Viewport simulation is not Android/iOS WebView validation.

## Boundaries and Acceptance Criteria

Always preserve existing social/Admin/Developer auth and unrelated edits; small
slices, test first, review real browser behavior and document failed/skipped checks.
Ask for this spec/plan approval before code; renew review for substantial changes
to origins, popup/cookie trust, trusted-runner authority or public contracts.
Routine approved additive schema/tasks need no repeated approval; production
migration, publishing, deploy, payment/wallet/notifications/device permissions,
native runtime, Admin Review/directory and Stage 7 are excluded.

Acceptance: active authorized actor can launch/stop/reload real verified partner;
single-use grants and expiring/revocable sessions isolate actors/apps/environments;
bridge implements only real capabilities; real Sandbox OIDC/consent/UserInfo and
separate partner sessions work under documented supported browser settings;
diagnostics/retention/readiness never leak secrets or turn observations into proof;
25 security cases, UI states and 20-step E2E have truthful executed evidence;
PostgreSQL migrations/races pass; builds/types/regressions pass; final handoff
reports all 18 user categories and Stage 7 prerequisites without starting Stage 7.

## Approved Architecture Choices (2026-10-17)

1. Approve a dedicated same-site HTTPS host hostname served by the current backend?
   Exact deployment DNS/TLS may remain operator configuration; never use Portal or
   issuer origin for partner execution. Local fixture DNS/certificate isolation
   must be verified before claiming production-equivalent cookie isolation.
2. Approve the opt-in popup SSO adapter and its unavoidable popup-permission tradeoff,
   with explicit blocked/partitioned-cookie outcomes? If all partner-created popups
   must be forbidden, embedded Public SSO needs a revised architecture first.
3. Approve owner-only Developer access, explicit Admin role+tester allowlist and no
   invented team model, plus the proposed expiry/quota/retention defaults?
4. Approve separately authenticated trusted-browser evidence for review readiness?
   With no executor provisioned, required runtime proof remains Skipped/blocking;
   browser/SDK events alone will never assert independent Passed.

References checked 2026-10-17: [iframe sandbox and browser limits](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/iframe),
[postMessage origin/source validation](https://developer.mozilla.org/en-US/docs/Web/API/Window/postMessage),
[frame-ancestors and ancestor checks](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/frame-ancestors).
