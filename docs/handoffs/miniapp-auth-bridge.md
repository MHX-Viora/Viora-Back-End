# Mini App Auth Bridge handoff — 2026-10-26

## 1. Architecture and outcome

ANKT keeps its main JWT/RefreshToken login. A trusted iframe/WebView host requests
a short-lived, one-use code bound to the registered public AppId, verified website
origin, environment, source session, publication/domain policy and S256 challenge.
The SDK exchanges it for a separate opaque Mini App token, held only in memory.
UserInfo reads the actual Account/User. Only profile scope is supported. Main JWTs
never enter the partner frame; Mini App tokens cannot authenticate main JWT APIs.

Developer Sandbox uses the same persisted Sandbox Account/User for all authorized
owners, with independent launch/access sessions. Production uses the signed-in
ANKT user's real source session and requires an approved live publication. Both
flows work with Sso:Enabled=false and no OIDC certificate/discovery service.

## 2. Files added or modified for this change

Paths below are relative to each repository. The workspace already contained
other changes; this list describes this feature, not every dirty file.

Backend:

- Viora.Domain/Developers/MiniAppBridgeSession.cs
- Viora.Application/Developers/MiniAppBridgeContracts.cs
- Viora.Infrastructure/Developers/MiniAppBridgeService.cs
- Viora.Infrastructure/Developers/{SandboxTestService,SandboxTestOptions,MiniAppPublicCatalogService}.cs
- Viora.Infrastructure/Persistence/Configurations/MiniAppBridgeConfiguration.cs
- Viora.Infrastructure/Persistence/Migrations/20261026021305_AddMiniAppAuthBridge.cs,
  its Designer.cs, and AppDbContextModelSnapshot.cs
- viora-BE/Developer/{MiniAppAuthBridgeApi,MiniAppApi}.cs
- viora-BE/Production/{MiniAppProductionRegistration,MiniAppPublicApi}.cs
- viora-BE/Sso/{DisabledSsoControllers,SsoRegistration}.cs; viora-BE/Program.cs
- viora-BE/Hosting/TrustedProxyHeaders.cs and
  Viora.Application.Tests/TrustedProxyHeadersTests.cs: fix diagnostic handling of
  consumed forwarding headers; trusted networks/proxies and HTTPS enforcement unchanged
- Viora.Application.Tests/MiniAppAuthBridgeTests.cs
- tools/SsoE2EHost/{AuthBridgeBrowserFixture,Program}.cs
- tools/AuthBridgeE2E/{fixture.mjs,run-fixture.mjs,portal.fixture.jsx,miniapp.fixture.ts}
- .env.example; docs/specs/miniapp-auth-bridge.md;
  docs/testing/miniapp-auth-bridge-audit.md; docs/operations/miniapp-auth-bridge.md;
  legacy docs/operations/sandbox-auto-login.md; this handoff

Frontend/SDK:

- packages/miniapp-sdk/src/host-auth.ts, src/index.ts, tests/host-auth.test.mjs,
  package.json and README.md
- features/mini-apps/lib/{auth-bridge-host,host-controller}.ts
- features/mini-apps/adapters/{auth-host.web,auth-host.native,host.web,host.native}.tsx
- features/mini-apps/tests/auth-bridge-host.test.ts
- features/developer/components/mini-app-auth-bridge-preview.tsx,
  mini-app-auth-bridge.css, mini-app-bridge-integration.tsx,
  mini-app-sandbox.tsx and mini-app-detail.tsx
- server/developer/{account-proxy.cjs,sandbox-policy.cjs,auth-bridge-policy.test.cjs}
- server/mini-app-host-policy.cjs; .env.example
- Local mini-app-host Android MiniAppHostModule.kt and iOS MiniAppHostModule.swift:
  explicit direct-bridge mode retaining platform origin/main-frame checks

Ignored local .env files enable only the new feature flags/origin settings;
existing credentials/certificates remain intact. No secret values are in this report.

## 3–4. Files deleted and dependencies removed

None. The new SDK /bridge-auth entry has no OpenID client import. Default Sandbox
and integration guidance use Bridge; old account UI mounts only when expanded.
Disabled SSO no longer maps its protocol controllers. Legacy implementations and
packages still have explicit consumers in source/examples/publication projection;
there is no evidence all deployed consumers have migrated. Removing these now
would break compatibility. See the audit for the dependency/removal plan.

## 5. APIs in use

- GET /api/miniapp/auth/config?clientId=...
- POST /api/miniapp/auth/launch: main signed user/source session
- POST /api/miniapp/auth/exchange: public client_id/session_id/code/code_verifier
- GET /api/miniapp/auth/me: Mini App Bearer + Origin + X-Ankt-MiniApp-Client
- POST /api/miniapp/auth/revoke: matching scoped token or authenticated source owner
- Owner GET /api/developer/mini-apps/{appId}/auth-bridge and POST child routes
  /launch and /revoke, forwarded through the existing authenticated/CSRF-protected BFF
- Existing ANKT account login/logout, Developer login, app/domain/version/review/
  publication/catalog APIs remain in service. Legacy OIDC routes remain only when
  explicitly enabled; they are not required by Bridge.

## 6–7. Database decisions

Keep Account/User, RefreshToken, DeveloperAccount/DeveloperSession, MiniApp,
versions, domain proofs, reviews, publications/operational policy, assets, audit and
SandboxIdentityTemplate. Add MiniAppBridgeSessions with hashed code/access token,
PKCE, origin/client/environment/source bindings and expiry/revocation.

OpenIddict*, AnktSsoSessions, MiniAppUserConsents and pairwise subjects are
advisory-deprecated for embedded Mini App login, pending consumer migration,
retention and rollback review. No DROP, live migration or data deletion performed.
Generated additive migration was applied only to disposable test PostgreSQL.

## 8–9. Sandbox Account/User and Auto Login

Actual database rows were created/reused in integration and browser fixtures.
Two real Developer BFF logins resolved the same Sandbox user, while each launch
had a separate session. Closing/reopening obtained a fresh session. Production
returned a distinct real user via the actual web host adapter and existing JWT.
These test databases are disposable; this does not claim a production Sandbox
account was provisioned. The deployment seeds/reuses it on its first authorized
Sandbox launch after configuration/migration.

SDK exposes init(), auth.getSession(), auth.getAccessToken(),
auth.getUserInfo(), auth.logout() and dispose(). Token renewal asks the trusted
host again; there is no refresh token or redirect login. getSession returns
metadata; getAccessToken exposes only the limited Mini App token to its app.

## 10. Verification

- Focused backend Bridge/Sandbox/SSO regression: 206 passed.
- Full backend suite: 938 passed, 0 failed, 1 skipped out of 939 (7m11s).
  MoneyAuditPostgresTests.ConcurrentWithdrawalsAndCompletionsCannotSettleTwice
  requires its separate money-audit test database; it was not configured.
- SDK final build/tests: 40 passed.
- Frontend final bridge/BFF/CSP tests: 7 passed; earlier related Sandbox set: 12 passed.
- TypeScript --noEmit and changed-file ESLint: passed.
- Android :ankt-mini-app-host:compileDebugKotlin --offline: BUILD SUCCESSFUL.
- Chrome E2E: passed real Developer login/shared Sandbox UserInfo, close/reopen,
  and Production web adapter with signed-in user. Real HTTPS, isolated PostgreSQL,
  site isolation enabled; no cross-site login cookies or OIDC. Verified main JWT
  absent from partner requests and code absent from URLs.
- Native device E2E: BLOCKED, adb reported zero devices/emulators.
- iOS build/device E2E: BLOCKED on Windows; existing iOS 27 permission guard remains.

Ignored local evidence: .codex-tmp/auth-bridge-browser-final.log,
auth-bridge-browser.png, auth-bridge-database.json (sanitized),
auth-bridge-sdk-final.log, auth-bridge-frontend-final.log,
auth-bridge-typecheck-final.log, auth-bridge-lint-final.log,
auth-bridge-android-build.log and test-results/auth-bridge/*.trx.

## 11. Security review and practical limits

Tests cover wrong source/origin/client (including same-origin apps), PKCE,
single-use/expired codes, atomic PostgreSQL exchange, source logout/revocation,
domain revocation, scope escalation, environment and token substitution.
Exact-origin/source messaging and platform main-frame checks remain mandatory.
No known critical issue was identified in the implemented/tested paths; native
runtime verification is still required before rollout.

Closing a frame sends best-effort revoke; network/process termination may prevent
delivery. Access TTL is five minutes and source/policy is revalidated on reads.
Domain proof changes/expiry/revocation are checked, but arbitrary DNS takeover
detection still depends on the existing verification window. Session retention
needs an approved operational policy; no production purge has been scheduled.
Partner writable data must separate Sandbox/session storage; a shared user id is
not an isolation key. Only profile data is exposed, never email/phone.

## 12–13. Configuration, deployment and Developer integration

Follow [the runbook](../operations/miniapp-auth-bridge.md) for exact environment
keys, endpoints, SDK example, iframe CSP and rollback. Configure backend
MiniAppAuth__Enabled, MiniAppAuth__HostOrigin and SandboxTest__Enabled, matching
frontend EXPO_PUBLIC_MINI_APP_AUTH_BRIDGE_ENABLED. Review the migration before
deploying; provision trusted HTTPS origins and verified partner domains. Native
also needs EXPO_PUBLIC_MINI_APP_NATIVE_RUNTIME_ENABLED and a rebuilt application.

Developers use their registered public AppId and the new /bridge-auth SDK entry;
no Client Secret, OAuth callback or certificate is needed. Allow framing by the
configured ANKT origin. Initialize the SDK inside the iframe/WebView, then read
UserInfo; stand-alone execution correctly reports HOST_UNAVAILABLE.

No production deployment, production configuration change, live database migration,
secret/certificate removal, commit or push was performed for this feature.
