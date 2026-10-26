# Sandbox automatic login — 2026-10-26

Status: local implementation and verification complete. Shared account, protocol,
upgrade migration, API and isolation: 18/18 tests; final SSO regression: 176/176;
SDK: 37/37; Portal: 7/7; concurrent Chrome E2E passed. Full BE checkpoint:
914 passed / 3 pre-existing proxy-log failures / 1 skipped. Deployment/application
DB rollout remains unperformed; see `../handoffs/sandbox-auto-login.md`.

## Objective and acceptance
Implement the attached request using existing .NET 8/OpenIddict, EF Core and
Developer Portal. One persisted Sandbox Account/User supplies all developers;
Developer–Mini App mappings and individual expiring sessions isolate credentials.
Owner launch must complete code + S256 PKCE without a login or consent prompt.
Keep pairwise OIDC subjects per Mini App, while profile IDs come from the shared
database User. No production authentication, financial writes or notifications.

## Structure and style
Existing services: `Viora.Infrastructure/Developers/SandboxTestService.cs` and
`Viora.Infrastructure/Sso/`; controllers: `viora-BE/Sso/`; xUnit tests:
`Viora.Application.Tests/`; SDK and Portal: sibling `viora/`. Preserve DI,
typed records, DeveloperAuthException codes, exact origin checks and no-store.

## Ordered plan and verification
1. Regression tests: shared persisted account, idempotence, real UserInfo IDs,
   promptless owner authorization, production environment deny. Run focused xUnit.
2. Shared account mapping/index migration; reuse valid template, retain old rows,
   revoke sessions when rebinding legacy mappings. Test fresh and upgrade databases.
3. Enable automatic consent only for authenticated, bound Sandbox sessions.
   Production always disables SandboxTest; disabled APIs must not seed data.
   Existing SSO code/redirect/state/nonce/PKCE checks remain authoritative.
4. Verify real PostgreSQL migration and protocol, two developers, expiry/replay,
   production API denial, SDK tests and Chrome Portal E2E. Review focused diff.
5. Record actual IDs, asset HTTP checks, test totals and limitations in handoff.

## Commands
From viora-BE use `.codex-tmp/sdk8-check/sdk/dotnet.exe test
Viora.Application.Tests/Viora.Application.Tests.csproj --filter
FullyQualifiedName~SandboxTestIdentityTests --logger trx` and `dotnet build
tools/SsoE2EHost/SsoE2EHost.csproj`; browser: `node
tools/SandboxIdentityE2E/run-fixture.mjs`. SDK: `npm test --prefix
../viora/packages/miniapp-sdk`.

## Boundaries and decisions
- User explicitly authorizes required local/test migrations and implementation.
- Never migrate configured application/production DB, deploy, print secrets,
  create a second token system or bypass production consent.
- Keep refresh grant disabled under existing SSO policy; SDK reauthorizes through
  the still-valid Sandbox cookie when access token expires. Tokens stay in memory.
- Sandbox writes remain denied by production JWT authentication; no shared writable
  backend data is exposed. Partner apps must isolate their own test data.
- Existing working trees contain substantial user changes; preserve them and do
  not stage or commit unrelated work.
