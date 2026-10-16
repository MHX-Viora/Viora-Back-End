# ANKT Developer Platform — Stage 5 plan

Status: conditionally approved 2026-10-16; all six conditions incorporated.
Spec: `../specs/developer-platform-stage-5.md`.

Tasks run sequentially in small slices, normally no more than five authored files.
Generated migration/designer/snapshot changes form their own reviewable task.
Each task begins with focused failing behavior tests and ends with build/tests.
No sub-agent orchestration, deployment, production migration or Stage 6 work.

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
Tasks 4–28 and all protocol/E2E checks remain incomplete.
