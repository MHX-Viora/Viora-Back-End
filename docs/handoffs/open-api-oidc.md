# Open API OAuth/OIDC handoff

## Storage and deployment

- OpenIddict 7.7.1 protocol storage is additive to the existing PostgreSQL database; OpenApiDbContext has four protocol tables and separate `__OpenApiMigrationsHistory`.
- Generated migration and idempotent SQL are under `Viora.Infrastructure/OpenApi/Migrations` and `deploy/open-api/open-api-migration.sql`. No database migration was applied.
- Production templates: `deploy/open-api/*.example`; walkthrough/rollback: `docs/open-api-deployment.md`; architecture: `docs/decisions/open-api-oidc.md`; acceptance plan: `docs/specs/open-api-oidc.md`.
- Feature defaults off. Persistent operator-managed signing/encryption PFX files, exact proxy addresses, issuer/audience and host authorization UI are required before enabling.
- OpenApiTokenPruningService uses scoped OpenIddict managers hourly with a one-day retention cutoff. Audit retention remains separate.
- The former unshipped standalone partner-connect gateway/templates were removed; this implementation runs inside the existing backend.

## Operational boundary

No production DNS/TLS/Nginx/service changes, deployment, commits or database updates are performed by these artifacts. Existing internal JWT, registry, grants and legacy partner exchange remain separate. New partner OIDC clients must implement their own backend exchange/session/callback; the Nghệ Thuật Số source/deployment is not in this workspace.

## Verified here

Backend project builds without warnings. EF reports no pending OpenApiDbContext model changes. Generated SQL contains four new protocol tables, their FK/indexes and separate history; no DROP/ALTER of existing business tables. Actual library security tests and host/admin/SDK verification are tracked by the implementation owner before final handoff.


## Implemented architecture and source map

- Existing .NET 8 process and PostgreSQL registry are retained. `Viora.Infrastructure/OpenApi/*` adapts published MiniApp registrations/secrets/permissions to OpenIddict 7.7.1; no parallel developer or consent registry. `Viora.Infrastructure/OpenApi/Migrations/*` adds the library's Applications, Authorizations, Scopes and Tokens tables with FKs/indexes; no production database mutation.
- `viora-BE/OpenApi/PartnerOpenApiConfiguration.cs` configures opt-in issuer, independent signing/encryption certificates, separate validation policy and explicit trusted proxies. `PartnerOpenApiBoundary.cs` enforces exact public paths and closes protocol paths on internal hosts, including mixed-case variants and feature-off requests. Production Swagger is disabled.
- Main JWT remains HS256 under its existing issuer/audience/token_type validation. Partner tokens use distinct asymmetric keys, configured Open API issuer/resource audience, pairwise subject, client_id, scopes and 5-minute expiry. No refresh-token grant or offline_access in this release. Every protected call validates stored token status plus live account/app/developer/permission/consent.
- Codes and host handoffs expire in 60 seconds and are single use. S256 PKCE, exact verified callbacks, state and nonce are bound throughout. Handoffs belong to the protocol client so Admin revoke also removes pending handoffs. A consent version binds handoffs/codes/access tokens to the actual grants; revoke then regrant cannot resurrect old credentials.
- `viora-BE/Controllers/OpenApi/*` provides protocol/host/resources; Admin and Developer OpenApi controllers reuse owner/admin guards. Existing app review, callback/domain verification, scopes and secret rotation remain the business source of truth. Admin sees 7-day usage/401/403/429 and confirms token revocation with a reason/audit. Developer sees configured discovery, approved scopes/callbacks and setup metadata. Both panels show the latest five sanitized errors (API returns at most ten); query strings and malformed audit entries are excluded. Admin flags rate limiting or a high authentication/permission rejection ratio as a signal to investigate, not a diagnosis.
- Expo `services/open-api.service.ts`, `features/mini-apps/oauth-authorize-screen.tsx`, `app/oauth/authorize.tsx` and safe OAuth return handling resume through login/profile completion. Existing consent skips prompting; missing permissions require explicit selection. Navigation cancels pending decisions. `app/mini-apps/linked.tsx` and the marketplace link manage/revoke existing user grants, including unavailable apps.
- SDK 2.1 in `viora-BE/wwwroot/mini-app-sdk/ankt-mini-app.js` keeps legacy requestLogin and adds standard OAuth bridge + same-origin BFF helpers. HELLO/READY handles dynamically loaded SDKs; parent/source/origin/app/session/nonce stay bound. Main JWT and private runtime tokens never appear in partner URLs or bridge responses. Web allows a registered callback origin to re-handshake; native transient navigation allows the configured authorization endpoint only.
- `examples/mini-app-oidc/*` is an executable Node BFF using openid-client 6.8.8. State, nonce, verifier, Client Secret and tokens remain backend-side; browser CSRF and HttpOnly secure sessions protect start/logout/callback. `docs/open-api-partner-examples.md` covers JS/React/ASP.NET Core; `integrations/nghe-thuat-so-ankt/OPENAPI-OIDC.md` explains migration of the separate website.

## Endpoint inventory

Public issuer: GET `/.well-known/openid-configuration`, `/oauth/jwks`, `/oauth/authorize`, `/oidc/userinfo`, `/v1/me`, `/v1/me/profile`, `/v1/apps/{appId}/permissions`, `/health`, `/mini-app-sdk/ankt-mini-app.js`; POST `/oauth/token`, `/oauth/revoke`, `/v1/account-links`; DELETE `/v1/account-links/{id}`. Resources require the explicit OpenApi validation policy and endpoint scopes. Clients cannot substitute another app or account ID.

Internal authenticated host: POST `/api/open-api/authorize`; GET `/api/mini-apps/linked`; existing per-app consent DELETE. Admin: GET `/api/admin/mini-apps/{id}/open-api`, POST `.../open-api/revoke-tokens`. Developer owner: GET `/api/developer/mini-apps/{id}/open-api`. Internal APIs are not in the new hostname allowlist.

## Required setup and remaining operational checks

The complete DNS/TLS/Nginx/systemd/env/migration/rollback walkthrough is `docs/open-api-deployment.md`; reviewed templates are `deploy/open-api/*.example`. Configure OpenApi__Enabled/Issuer/Audience/AuthorizationUi/KnownProxies/SigningCertificatePath/EncryptionCertificatePath/CertificatePassword in protected backend environment. Set public EXPO_PUBLIC_OPEN_API_ISSUER for the host build. The partner's protected env requires ANKT_OIDC_ISSUER, ANKT_HOST_ORIGIN, ANKT_CLIENT_ID, ANKT_CLIENT_SECRET and ANKT_REDIRECT_URI. Public origins identify services; authorization is enforced by credentials/token/scopes, not hiding URLs.

No DNS, certificate provisioning, production Nginx reload, database application, deployment or commit occurred. Before rollout: approve/apply additive migration in staging PostgreSQL; provision real persistent keys/TLS and exact proxy addresses; run nginx -t on Ubuntu; deploy host/partner callback together; verify actual embedded cookie behavior and revoke/suspension on target browsers/devices. Node demo uses RAM session/account stores and must be adapted to the real website's shared store/database before production. No source/access for that actual website was available here. Safari/third-party-cookie restrictions and native isolation builds require explicit target-platform testing. Forced login/select-account/max_age requests are rejected rather than silently ignored; supported flow is code+PKCE with normal consent or prompt=none protocol errors.

Application limit: 120 requests/minute/IP; verified principals additionally use 600/minute/client and 60/minute/client+pairwise-user, including issuance and resource calls. Unknown/invalid tokens never establish user/client partitions. Audit stores path/status/duration/correlation only, with separate context and safe failure handling. Proxy/APM logging must also omit OAuth query strings/bodies/headers. Audit retention follows the existing operational policy; protocol pruning runs hourly with one-day minimum retention.

## Verification (2026-10-10)

- Full .NET regression: 153 passed, 2 PostgreSQL-dependent tests skipped, 155 total. Real OpenIddict TestServer cases cover cross-token boundaries, actual Admin revoke, code replay/expiry, PKCE/secret/callback/client swaps, inactive account/app/independent mode, consent revoke/regrant, pending handoff revoke, exact host gates including uppercase/feature-off, scope/link isolation, OIDC ID-token signature/nonce, CORS and rate limits. Registry/protocol fixtures use isolated SQLite, not production data.
- BE build: succeeded, no errors/warnings. OpenApi EF model has no pending changes; migration SQL is generated and additive, not applied.
- SDK: 17 tests passed. Partner BFF: 8 passed (actual maintained client/JWKS signature, state/nonce/issuer/audience/expiry plus HTTP CSRF/session/replay); npm dependency audit reported 0 vulnerabilities.
- Expo focused auth/mini-app/developer suite: 64 passed; full TypeScript and changed-file lint passed. Admin production build passed (existing bundle-size warning); 22 targeted Admin tests/typecheck/lint passed. Expo production web export also passed.
- Browser fixtures used actual Admin and Expo consent components with synthetic service responses. Admin revoke confirmation/reason/single-submit and responsive 390px were verified. OAuth has no automatic grant; identity-only confirmation requests only openid, handles 403 with retry and no redirect/token leakage. Checked aria state, >=44px targets, no horizontal overflow and clean console after fixes. These fixtures do not assert live production SSO.
