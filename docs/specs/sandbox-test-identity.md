# Sandbox test identity and passwordless partner testing

> Shared-account and automatic-login requirements now follow
> [sandbox-auto-login.md](sandbox-auto-login.md); this document records the prior design.

## Objective
An authenticated owner launches an owned Sandbox OAuth client with an ANKT test
identity; no social password is requested. Accounts and Users store the test
accounts, per user instruction. A shared template supplies the visible profile;
each Developer + Mini App has a persistent separate account and pairwise subject.
Existing managed Sandbox and real-user SSO remain supported.

## Contracts and boundaries
- Add Account.IsSandbox, default false; production account/user queries exclude
  these rows. JWT issuance and social SSO session creation reject them explicitly.
- Shared SandboxIdentityTemplate stores a template Account/User and missing
  non-KYC profile metadata (name/nickname/birthdate); do not forge verification.
  SandboxTestIdentity uniquely maps Developer + Mini App to an Account/User.
- Owner GET /api/developer/mini-apps/{appId}/sandbox-test returns profile, client
  readiness, callbacks and recent sanitized sessions. POST /sessions accepts only
  {} and issues a one-time, 60-second launch ticket plus a 15-minute session.
- Browser submits the launch ticket by POST to the existing issuer origin,
  /sso/sandbox-test/start. Validate Origin, consume atomically, set a distinct
  Secure/HttpOnly/Lax cookie, redirect to registered website. No tokens in URLs.
- Existing /connect/authorize authenticates this cookie only for its bound
  Sandbox client. Keep consent and antiforgery. OpenIddict continues code, nonce,
  state, redirect, PKCE, signing, encryption and reference-token enforcement.
- Private session binding survives encrypted codes and reference access tokens;
  every exchange/UserInfo checks owner session, app, client, account and expiry.
  Scope-based UserInfo: profile supplies name/preferred_username/nickname/gender/
  birthdate/picture/ankt_cover_picture; email supplies unverified sample email.
- No Production JWT/refresh tokens, SSO Production audiences, payments, wallet or
  notifications are issued to test accounts. Separate partner test storage remains
  the integrating Mini App's responsibility.
- Cloudinary: fixed asset IDs, reuse existing resources, explicit provisioning
  command only; no upload at application startup. Persist URLs in template/User.
  Missing or failed assets are reported, never simulated as live upload success.
- Additive migration only; do not migrate the configured application database
  during local verification. Use disposable SQLite/PostgreSQL and real OpenIddict.

## Plan
1. Entities/configuration/migration, profile provisioner, ownership/session service.
2. Tests for identity stability, isolation, expiry/replay, scope and production deny.
3. Bind sandbox sessions through existing SSO consent/token endpoints.
4. Owner API/BFF and responsive test profile/actions/status UI.
5. Run backend build/tests, FE typecheck/lint, browser E2E against a disposable
   issuer and partner using real authorization code + PKCE; document actual results.

## Commands and style
Use .NET 8 SDK already at .codex-tmp/sdk8-check/sdk; focused xUnit first, then
dotnet build/test. FE: node --import tsx --test --experimental-test-isolation=none;
node node_modules/typescript/bin/tsc --noEmit; changed-file ESLint. Existing DI,
typed DTOs, DeveloperAuthException codes and no-store responses are retained.
All remote provisioning must keep secrets out of output and version control.
