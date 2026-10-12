# ANKT Open API and standard SSO

## Objective

Implement the supplied 2026-10-10 Open API request in the existing .NET 8 backend, Expo app and admin. The new public origin is configured as `openapi.mxh.ankt.vn`. Replace the earlier unshipped Connect gateway plan. Reuse MiniApps, approved version snapshots, developers, user consent and pairwise identities. Retain the legacy SDK/exchange during migration; introduce standard OAuth authorization-code + S256 PKCE and OIDC through maintained OpenIddict.

## Audit and trust boundaries

Current internal JWT validation enforces HS256, issuer/audience, expiry and `token_type=access`. Existing bridge passes runtime binding and one-use authorization codes, not ANKT access/refresh tokens. Published app snapshots constrain redirects/scopes; current exchange returns identity rather than a reusable partner token. Existing public Swagger exposes internal API structure. CORS alone cannot prevent server-side callers. Public API and default internal auth must remain separate schemes; every resource request additionally checks active account, active app/developer and current consent.

## Plan and acceptance

1. Add OpenIddict persistence in the same database with its protocol tables only. Existing MiniApp registrations remain the source of truth; reconcile protocol clients and reuse existing hashed secrets. Add opt-in `OpenApi:Enabled`, configurable issuer, audience, host UI URL and persistent signing/encryption certificate configuration. Disabled is backward compatible.
2. Enforce an Open API host allowlist before controllers/static files/Swagger. Separate server/validation schemes from internal Bearer. Expose discovery/JWKS, authorize/token/revoke, userinfo and scoped `/v1` resources only.
3. Host-authenticated handoff creates a short-lived one-use ticket bound to client, exact redirect, state, nonce, scopes and PKCE. Third-party pages never receive the main JWT. Outside ANKT, redirect to the existing authenticated host UI for account/consent confirmation and resume OAuth. Honor prompt=none with protocol errors rather than granting missing consent.
4. Add access-token validity checks against token store and live registry/consent. Use five-minute access tokens; no refresh-token grant initially. Revoke and app/consent suspension take effect on the next request.
5. Extend SDK/bridge and partner examples for standard flow, preserving legacy requestLogin. Prepare automatic embedded login through a partner-owned server transaction; stop after denial/errors. Add host authorization UI and linked-app controls, developer integration information and admin audit/revocation views using existing UI tokens.
6. Generate additive protocol migration, Nginx allowlist, systemd/env templates, local/VPS instructions and rollback. Never apply production changes automatically.
7. Test actual library issuance/redemption and signed-token separation, wrong client/scopes/expiry/revocation, callbacks/PKCE/replay, host gate, consent, bridge leakage and frontend behavior. Build BE/admin and type-check Expo.

## Structure and commands

`Viora.Infrastructure/OpenApi`: registry/persistence/security; `viora-BE/OpenApi`: configuration/controllers/middleware; `Viora.Application.Tests/OpenApi*`: focused .NET tests; `deploy/open-api`: Nginx/systemd/env templates; Expo authorization screen and SDK bridge live in their existing feature folders. Existing C#/TypeScript conventions and scoped services; no account/wallet refactors.

```powershell
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -nr:false
dotnet build viora-BE.sln --no-restore -m:1 -nr:false
node --test tests/mini-app-sdk.test.cjs examples/mini-app-sdk.test.mjs
```

## Boundaries

Implement source, migrations and reviewable deployment configuration now. Do not deploy, alter DNS/TLS, restart remote services, apply production migrations or commit secrets. Keep unrelated working changes. Certificates and real domains are operator-provided in production; development keys are restricted to development. OIDC account linking does not merge partner accounts by email. SDK helpers cannot hold confidential-client secrets or tokens in the browser.
