# ANKT Developer Platform — Stage 5 specification

Status: approved with conditions on 2026-10-16; conditions incorporated below.
Scope: the user's attached Stage 5 requirements, sections I–XXI. Stage 6 is excluded.

## Objective and acceptance

An authenticated ANKT user opens a Mini App, grants explicit consent when needed,
and signs into the partner through standard OIDC Authorization Code + PKCE S256.
Existing consent permits a fast top-level redirect flow without another password.
The partner validates identity and creates its own session. ANKT social JWTs and
refresh tokens never reach the partner. No promise of universal silent sign-in.

Completion requires working public/confidential examples, SDK and bridge harness,
real protocol exchanges, executed security tests, and an honest verification report.

## Approved implementation conditions

1. Mini App Registry is the credential authority. OpenIddict application records
   are transactional projections of ClientKey, client type, environment, redirects
   and credential lifecycle. Existing secret rotation/revocation must invalidate
   token endpoint authentication immediately; no independently managed credentials.
2. Valid ANKT session plus unchanged valid consent takes the fast SSO path without
   another login. Consent and application lifecycle checks always remain active.
3. Sandbox/Production separation covers client, token resource/audience,
   redirect URI, authorization and access permissions, not only a UI setting.
4. Public clients use PKCE without secrets; confidential secrets stay backend-only.
   Standard OIDC libraries perform protocol and identity validation.
5. Migration, transaction and concurrency verification must execute against a
   disposable PostgreSQL test database. SQLite is supplementary evidence only.
   Never use the configured production database or infer permission to migrate it.
6. Only Stage 5: no Stage 6, production deployment or SDK publication.

Task 1 registers OpenIddict 7.7.2 behind `Sso:Enabled=false` by default, preserving
existing startup and authentication. Explicit enablement requires a validated
HTTPS issuer and separate externally supplied RSA signing/encryption certificates.
No development certificate or ephemeral-key fallback is enabled. Persistent stores
arrive in Task 2; this increment does not claim usable protocol endpoints.

## Repository audit and reusable components

| Area | Observed source | Reuse / required change |
| --- | --- | --- |
| Social authentication | `viora-BE/Controllers/AccountsController.cs`, `Viora.Infrastructure/Security/JwtTokenService.cs`, `viora-BE/Program.cs` | Existing login and Google login, HS256 access JWT validation; preserve social authentication. This is not an OAuth authorization server. |
| Refresh lifecycle | `Viora.Infrastructure/Persistence/Repositories/AccountRepository.cs` | Hashed refresh credentials and conditional revocation/rotation writes; keep separate from Mini App tokens. |
| Browser cookie | `AccountsController.cs` | Secure HttpOnly refresh cookie, SameSite=None, path `/api/accounts`; not a user session available at `/connect/authorize`. |
| Identity/data | `Viora.Infrastructure/Persistence/AppDbContext.cs` | Accounts, Users, UserIdentities, ExternalLogins and PostgreSQL EF Core context; reuse identity after checking account status and verified claim provenance. |
| Developer identity | `viora-BE/Developer/DeveloperAuthentication.cs` | Independent DeveloperSession scheme and DeveloperActive policy; never use it as the ANKT user's identity. |
| Client registry | `Viora.Domain/Developers/MiniAppEntities.cs` | MiniApp, MiniAppOAuthClient, ClientKey, encrypted client secrets and revocation exist. Default clients are Confidential. Need public clients, scopes and environment binding. |
| Secret lifecycle | `Viora.Infrastructure/Developers/MiniAppService.cs` | Existing backend-only credential retrieval and rotation must stay compatible with token endpoint authentication. |
| Redirects/domain | `MiniAppIntegrationService.cs`, `MiniAppDomainLifecycle.cs` | App-level callbacks, canonical hosts, expiring/revocable app-bound proof; add per-client/environment checks and exact redirect matching. |
| Portal | `../viora/features/developer`, `../viora/server/developer` | Integrated Expo web portal and BFF with existing session/CSRF boundaries; extend these rather than assume a Next.js application. |
| HTTPS/CORS | `viora-BE/Program.cs` | HTTPS redirection and configured Web origin policy exist; add constrained OIDC behavior without widening social API access. |
| Keys | `JwtTokenService.cs`, `Program.cs` | Social symmetric key is unsuitable for public OIDC verification. Provision independent asymmetric signing/encryption credentials. |
| Mobile/runtime | `../viora/package.json` | Expo/React Native, secure storage and browser packages exist; no verified Mini App WebView bridge found in the targeted audit. Implement controlled core harness only. |
| Test foundations | `Viora.Application.Tests`, `tools/DeveloperE2EHost`, `../viora/features/developer/tests` | xUnit, TestHost, SQLite and developer E2E tooling can be extended with real OIDC flows. |

Audit is source-based, not proof of deployed database state or production readiness.
Stage 4 handoff records that public-domain E2E and PostgreSQL execution remain
unverified. Historical Mini App migrations do not establish a current OIDC runtime.

## Proposed architecture

- Embed OpenIddict server/core/validation integration in the existing .NET 8 API;
  use its EF Core applications, authorizations and tokens, not custom JWT issuance.
  Task 1 pins the .NET 8-compatible stable release 7.7.2.
- Preserve default social Bearer and DeveloperSession schemes. Add explicit OIDC
  schemes and a dedicated server-side ANKT SSO user session with a protected,
  Secure HttpOnly host-only cookie, CSRF defense and expiry/revocation checks.
- Bootstrap SSO only from the existing authenticated first-party app through a
  same-origin, origin-checked, CSRF-protected POST. Validate the social identity
  and live account/session state. Never place its credential in a navigation URL.
  Bind the bootstrap transaction to the initiating browser before creating the
  SSO session; prevent login CSRF and session fixation. An expired social session
  returns through the existing login UI with a validated first-party return path.
- Four independent contexts: social user session, Developer Portal session,
  per-client Mini App authorization, and partner session.
- ClientKey remains public client_id. Map registry clients to OpenIddict
  applications atomically. Public clients have no secret; confidential clients
  authenticate server-side. Rotate/revoke updates must immediately affect token
  exchange. Do not create a second unsynchronized source of credential truth.
- Bind applications, redirects, authorizations and resources to Sandbox or
  Production. Sandbox registration grants no publication/review rights.
  Production requires Published status; suspended/deleted/revoked clients always
  fail closed. Sandbox test apps use explicit owner-authorized test eligibility
  and verified domains, not a production status bypass.
- Both authorize and token processing recheck effective SSO enablement, app and
  developer status, client revocation, environment, verified domain and scopes.
- Require code flow and S256 for both client types. Codes expire after 60 seconds,
  are single-use and bound by the library to client, redirect and PKCE. Initial
  access tokens expire after 5 minutes; final limits are configuration-validated.
- Use persisted opaque access tokens and immediate revocation validation for the
  SSO UserInfo resource. Expose introspection only to explicitly authenticated
  authorized resource servers. Issue standard asymmetrically signed ID tokens.
- Initially do not grant browser refresh tokens. Confidential refresh tokens may
  be enabled only with bounded lifetime, rotation, family reuse detection and
  revocation tests. An unverified policy remains disabled.
- Stable random pairwise subject mapping per Mini App/environment, never raw
  account IDs. All clients of that app/environment share its sector mapping.
- Grant only openid/profile/email with explicit consent. Return only permitted
  claims; release email only when actual verification evidence exists.
- Partner account key is `(issuer, sub)`. Linking an existing account requires
  authenticated ownership proof; matching email never authorizes linking.
  Unlink/relink must preserve subject stability without permitting reassignment.

## Storage and migration

Use OpenIddict's standard application/authorization/token tables. Add session,
pairwise subject, consent, account-link and bounded redacted auth audit storage.
Add per-client environment, redirect and allowed-scope bindings with uniqueness,
foreign keys and concurrency rules. Consent references the library authorization;
revocation invalidates its tokens. Pairwise mappings are unique for user + app +
environment and subject + app + environment.

Generate additive EF migration and PostgreSQL SQL; validate with disposable data.
Do not apply any migration to the configured or production database. Do not delete
historical migrations, reset databases or commit keys. Document rollback data loss.

## Public interfaces

| Interface | Contract |
| --- | --- |
| `GET /connect/authorize` | Standard request validation; login/consent handling; prompt=none errors; no redirect to an unvalidated URI. |
| `POST /connect/token` | Standard code exchange, optional policy-enabled refresh; no token URL transport. |
| `GET /connect/userinfo` | Dedicated SSO audience/token validation; scope-filtered claims; reject revoked grants. |
| `GET /.well-known/openid-configuration` | Library discovery with configured issuer and actual capabilities. |
| `GET /.well-known/jwks.json` | Public signing keys only; rotation overlap exceeds maximum valid ID token lifetime. |
| `POST /connect/revocation` | Library-backed authenticated/policy-appropriate revocation. |
| `POST /connect/introspect` | Authenticated authorized resource servers only; opaque access tokens. |
| First-party SSO session endpoints | Protected session bootstrap/logout; local return paths only; no partner credential transport. |
| User authorization API | List/detail/history/revoke owned grants; authorization owner is derived from user session. |
| Developer SSO API | Owned client/environment/type/redirect/scope configuration and diagnostics; Developer cannot grant user consent. |

Consent UI displays Mini App logo/name/developer and described scopes, allow/deny
actions, CSRF protection and server-bound transaction. New scopes require consent;
unchanged valid grants may skip it. Respect prompt=consent and prompt=none.

## SDK, bridge and examples

Proposed independent workspace package: `../viora/packages/miniapp-sdk`, published
name `@ankt/miniapp-sdk`. No npm publication is authorized by implementation scope.
Use an established OIDC client library for discovery/PKCE/state/nonce and ID token
validation. TypeScript, no React dependency, config validation, typed errors,
timeouts, Promise API, version, build and unit tests are required.

Implement initialize, auth.login/getUser/getStatus/logout with documented local
logout semantics. Do not imply logout revokes consent or signs out ANKT globally.
Implement platform methods only when supported by the controlled host protocol.
Tokens remain in memory; bounded transient PKCE/state transactions may use session
storage. Reload reacquires through top-level authorization or the partner session.

Bridge v1: namespaced versioned request/response, unpredictable request ID, timeout,
schema validation, exact targetOrigin, event.origin and event.source validation,
bounded outstanding requests, method permissions and navigation cleanup. No social
credentials traverse the bridge. Inside-host status requires a verified handshake.
Mobile native implementation is deferred; do not advertise unsupported native APIs.

Create public SPA and confidential backend examples with real discovery, token
exchange, signature/issuer/audience/nonce/expiry validation and separate sessions.
The confidential example keeps credentials/tokens server-side and cookies secure.
Portal adds “Đăng nhập ANKT”, copy controls, discovery/configuration diagnostics and
appropriate examples without embedding secrets in JavaScript.

## Commands and conventions

Backend cwd `viora-BE`: `dotnet build viora-BE.sln`;
`dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj`.
Frontend cwd `viora`: `npm run test:developer`; `npx tsc --noEmit`;
`npm run build:web`; `npm run test:developer:e2e` (isolated host required).
SDK build/test and SSO E2E scripts must be added with the package and then recorded
as exact executable commands. Never report nonexistent scripts as executed checks.

Follow existing Domain/Application/Infrastructure/API boundaries, DTO records,
explicit schemes, ownership queries, cancellation tokens and structured errors.
Example: `public sealed record SsoClientResponse(string ClientId, string ClientType);`.
Use thin endpoints and standard library protocol handlers rather than custom tokens.

## Verification and boundaries

Write failing focused tests before behavior changes. Exercise all 27 security
cases in the attached requirements: code reuse, PKCE/client/redirect mismatch,
expiry, state/nonce/issuer/audience/signature failures, escalation, consent bypass,
token and refresh replay, cross-client use, suspended app/unverified domain, CSRF,
XSS-sensitive flows, open redirect, linking takeover, UserInfo authorization,
bridge spoofing, credential leakage, fixation and cross-environment use.
Bearer access tokens are normally replayable until expiry/revocation; do not claim
sender-constrained replay protection without implementing and testing it.

Run real public/confidential first-consent and repeat-consent E2E, reload, logout,
reauthorization and revocation. Review actual browser behavior and secret-free
logs. PostgreSQL-specific migration/concurrency checks require a disposable test DB.
Controlled TLS harness results must not be described as public-domain E2E results.

Always preserve social auth/Admin permissions and unrelated changes. Dependency
and additive schema work are within the user's requested Stage 5 scope. Never
deploy, publish packages, run production migrations or start Stage 6 automatically.
Record executed, failed, skipped and blocked checks separately in the final handoff.

## Authoritative references

- https://documentation.openiddict.com/integrations/aspnet-core
- https://documentation.openiddict.com/configuration/application-permissions
- https://documentation.openiddict.com/configuration/proof-key-for-code-exchange
- https://datatracker.ietf.org/doc/html/rfc9700

## Review gate

The user approved this spec and plan subject to the six conditions above and
authorized implementation after updating both documents. No repeated approval is
required for planned tasks. A serious architectural issue requires spec/plan
revision and renewed review before dependent implementation proceeds.
Production issuer, key provisioning and real-domain deployment remain deployment
configuration; no secret or production access is needed to prepare isolated tests.
