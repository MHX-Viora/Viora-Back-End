# Developer Platform Stage 5 — completed implementation

Updated 2026-10-17. Canonical scope/acceptance: `../specs/developer-platform-stage-5.md`;
ordered tasks and accumulated evidence: `../plans/developer-platform-stage-5.md`.

## Current checkpoint

Tasks 1–28 implemented and verified in the controlled local environment. Public
and confidential examples, real Chrome OIDC E2E, the 27-case security matrix and
operations/key-rotation guidance are complete. Public-domain deployment and native
runtime verification remain release prerequisites outside this implementation.
No Stage 6 work, deployment, production migration or publication.

## New endpoints

- `POST /connect/revocation`: standard library token revocation; consent stays active.
- `POST /connect/introspect`: standard confidential authentication and own-token
  restriction; requires exact `Sso:IntrospectionClientIds` operator allowlist and
  eligible registry. No browser Origin. Existing projections must be synchronized
  through the registry before enabling newly added introspection permissions.
- `GET /api/me/miniapp-authorizations?offset=0`, `GET/DELETE /{id}`: live signed
  social Bearer only, owner-bound history and atomic grant/token revocation. Pages
  contain at most 100 records; advance offset until empty. Developer/SSO cookies
  do not authorize these APIs.
- `POST /api/sso/account-links/challenges`: Confidential Client HTTP Basic over
  HTTPS, JSON `{subject, partnerAccountId}`. Derive partner ID from authenticated,
  CSRF-protected backend session and subject from validated OIDC identity. The
  backend attests its local session ownership; ANKT does not inspect partner DBs.
- `POST /api/sso/account-links/preview`, `/confirm`: live social Bearer, JSON
  `{challenge, approve:true}` for confirmation. Explicitly show preview before
  approval. Never put proof or credentials in URLs. Proof expires after 2 minutes,
  binds current credential/client/environment/owner, and is consumed atomically.
- `GET /api/sso/account-links/mine`, `DELETE /mine/{id}`: live ANKT owner only.
  Unlink keeps permanent identity bindings and does not revoke consent or sessions.

No account-link browser page is claimed. Partners must build their authenticated
session initiation and ANKT consumers must present the preview/explicit approval.
No refresh issuance is enabled for either client type.

## Schema and verification

Additive `20261017014703_AddSsoAccountLinks`: link tombstones, hashed challenges and
bounded allowlisted audit records. Rollback loses those new records only. SQL
generated at `.codex-tmp/stage5-account-links.sql` for local inspection; never applied
to the configured application DB. No pending EF model changes.

Checkpoint 18 backend suite: 305 passed, 1 existing skip. Disposable PostgreSQL verified concurrent
revocation, transaction rollback, challenge replay/concurrency, migration up/down/up,
uniqueness and preserved identity/consent. HTTP/service tests cover owner isolation,
credential expiry, consent/domain/app/environment gates, explicit approval and
redaction. No public-domain or native runtime verification is claimed.

Commands from backend root:

```powershell
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-build --no-restore --verbosity quiet -m:1
$env:VIORA_EF_DESIGN_CONNECTION='Host=127.0.0.1;Database=unused_design_only;Username=unused'
dotnet ef migrations has-pending-model-changes --no-build --project Viora.Infrastructure --startup-project Viora.Infrastructure
```

Windows sandbox cannot start the isolated PG fixtures; full suite was run with
approved escalation. Review is internal only per the recorded user preference.
Unrelated frontend edits and pre-existing backend Task 15 work were preserved.

## SDK and bridge checkpoint

Independent unpublished/private `../viora/packages/miniapp-sdk`: strict public-client
configuration, ESM/declarations, openid-client 6.8.8, actual JWKS signature/claims
validation, state/nonce/S256, timeout, bounded transient sessionStorage transaction
and memory-only tokens. Reload reacquires; local logout does not revoke consent or
global/partner sessions. Unit tests intercept HTTP and use real signed JWT/JWKS;
they do not replace actual protocol/browser E2E.

Bridge verifies exact origin/source/schema/version/request ID, host permissions,
timeout/pending cap and pagehide cleanup. Only controlled getContext/close methods;
insideHost requires handshake. Standalone `tools/SsoBridgeHost` serves a sandboxed
iframe on two localhost HTTPS ports with a temporary certificate and no application
configuration/DB access. Chrome confirmed handshake/context/close (1/1/1), no console
warnings/errors; local certificate exception was used without changing system trust.
Native APIs remain unsupported. README contains executable setup/API examples.

SDK `npm test`: 19 passed and declarations compiled. Internal reviews found/fixed
historical token cleanup, credential-expiry 500 and environment schema coercion;
each has a failing-then-passing regression. No external-model CLI was used.

Checkpoint 22 cross-project checks: solution build 0 warnings/errors; frontend TypeScript
passed with the new package; existing Developer Portal + SSO bootstrap tests 32
passed using approved unsandboxed Node/tsx after sandbox EPERM. Diff whitespace
check passed. Existing app production export was not rerun: no app UI/runtime import
was changed. No commit or publication. Local bridge fixture stopped after testing.

At checkpoint 22, next work was Tasks 25–26 actual public/confidential examples and controlled browser
protocol E2E, Task 27 complete attack matrix/regression, Task 28 final operational
documentation/key rotation/environment guidance. Production/public-domain and
native validation are not implied by the controlled harness results.

## Portal SSO configuration checkpoint (Tasks 23–24)

Developer Mini App detail now includes `Đăng nhập ANKT`. Its GET/PUT BFF routes
retain the existing same-origin cookie and CSRF boundary, forwarding only an
owned DeveloperSession. Configuration requires optimistic app/client versions and
explicit Sandbox/Production; no environment fallback. Fields: ClientType,
SsoEnabled (app-wide), SandboxEnabled, Scopes (openid/profile/email), exact HTTPS
RedirectUris. Saving Production never changes app publication. All changes and
library projections share the existing transaction. No new migration/dependency.

Backend routes under `/api/developer/mini-apps/{appId}`:

- `GET /sso`: client settings/version/readiness and actual enabled library metadata.
  Disabled runtime returns null issuer/endpoint URLs. Readiness checks lifecycle,
  verified domain, credentials, exact projection permissions/redirects/type/PKCE.
- `PUT /sso/{Sandbox|Production}`: `MiniAppSsoRequest` DTO. Creates a missing
  environment explicitly, configures an existing client, rejects stale versions
  with 409 and normalizes URI host/default port consistently with OpenIddict.
- `GET /sso/{env}/credential`: current Confidential secret for authenticated owner
  backend tooling only. `POST /sso/{env}/credential/rotate`, JSON `{clientVersion}`,
  rotates atomically and invalidates the prior secret immediately. Both reject
  Origin/Sec-Fetch-Site and are deliberately absent from BFF allowlists. Do not
  expose their response through partner frontend code, logs or build variables.

Configuration responses/snippets contain no secret. Public transition revokes old
credentials. Existing Sandbox credential management shares the same registry and
now increments client.Version on rotation as well. Fixed audit actions contain no
credential material. Secrets are provisioned directly to owner backend tooling;
the new tab provides public identifiers and configuration only.

Frontend handles loading/save/retry/conflicts, explicit environment drafts, copied
metadata/snippets and dirty navigation. Legacy HTTP callbacks/scopeless migrated
clients remain editable; new saves require HTTPS and openid. Production starts
with empty callbacks rather than reusing Sandbox. Sandbox shortcut opens the SSO
tab; native runtime is not advertised.

Verification: backend 318 passed/1 existing skip, solution build 0 warnings/errors;
Developer/BFF/SSO 34 passed; TypeScript and focused lint passed. Disposable PostgreSQL
tested competing saves, failed projection rollback, secret recovery and rotation.
Real enabled discovery matches the diagnostic metadata via HTTP tests. Actual
React components in a local rendering fixture passed save, environment isolation,
dirty create-discard/cancel, 503/retry and 320px/no-overflow checks. One expected
network console error was the injected 503; no unrelated browser error observed.
This component fixture does not prove live browser OIDC exchange. Local fixture stopped.

Verification also exposed wrapped Npgsql serialization failures in older account
link confirmation. A targeted regression now proves 409 and unconsumed proof on
that conflict, and the real PG race plus full suite pass. No automatic retries.
TRX: `.codex-tmp/test-results/stage5-checkpoint24.trx`.

Final Expo production web export passed after all Task 24 UI changes. No deploy,
production DB migration, SDK publication or commit; unrelated edits preserved.

## Final report — Tasks 25–28 (2026-10-17)

| # | Category | Result / reference |
| --- | --- | --- |
| 1 | Scope and status | All 28 planned implementation tasks complete; Stage 6, deployment and publication excluded. |
| 2 | Architecture | Existing .NET/EF/OpenIddict server and Expo portal retained; four independent social/developer/SSO/partner contexts. |
| 3 | Backend changes | Existing Stage 5 endpoints above; final increment adds bounded previous signing/encryption certificate arrays and fail-closed distinct-key loading. |
| 4 | Frontend changes | Integrated SSO configuration tab retained; Public example DOM iteration fixed for the root TypeScript configuration. No further application runtime imports/UI changes. |
| 5 | SDK | Private independent package, openid-client 6.8.8, strict public config, memory tokens and transient PKCE transactions; 23 unit tests and declarations pass. |
| 6 | Public example | `../viora/packages/miniapp-sdk/examples/public`: actual SDK/discovery/PKCE/JWKS/UserInfo; consent/repeat/reload/local logout/revocation/reconsent executed. |
| 7 | Confidential example | `../viora/packages/miniapp-sdk/examples/confidential`: backend-only credential/exchange/tokens, separate opaque HttpOnly partner session, CSRF logout and one-use browser-bound callbacks. |
| 8 | Host bridge | Controlled iframe handshake/context/close verified at checkpoint 22; origin/source/schema/request/permission limits remain; native methods unavailable. |
| 9 | API and identity contract | Standard code flow; exact HTTPS callbacks; stable `(issuer, sub)` identities; email never authorizes account merging; refresh disabled. |
| 10 | Database/migrations | Existing additive Stage 5 migrations unchanged by final increment. Full disposable PostgreSQL migration/rollback/transaction/concurrency suite passed; configured application database untouched. |
| 11 | Environment setup | `tools/SsoE2EHost` loopback issuer + private generated partner config, temporary CA/certificates/accounts/SQLite. No application settings or DB connection used by fixture. |
| 12 | Tests and browser evidence | Backend 329 passed / 1 existing skip; Developer/BFF 27 + bootstrap 7; SDK 23; real HTTPS protocol E2E 1. Both Chrome flows passed; detailed sequence in security report. |
| 13 | Build and types | Backend solution build 0 warnings/errors, SDK declaration build and root TypeScript passed. Task 24 Expo export evidence retained; final changes do not add app runtime imports. |
| 14 | Security coverage | [27-case matrix](../testing/developer-platform-stage-5-security.md) maps executed tests and explicit boundaries, including normal Bearer replay while valid and disabled refresh. |
| 15 | Review and corrections | Internal correctness/security/architecture review; added real second-client denial, deterministic code/access expiry, cross-environment/domain changes, key overlap/JWKS and old-code decryption after promotion. Fixed SPA DOM typing. No remaining critical finding in this increment. |
| 16 | Operations and rotation | [Runbook](../operations/developer-platform-stage-5.md): exact commands, credential provisioning, key overlap/removal/cache horizon, environment separation, redacted logs and rollback/release prerequisites. |
| 17 | Failures/skips/limits | Initial sandbox certificate Access denied and esbuild EPERM resolved through approved isolated execution. New tests first exposed missing overlap support; test-driver duplicate form fields/expected 400 and cross-client seed uniqueness were corrected. One pre-existing MoneyAudit PostgreSQL skip needs its separately configured audit DB. No public-domain/proxy, external-provider, native or fleet rotation evidence claimed. |
| 18 | Delivery and next gate | No commit, deployment, production migration or npm publication. Preserve unrelated edits. Follow the separate release gate in the runbook before production; do not begin Stage 6 automatically. |

Final backend evidence: `.codex-tmp/test-results/stage5-final.trx` (329 passed,
1 existing skip). Exact regression commands from backend root:

```powershell
dotnet build viora-BE.sln --no-restore --verbosity quiet -m:1 -p:UseSharedCompilation=false
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-build --no-restore --verbosity quiet -m:1 --logger "trx;LogFileName=stage5-final.trx" --results-directory .codex-tmp/test-results
```

Frontend root: `npm run test:developer`,
`node --import tsx --test features/sso/bootstrap.test.ts`, `npx tsc --noEmit`.
SDK root: `npm test`; real fixture command and CA setup are in the runbook.
Fixture processes were stopped after verification; temporary issuer storage removed.
