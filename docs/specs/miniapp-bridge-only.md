# Mini App Bridge-only authentication

## Objective and boundaries
Retire Mini App SSO/OIDC throughout backend, Portal, SDK and examples. Preserve
ANKT JWT/RefreshToken, Developer authentication, Gmail, registry, domain proofs,
approval, permissions and audit. Bridge one-use codes, S256, scoped tokens and
source-session validation remain required. No deployment, live migration or
secret/certificate change is authorized. Prepare reversible schema cleanup only.

## Structure and conventions
Backend: Viora.{Application,Domain,Infrastructure}, viora-BE/Developer; xUnit in
Viora.Application.Tests. Frontend: features/developer, features/mini-apps and
packages/miniapp-sdk. Use existing typed records, DeveloperAuthException and EF
queries; for example `throw new DeveloperAuthException(401, "SESSION_EXPIRED", message);`.

## Ordered implementation and verification
1. Audit bridge and legacy consumers; retain primary auth and registry dependencies.
2. Test and extract social-session validation; reduce Sandbox identity to Bridge.
   Verify xUnit Bridge security and real Account/User identity behavior.
3. Migrate SDK, Portal, publishing and Sandbox consumers. Remove legacy runtime,
   endpoints, UI, types and packages. Verify SDK tests and frontend typecheck.
4. Remove unused EF mappings; create reversible archive migration, inspect existing
   tables in an isolated test cluster, test Up/Down without touching live data.
5. Run backend regression and real Chrome login/iframe/exchange/UserInfo E2E.
   Review security bindings and document exact results and platform limitations.

## Commands
Backend: `.codex-tmp/sdk8-check/sdk/dotnet.exe test Viora.Application.Tests --logger trx`.
SDK: `npm --prefix packages/miniapp-sdk test`.
Frontend: `npx tsc --noEmit`; targeted Node tests and changed-file ESLint.
Browser: `node tools/AuthBridgeE2E/run-fixture.mjs` with a built isolated fixture.

## Acceptance
No legacy Mini App login routes, OIDC tabs/forms, redirects, discovery or secrets
in the active integration. Sandbox uses one persisted test Account/User and
independent launch sessions; Production uses actual logged-in user. Same API and
Client ID can serve both with isolated tokens. Replay, expiry, origin/client/source
substitution, unauthorized developer, logout and suspended app fail closed.
Only tests actually executed may be reported as passing. Native device and iOS
verification must be reported separately when unavailable.
