# ANKT Developer Platform — Stage 5 plan

Status: conditionally approved 2026-10-16; all six conditions incorporated.
Spec: `../specs/developer-platform-stage-5.md`.

Current execution checkpoint: Tasks 1–28 complete in the controlled local environment.
Public-domain deployment and native runtime remain separate release prerequisites.

Tasks run sequentially in small slices, normally no more than five authored files.
Generated migration/designer/snapshot changes form their own reviewable task.
Each task begins with focused failing behavior tests and ends with build/tests.
Implementation stays sequential. Independent read-only adversarial review follows
the applied doubt-driven-development skill; no parallel implementation streams.
No deployment, production migration or Stage 6 work.

Execution conditions: registry credentials are authoritative, fast SSO reuses valid
user session and consent, environments remain isolated throughout authorization,
public clients have no secret, protocol checks use standard libraries, and SDK
publication is excluded. PostgreSQL migration/transaction/concurrency checks are
required on an isolated test database; absence of one blocks the affected task's
completion rather than permitting a SQLite-only success claim.

Task 1 is a disabled-by-default registration increment. Enabling it validates HTTPS
issuer, bounded lifetimes and externally provided independent RSA certificates.
It does not expose a completed authorization flow before stores/handlers exist.
Serious architecture changes require renewed review; routine planned work is approved.

| # | Slice / likely files | Depends | Acceptance and verification |
| --- | --- | --- | --- |
| 1 | Pin OpenIddict dependencies; API registration/options and tests | Review | .NET 8-compatible library configured, HTTPS/issuer/credential validation fails closed; API build and options tests. |
| 2 | OpenIddict EF integration; AppDbContext and migration | 1 | Standard tables, additive SQL and snapshot consistent; generate/review PostgreSQL SQL, test disposable SQLite and PostgreSQL when available. |
| 3 | Client environment/scopes mapping; domain/configuration and tests | 2 | Public has no secret, confidential has server authentication, exact per-client redirects; registry tests. |
| 4 | Registry writes synchronized with library application manager; service and tests | 3 | Create/update/rotate/revoke cannot leave usable stale library state; transaction/concurrency tests. |
| 5 | SSO sessions; entity/configuration/service and tests | 2 | Separate hashed session identity, expiry, revocation and live account checks; session/fixation tests. |
| 6 | SSO session migration | 5 | Additive schema and indexes; migration consistency/disposable DB check. |
| 7 | First-party bootstrap API and tests | 5–6 | No URL credential, authenticated identity plus origin/CSRF/browser binding; login CSRF/cross-origin tests. |
| 8 | Expo first-party SSO navigation adapter and tests | 7 | Existing login retained, same-origin bootstrap, validated local return and timeout; frontend tests/typecheck. |
| 9 | Authorization request handler and tests | 4,7 | S256 only, valid session, status/domain/environment/scope gates, prompt behavior; request matrix. |
| 10 | Pairwise subject/consent model and configuration | 9 | Unique stable per-app/environment subject; consent references library authorization; persistence tests. |
| 11 | Consent/subject migration | 10 | Additive unique/FK constraints; migration consistency and disposable DB check. |
| 12 | Consent service and protected allow/deny endpoints | 11 | Same scope fast path, new scope re-consent, no developer grants, CSRF protected; bypass tests. |
| 13 | Consent browser page and frontend tests | 12 | Logo/name/developer/scope descriptions, accessible allow/deny/loading/error; browser verification. |
| 14 | Token exchange and UserInfo handlers/tests | 12 | Real OpenIddict redemption, filtered claims and dedicated resource, immediate lifecycle gates; code replay and claims tests. |
| 15 | Revocation/introspection and owner authorization API/tests | 14 | Ownership enforcement, grant token revocation, restricted introspection; API and cross-client tests. |
| 16 | Optional confidential refresh policy/tests | 15 | Rotation/expiry/family reuse revocation proven, otherwise keep issuance disabled; deterministic replay tests. |
| 17 | Account link/unlink contracts/service/tests | 15 | Existing partner ownership proof required, stable issuer/sub mapping, no email takeover; link/unlink tests. |
| 18 | Link/audit schema migration and redaction tests | 17 | Additive bounded audit records and link indexes; no token/code/verifier/secret logs; SQL and redaction checks. |
| 19 | SDK package foundation/config/errors/build/tests | 14 | Independent TS package, no secrets/React dependency, reproducible build; package build/unit tests. |
| 20 | SDK real OIDC auth adapter and tests | 19 | Library discovery/signature/state/nonce/issuer/audience/expiry validation, timeout and reload behavior; callback attack tests. |
| 21 | SDK bridge transport and tests | 19 | Exact origin/source, schema/request IDs/timeout/permissions; spoofing, cleanup and unsupported-method tests. |
| 22 | Controlled browser host harness and tests | 21 | Verified handshake and only implemented platform methods, no social token messages; real iframe harness tests. |
| 23 | Developer SSO configuration/diagnostic API and BFF tests | 4,15 | Owned clients only, actual discovery/configuration data, existing CSRF boundary; backend/BFF tests. |
| 24 | Portal “Đăng nhập ANKT” tab and frontend tests | 23 | Public/confidential guidance, copy controls, diagnostics and error states; typecheck/build/browser review. |
| 25 | Public SPA example and isolated fixture | 20,22 | PKCE without secret; first/repeat consent, UserInfo/reload/logout; real browser protocol E2E. |
| 26 | Confidential backend example and isolated fixture | 20 | Server-only secret/exchange, validated identity, independent protected cookie; real protocol/session E2E. |
| 27 | Full attack matrix and regression suite | 16–26 | All 27 requested cases mapped to executed tests or explicit limitations; both E2E flows and existing auth regression. |
| 28 | Integration/key rotation/environment docs and handoff | 27 | Report all 18 requested report categories, executed evidence, remaining work and Stage 6 prerequisites. |

Checkpoints after 4, 8, 15, 22, 26 and 28: inspect diff, build impacted projects,
run relevant tests, record evidence and unresolved risks before continuing.

## Risks and decisions

- Existing callbacks are app-wide, but client/environment authorization must be
  explicit; do not silently reuse one environment's authorization in another.
- Existing encrypted secrets are retrievable by the owner. Library integration
  must respect existing rotation/revocation without duplicate credential truth.
- Existing refresh cookie is not a `/connect` login session; SSO bootstrap must
  handle browser binding, CSRF and live account checks before user-facing rollout.
- No native runtime is verified. Scope ends at a controlled bridge harness;
  unsupported native methods must fail explicitly.
- Production keys, issuer and domain deployment are operational prerequisites.
  Isolated test TLS/keys are disposable and never production configuration.
- Stage 4 public-domain/PostgreSQL limitations remain open until actually tested.

## Current verification

Task 1 complete: OpenIddict Core/Server.AspNetCore/Validation.AspNetCore pinned to
7.7.2; disabled-by-default API registration; HTTPS issuer, bounded code/access
lifetimes and externally provisioned distinct private RSA certificates validated
at startup. Existing authentication defaults remain intact. No signing key files
are generated by application startup or committed.

TDD RED: focused test compilation failed because `viora_BE.Sso` was absent.
GREEN: initial 17 focused cases passed; a wrong-password/redaction case was added,
then solution build passed with 0 warnings/errors and the full suite passed with
204 passed, 1 pre-existing database-dependent skip (205 total, including 18 SSO
cases). Full suite required unsandboxed execution because existing HTTPS fixtures
import Windows certificate-store keys; sandbox run had 12 Access denied failures.
Production code/tests were not modified to bypass this environment restriction.

Commands: `dotnet build viora-BE.sln --no-restore --verbosity quiet -m:1
-p:UseSharedCompilation=false`; `dotnet test
Viora.Application.Tests/Viora.Application.Tests.csproj --no-build --no-restore
--verbosity quiet -m:1`.

Task 2 complete: standard OpenIddict EF Core stores share AppDbContext. Additive
`20261016071639_AddOpenIddictStores` creates only the four library tables/indexes.
Real isolated PostgreSQL 18.6 verified migration up/down/up, preservation of an
unrelated table, transaction rollback, unique client ID and optimistic concurrency.
The fixture creates its own loopback-only cluster and stops/removes it; it never
reads application database configuration. Initial process-pipe hang was corrected.
Build: 0 warnings/errors. Full backend suite: 206 passed, 1 existing skip.
No database migration, production configuration, deployment or SDK publication
has been performed against production.

Task 3 complete: client and redirect environment bindings, opt-in Sandbox,
allowed scopes, client concurrency version and lifecycle/domain/scope policy.
Existing legacy credentials/callback APIs address Sandbox explicitly; app deletion
revokes every environment. Generated `AddSsoClientEnvironments` is additive except
for replacing uniqueness indexes with environment-aware indexes. Existing migrated
clients default to disabled Sandbox with empty scopes (configuration required).
Rollback requires resolving duplicate environments explicitly; no data is silently
discarded. 12 policy cases plus actual PostgreSQL migration/index/preservation
checks passed; related suite: 58 passed. Solution build: 0 warnings/errors.
Task 4 complete: `MiniAppOidcRegistry` projects existing ClientKey/type/secret,
PKCE requirement, environment, scopes and redirects through the standard library
manager in the same AppDbContext transaction as registry writes. Existing create,
update, rotation, callback, integration and delete paths participate when SSO is
enabled. Entity caching is disabled; Public Client credentials API rejects secret
retrieval/rotation. Deletion/SSO disablement removes projections and their grants.
PostgreSQL tests prove hash-only projection, old-secret rejection, atomic failure
rollback, Public Client without secret, environment-specific callbacks/scopes,
and removal of both environment projections. An ImmutableArray assertion was
corrected to compare contents. Solution build: 0 warnings/errors. Full suite:
220 passed, 1 pre-existing skip (221 total).

Checkpoint 4: Tasks 1–4 verified. No protocol exchange, user SSO session, consent,
SDK, portal SSO tab, bridge or E2E completion is claimed. PostgreSQL tests use new
disposable clusters; deployed database state remains unverified. Task 5 is next.
Task 5 complete: opaque SSO sessions store hashes, expire within 8 hours or the
source social-session expiry, and recheck account status plus live refresh-session
family on every use. Explicit revoke, expiry, banned/deleted account and source
session revocation are tested. Newly issued social access JWTs add signed `sid`;
existing endpoints/default schemes remain intact. Older tokens refresh normally
before bootstrap (Task 7–8).

TDD exposed a dependency regression: OpenIddict brought IdentityModel 8.19.2 while
JwtBearer's OpenIdConnect/Jwt assemblies remained 7.1.2; the legacy JWT reader
silently skipped alternating claims. Pinning both remaining assemblies to 8.19.2
fixed parsing. A real signature/issuer/audience/lifetime validation regression test
now protects social JWT compatibility. Upstream requires aligned IdentityModel
versions: https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/wiki/IdentityModel-8x
Build: 0 warnings/errors. Full suite: 222 passed, 1 existing skip (223 total).
Task 6 complete: additive `AddAnktSsoSessions` migration. PostgreSQL test executed
up/down/up, account FK enforcement, hash uniqueness and conditional single-winner
revocation; original Accounts row remained after rollback. Session-focused tests:
3 passed. Build: 0 warnings/errors.

Task 7 complete: `/sso/start`, `/sso/bootstrap` and `/sso/complete`, dedicated
cookie session scheme, exact configured first-party origin/CORS, signed social
session binding, local authorization return paths, secure HttpOnly Lax cookies,
no-store/no-referrer responses and independent process-local rate limiting.
Top-level completion avoids third-party cookie dependence. Internal bootstrap
transaction metadata is persisted by additive `AddSsoLoginTransactions` migration.
Real JWT HTTP tests cover origin rejection, cookie binding, top-level completion
and replay. PostgreSQL up/down/up and two concurrent completions issue exactly one
session. Focused bootstrap/session/startup suite: 28 passed; solution build clean.
Frontend bootstrap page is Task 8; partner authorization/token flow remains absent.
Task 8 complete: Expo `/sso/login` page, existing login/Google/profile return
integration, fixed-issuer bootstrap adapter, one refresh retry for legacy/expired
access JWTs, timeout/cancel/error states and validated local pending return.
Frontend tests: 32 passed (7 SSO + 25 existing Developer), TypeScript and focused
ESLint passed; Expo web export passed (Metro recovered from an existing cache
deserialization warning). Real Chrome verified error/retry, accessibility tree and
320px layout with scrollWidth=innerWidth=320. No console errors; one pre-existing
shared `shadow*` deprecation warning. Browser success-path OIDC E2E remains for
Tasks 25–27, not represented by unit transport doubles.

Bootstrap retry refinement: the same account/signed social session may repeat an
unconsumed authenticated transaction after a lost response; another account or
session cannot overwrite it. TDD regression covered the retry. Backend full suite:
231 passed, 1 existing skip (232 total); solution build clean.

Checkpoint 8: Tasks 1–8 verified as individual increments. Existing frontend
dirty files are preserved; only SSO-specific layout hunks belong to this work.
Authorization handler, consent, token exchange, SDK, bridge, portal SSO tab and
complete public/confidential OIDC E2E remain outstanding.

Task 9 complete: authorization endpoint passthrough backed by OpenIddict protocol
validation; explicit ANKT SSO cookie authentication; fresh registry lifecycle,
domain, secret expiry and scope checks; exact redirect binding; mandatory nonce;
prompt=none login/consent errors. Interactive anonymous users enter first-party
bootstrap. Issuance remains securely denied until consent Tasks 10–12 land.
Real library HTTP tests: 9 passed, covering untrusted client/redirect, S256-only,
missing PKCE/nonce, unsupported flow/scope, session reuse and suspended app.
Early library validation returns direct text errors before redirect validation;
tests verify that response rather than expecting unsafe error redirects.
Solution build clean. Tasks 10–28 remain incomplete.

Independent adversarial review before consent found two actionable issues, both
reproduced by failing regression tests and fixed: browser-binding cookies now use
`__Host-` with Path=/ (including deletion) to reject sibling-domain injection;
expired/missing confidential credentials remove only their unavailable projection
instead of rolling back healthy clients in another environment. Related HTTP/PG
suite: 18 passed; build clean. Second scoped review found no remaining issues.
The user selected internal review only; no external cross-model CLI is authorized.

Task 10 complete: pairwise mapping per account/app/environment with unique subject
within each sector; owner/client/environment-bound consent, active-grant uniqueness,
version concurrency and nullable FK to the standard OpenIddict authorization.
Library authorization deletion clears that reference without destroying consent
history. Model/authorization tests: 10 passed; solution build clean. Actual
PostgreSQL persistence/migration and concurrency checks follow in Task 11.

Task 11 complete: additive `AddMiniAppConsents` migration. Isolated PostgreSQL
verified up/down/up, sector uniqueness, independent environment subjects, active
consent uniqueness, concurrent update rejection, authorization FK SET NULL and
preservation of Accounts plus stable pairwise identity when authorization is
deleted. Focused schema/PG suite: 2 passed; solution build clean.

Task 12 complete: consent is bound to the authenticated owner, exact validated
request, environment and scopes through antiforgery plus a five-minute protected
interaction ticket. Real authorization codes are issued only after approval;
unchanged grants take the fast path, prompt=consent forces interaction, and
prompt=none returns consent_required without a grant. Pairwise subjects remain
stable; principals disclose only explicitly scoped profile/verified email claims.
Isolated PostgreSQL concurrent approvals leave one consistent active consent.
Internal review found expired consent renewal and raw PostgreSQL commit-conflict
handling defects; both were reproduced RED and fixed. Expired consent history is
retained and serialization/deadlock conflicts return retryable HTTP 409. Related
consent/authorization suite: 23 passed; solution build: 0 warnings/errors.
The consent browser UI and token redemption remain Tasks 13 and 14.

Task 13 complete: server-rendered Vietnamese consent page displays safe HTTPS
logo, app/developer names, explanations of requested scopes and accessible native
allow/deny controls. Razor encodes untrusted fields; nonce CSP disallows scripts
and framing. Native document/form navigation avoids a separate JavaScript loading
state. JSON responses remain available for explicit application/json clients.
Internal review exposed decision parameter injection: a full HTTP regression
proved duplicate allow/deny values could grant permission. Reserved query controls
are now filtered, and POST rejects ambiguous controls; all regressions pass.
UI/endpoint suite: 13 passed; solution build clean. Chrome checked actual rendered
HTML (fixture hidden values redacted) at 320px: no overflow, 48px controls, no
scripts or console errors. Live browser protocol E2E remains Tasks 25–27; this
preview verifies rendering only. Production headers are verified by HTTP tests.

Task 14 complete: OpenIddict still performs protocol validation and redemption;
passthrough handlers additionally recheck registry/domain/environment, active
account and exact live consent/authorization before exchange and every GET/POST
UserInfo call. ID tokens are RSA-signed, nonce/client-bound and contain pairwise
identity; persisted reference access tokens target the separate environment SSO
resource. No refresh token issuance is enabled. Tests cryptographically validate
ID tokens against real JWKS and reject wrong PKCE/redirect/client, code replay,
suspension and revoked consent/domain/account. PostgreSQL concurrent redemption
produced exactly one token response. Baseline auto-redemption was proven to fail
the new suspension test before passthrough was enabled.

Browser boundary: public metadata GET allows anonymous CORS; token/UserInfo only
allow verified eligible Public Client callback origins, without credentials.
Actual requests enforce the token client's own origin; Confidential Client calls
with browser Origin are rejected. Existing Web/first-party policies are retained.
Internal review fixes cover POST UserInfo and normalized root/default-port callback
origins. CORS currently enumerates unrevoked public callbacks for normalization;
large-registry optimization remains a measured operational concern, not a cached
authorization decision. Related authorization/consent/token/CORS suite: 49 passed;
solution build clean. Library reference:
https://documentation.openiddict.com/configuration/claim-destinations.html and
https://github.com/openiddict/openiddict-samples/tree/dev/samples/Velusia.

Task 15 complete (2026-10-17): library-backed revocation, operator-allowlisted
Confidential Client introspection with own-token and fresh registry/consent/account
checks; browser Origin introspection is rejected. Social owner list/detail/history
and idempotent revoke require live signed social sessions; list pages are bounded
to 100, with validated offset. Grant and persisted tokens revoke transactionally.
Internal review reproduced historical tokens remaining valid after reapproval;
regression now checks actual stored statuses and replacement revokes them too.
Isolated PostgreSQL proved concurrent owner revoke and rollback on consent-write
failure, preserving the prior authorization/token states. Library authentication,
token validity and ownership checks remain enabled.

Task 16 complete: refresh issuance remains disabled for Public and Confidential
Clients. Tests exercise successful code exchanges without refresh tokens, rejected
refresh grants/offline_access, and discovery advertising no refresh capability.
No unproven refresh rotation/family reuse policy is enabled.

Task 17 complete: user selected partner-session backend attestation with a one-use
challenge. Refined contract is recorded in the spec. Standard application-manager
secret verification, serializable credential snapshot, exact credential-ID binding,
hashed two-minute proof, live first-party owner confirmation and active consent
protect account linking. Permanent app/environment subject/local-account bindings
survive unlink/relink and cannot be reassigned. Email never identifies a link.
Internal review found a credential-expiry race producing HTTP 500; deterministic
clock regression reproduced it and nullable lookup now returns structured denial.
HTTP tests reject browser/backend mixup, Developer JWTs and implicit approval.

Task 18 complete: generated additive `20261017014703_AddSsoAccountLinks` contains
only three new tables/indexes/FKs/check constraint. Disposable PostgreSQL exercised
up/down/up, single-winner concurrent proof consumption, tombstone uniqueness,
identity/consent preservation after rollback and audit/proof redaction. Model
snapshot check reports no pending changes. Audit stores fixed bounded event names
and internal IDs only. Rollback deletes these new link/proof/audit records; it
preserves existing accounts, pairwise identities and consents. Never run down on
production without an explicit data-retention decision.

Checkpoint 18: complete backend suite 305 passed, 1 pre-existing skip (306 total).
PostgreSQL/HTTPS tests required unsandboxed execution; sandbox cannot start its
isolated PostgreSQL processes. No application/production DB was contacted. First
Task 18 checkpoint had one failing test; after the expiry fix and assertion cleanup,
the focused PostgreSQL test and full suite passed. Tasks 19–28 remain outstanding.

Task 19 complete: independent private ESM `../viora/packages/miniapp-sdk`, TypeScript
declarations/build, version, typed redacted errors and strict immutable public-client
configuration. Exact openid-client 6.8.8 dependency and isolated lockfile; no React
or shared frontend dependency edits, no secrets and no package publication.

Task 20 complete at unit level: actual library discovery/code/PKCE/state/nonce and
RS256/JWKS signature validation (explicit nonrepudiation checks), issuer/audience/
expiry validation, UserInfo subject binding, bounded timeout, five-minute one-use
transient transaction, memory-only tokens, reload and local logout semantics.
All endpoints are constrained to the issuer HTTPS origin, fetch omits cookies and
rejects redirects. 13 SDK tests/build passed, including bad signatures/claims,
wrong callback/state, endpoint substitution, expiry, UserInfo mismatch and logout
during exchange. Tests use intercepted HTTP with real signed JWT/JWKS fixtures;
real public/confidential server/browser E2E remains Tasks 25–27.

SDK command: from `../viora/packages/miniapp-sdk`, `npm test` (build plus Node tests).
Node tests use existing repo's no-process-isolation convention because sandbox
child process creation returns EPERM. Independent internal SDK review found no
actionable issues. Tasks 21–28 remain incomplete.

Task 21 complete: standalone SDK host-bridge connector returns insideHost only
after a verified handshake. Namespaced v1 envelopes, exact origin/source/schema,
UUID request IDs, 32 pending request cap, bounded timeout, method permission checks,
pagehide/dispose cleanup and typed errors are covered. Only controlled getContext
and close are exposed; arbitrary payloads and native/social auth are unsupported.
Internal review reproduced array-to-string coercion in the environment schema;
the failing regression now passes with an explicit string check. SDK suite/build:
19 passed. Latest frontend TypeScript check also passed before bridge addition;
final cross-project verification follows below.

Task 22 complete: independent `tools/SsoBridgeHost` fixture, ephemeral self-signed
HTTPS on two loopback origins, restrictive CSP, sandboxed child iframe and exact
host protocol allowlist. Real Chrome verified handshake, user-triggered context
response and close (counts 1/1/1), then no console warnings/errors. Browser required
a local certificate-warning exception; system trust was not modified. Initial
TLS failed because direct certificate creation was not Schannel compatible; the
fixture now uses the same export/import approach as the existing HTTPS fixture.
Private-key TLS use required approved unsandboxed execution. Fixture has no app
configuration/database dependencies and claims no mobile/native runtime support.

Checkpoint 22: Tasks 1–22 implemented; Tasks 23–28 remain incomplete. Stage 5 overall
is not complete. Developer SSO configuration/diagnostics tab, public SPA and
confidential backend examples, real browser OIDC E2E, full 27-case attack mapping
and final operational/key-rotation handoff remain required. Package not published.

Final checkpoint validation (2026-10-17): solution build 0 warnings/errors; backend
305 passed/1 pre-existing skip; frontend `npx tsc --noEmit` passed after SDK/bridge
addition; Developer Portal + SSO bootstrap regression 32 passed; SDK 19 passed.
The default frontend test runner failed with sandbox child-process EPERM; the same
test files passed using approved unsandboxed Node/tsx with process isolation off.
`git diff --check` passed. Full frontend production export was not rerun because no
existing app UI/runtime imports changed; the new SDK compiles as its own package.
The controlled bridge fixture was stopped after verification. No commits were made;
pre-existing unfinished Task 15 files were completed in place and unrelated edits
remain unchanged. Next implementation starts at Task 23, reusing existing Developer
Session/BFF CSRF boundaries and authoritative registry transactions.

Task 23 complete (2026-10-17): owned DeveloperSession GET `/api/developer/mini-apps/{appId}/sso`
and PUT `/sso/{Sandbox|Production}` configure actual clients, scopes, redirects and
Sandbox eligibility. The app-wide SSO toggle is explicit; Production configuration
does not grant Published status or user consent. App/client concurrency versions,
existing registry transactions and standard application manager remain authoritative.
Callbacks canonicalize identically to the library. Diagnostics compare actual
projection type, redirects, PKCE and permissions, and return actual library issuer/
discovery/endpoints only when runtime is enabled; disabled runtime returns null URLs.
Legacy HTTP callback text remains editable but is diagnosed invalid for SSO.

Confidential secrets are encrypted server-side and never in configuration DTOs,
the SSO tab or its snippets. Direct owner-backend GET `/sso/{env}/credential` and
POST `/sso/{env}/credential/rotate` are rate-limited/no-store and reject browser
Origin/Sec-Fetch-Site; rotation requires current ClientVersion. BFF only allowlists
configuration GET/PUT and rejects both credential routes. Public credentials are
rejected; switching to Public revokes old secrets. Legacy Sandbox rotation now
also bumps client.Version so it cannot bypass the new stale-writer boundary.
Audit contains fixed action names only. Disposable PostgreSQL verified single-writer
configuration, atomic projection-failure rollback, expired secret recovery and
rotation rejecting old secrets/stale writers. HTTP tests verify ownership and that
diagnostic URLs match actual enabled discovery. No schema/migration was added.

Task 24 complete: integrated `Đăng nhập ANKT` tab, explicit environments/types,
scopes/callbacks, global and Sandbox switches, readiness/error explanations,
copy fields and public/confidential guidance without secrets. Request generation
guards, dirty navigation/create-discard/reset, retry, loading/error and empty
Production defaults are implemented. Browser preview used the actual detail/panel
components with isolated transport/navigation fixtures: save, empty Production
callback, create-discard/cancel preserving saved baseline, controlled 503/retry,
and 320px viewport with scrollWidth=innerWidth=320 passed. Its only console error
was the deliberately injected 503. This verifies rendering/interactions, not live
portal-to-OIDC E2E. Native/runtime publication claims remain excluded.

Checkpoint 24 regression: backend 318 passed/1 pre-existing skip (319 total),
solution build 0 warnings/errors; frontend/BFF/SSO 34 passed, TypeScript and focused
lint and final Expo web export passed, including legacy-empty-scope editing. Full-suite verification
exposed a pre-existing wrapped PostgreSQL 40001/40P01 in link confirmation: exact
wrapped conflicts now become structured 409 after transaction disposal, without
automatic retry or proof consumption. Deterministic HTTP and actual PG regressions
pass. Internal review issues (projection permissions/canonical redirects, dirty
reset and mixed rotation versions) each informed targeted fixes. Full backend TRX:
`.codex-tmp/test-results/stage5-checkpoint24.trx`. No deploy, production migration,
package publication or commit. The next implementation at this historical checkpoint was Task 25.

## Final checkpoint — Tasks 25–28

Completed 2026-10-17. Actual Public SPA and Confidential backend examples execute
standard library discovery/code exchange/JWKS/UserInfo with independent identities
and storage. `tools/SsoE2EHost` creates only loopback HTTPS/temporary keys/accounts/
SQLite and a private partner config; no configured application database is used.
Real Chrome verified first/repeat/forced consent, reload/local logout, owner
revocation and reauthorization. Public deny and confidential server-only session
behavior were observed. `npm run test:sso:e2e` also repeats real protocol/session
lifecycles plus CSRF and callback-injection checks; it does not substitute for
browser SameSite/CSP checks. Successful pages have no console errors/warnings.

Task 27 adds a registered cross-client redemption attack, deterministic TimeProvider
code/access expiry, post-code environment/domain changes, social/URL credential
rejection and JWKS overlap. Task 28 adds bounded previous signing/encryption key
configuration; failing-then-passing tests prove missing/duplicate overlap fails
closed. Old-key code remains redeemable after new-key promotion. No migration or
new dependency in this final increment. Internal review completed.

Final checks: backend 329 passed/1 existing skip, solution build 0 warnings/errors;
SDK 23 passed and built; Developer/BFF 27 and first-party bootstrap 7 passed;
root TypeScript passed; real HTTPS E2E 1 passed. Earlier final Expo export passed
at Task 24; no later app UI/runtime import change requires another export.
TRX `.codex-tmp/test-results/stage5-final.trx`.

Full [security matrix](../testing/developer-platform-stage-5-security.md),
[integration/operations/key rotation](../operations/developer-platform-stage-5.md)
and [18-category handoff](../handoffs/developer-platform-stage-5.md) record evidence,
initial failures, limits and release prerequisites. No deployment, application DB
migration, publication, commit or Stage 6 work. Unrelated edits preserved.
