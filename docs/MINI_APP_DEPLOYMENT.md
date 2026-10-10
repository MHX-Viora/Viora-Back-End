# Hybrid Mini App deployment guide

This guide describes the release procedure. The implementation task does not deploy or apply production migrations.

## Build and migration

1. Back up PostgreSQL and review the additive Mini App migration/SQL.
2. Run backend tests/build, client TypeScript/lint/policy tests, admin build/lint and partner example tests.
3. Validate EF pending-model changes and inspect migration SQL. Apply with your normal controlled deployment mechanism.
4. Deploy backend and hosted SDK before frontend features using the new contract.
5. Build a new Expo Android/iOS development binary for native dependencies and configuration-plugin changes. An old installed binary cannot gain WebView native patches from JavaScript alone.
6. Deploy admin/developer portal and Expo web bundle.

Typical commands from `viora-BE`:

```powershell
dotnet build viora-BE.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
# Synthetic design-time connection: these commands compare models/generate SQL,
# never open a database connection or load application credentials.
$env:VIORA_EF_DESIGN_CONNECTION = 'Host=127.0.0.1;Port=1;Database=mini_app_model_only;Username=mini_app_model_only;Timeout=1'
dotnet ef migrations has-pending-model-changes --project Viora.Infrastructure --startup-project viora-BE --context AppDbContext --no-build
dotnet ef migrations script 20261006094159_AddWithdrawalPercentageFee 20261009081149_AddHybridMiniAppPlatform --project Viora.Infrastructure --startup-project viora-BE --context AppDbContext --no-build --output .codex-tmp/mini-app-hybrid.sql
```

The reviewed migration is `20261009081149_AddHybridMiniAppPlatform`. The workspace SQL artifact is [mini-app-hybrid.sql](../.codex-tmp/mini-app-hybrid.sql); use a separate reviewed database connection for any actual deployment.

Inspect the SQL before applying it; it must preserve existing Developer/apps/grants/identities. Existing legacy apps retain SSO mode and existing status enum values. Legacy launch codes lack the new binding fields and cannot be redeemed by the upgraded exchange; they were short-lived and are intentionally not grandfathered into insecure redemption.

Legacy published websites remain listed/launchable, but trusted iframe/WebView origins are withheld until their exact hosts are verified. SSO authorization also requires verified callback ownership. Migrate those partners through domain verification before expecting embedded bridge/SSO access. Legacy pending apps must verify hosts before approval.

## Configuration

- Host API/admin/client on valid HTTPS origins. Configure normal ANKT API authentication/CORS without broadening other modules.
- Partner secrets stay server-side. Public clients use `None` client authentication and S256 PKCE.
- Serve `/mini-app-sdk/ankt-mini-app.js` as a static asset. Configure the partner's trusted ANKT iframe parent origin as specified by the SDK.
- Domain-verification egress must reach public TLS hosts and DNS; no private network/proxy exception is needed.
- Partners opt into frame-ancestors for ANKT web when desired. Sites denying embedding use the external-tab option.
- Keep callback URLs out of access logs, analytics and referrers; retain redacted event logs.
- Single-process API throttling is not distributed protection. Configure deployment-level rate limits when running multiple API instances.

## Check the deployed SSO contract

An HTTP 200 from the SDK endpoint alone does not confirm that the new SSO release is running.

- The deployed `/mini-app-sdk/ankt-mini-app.js` must expose version `2.0`, `configure`, and `requestLogin`.
- The request schema for `POST /api/mini-app-auth/exchange` must include `clientId`, `clientSecret`, `code`, `redirectUri`, `state`, and `codeVerifier`.
- Publish the full backend output, including `wwwroot`; rebuild/restart the deployed service or container and invalidate stale SDK caches. Verify the public endpoint after deployment, including all API replicas.
- Opening a Mini App creates a runtime session, not a partner login session. For automatic sign-in, the partner must start its server-side state/PKCE transaction when opened inside ANKT without an existing partner session, call `ANKT.requestLogin`, then redeem the callback code on its backend and issue its own session.
- Keep confirmation for sharing data when the required consent is absent. Do not retry automatically after consent denial or an SSO error; show an explicit retry action.

The Node example starts SSO from a button. It demonstrates the secure exchange but does not automatically sign users in when its home page opens. See [SSO deployment diagnosis](handoffs/mini-app-sso-deployment.md) for the production mismatch observed on 2026-10-10.

## Acceptance on staging

Register one independent-login app and one SSO app on real verified HTTPS hosts. Test old partner login, fresh ANKT login, linking/unlinking, state/PKCE/redirect mismatch, reused/expired codes, permission revoke and app/Developer suspension. Test web embedding allowed/blocked, external browser OAuth, downloads/uploads and both native permission behavior paths.

Run PostgreSQL redemption concurrency against a dedicated test database only. Never point a destructive integration fixture at an application database. Complete Android and iOS device verification, particularly the Android scoped media/file hardening, before describing the platform as production ready.

## Rollback

Prefer disabling/suspending Mini Apps and rolling back frontend/backend releases together. Keep migration data intact unless a database rollback is explicitly reviewed. Older backend releases do not enforce the new code-binding fields; do not enable legacy auth exchange merely to restore availability.
