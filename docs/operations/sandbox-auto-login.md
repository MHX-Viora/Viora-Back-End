# Sandbox automatic login

Use a separate non-Production backend/test database. The default is disabled and
`Production` hosts force it off even when configuration says true. This runbook
supersedes the earlier separate-account deployment instructions.

## Setup

1. Review migrations `20261024081450_AddSandboxTestIdentities` and
   `20261026012608_ShareSandboxTestAccount`. Back up the intended **test** database.
   Run the existing EF migration procedure only against that verified test target.
   Fresh installations use the same migration chain. Shared mappings prevent
   downgrade to a unique AccountId index; legacy accounts are preserved for audit.
2. Preserve existing SSO issuer, HTTPS signing/encryption certificates, BFF secret
   and Mini App encryption keys. Set `Sso__Enabled=true`,
   `SandboxTest__Enabled=true`, exact `SandboxTest__PortalOrigin` and a
   `SandboxTest__SessionMinutes` value from 1 to 15 on the test host.
   Set its actual environment to Development/Testing/Staging, never Production.
3. Deploy matching backend and Portal to the test environment. The first valid
   owner overview/launch idempotently creates the shared Account/User and template.
   Real Cloudinary URLs are already configured; missing assets fail readiness.
4. Configure an owned Sandbox OAuth client, exact HTTPS callback and verified
   website domain. Use a public client with the browser SDK; confidential clients
   exchange their secret only from their backend.

No application database migration or deployment is performed by the local tests.
Do not change a production service's environment to enable this feature.

## Developer flow

Đăng nhập Developer Portal → chọn Mini App → Sandbox → **Chạy thử Mini App**.
Mini App dùng Sandbox OAuth client để tự khởi chạy code + PKCE. Không nhập mật
khẩu hoặc đồng ý cấp quyền thủ công. **Mở lại Mini App** tạo phiên riêng;
**Dừng phiên** thu hồi token của phiên đó. UserInfo lấy profile từ database.

The existing SDK calls are `initialize`, `auth.login`, `auth.handleRedirect`,
`auth.getStatus` and `auth.getUser`; no duplicate getSession/getUserInfo API exists.
On a dedicated Sandbox entry/callback, reuse the configured SDK instance:

```ts
async function startSandboxMiniApp() {
  await sdk.initialize();
  const callback = new URL(window.location.href);
  if (callback.searchParams.has('code') || callback.searchParams.has('error')) {
    await sdk.auth.handleRedirect(callback);
    history.replaceState(null, '', callback.pathname);
  } else if (sdk.auth.getStatus() !== 'authenticated') {
    await sdk.auth.login({prompt: 'none'});
    return; // the browser is navigating to the issuer
  }
  const user = await sdk.auth.getUser();
  renderProfile(user); // sub, id, username, displayName, fullName, isSandbox, images
}
```

Handle errors visibly; do not loop silently on login_required. With an active
Sandbox cookie, repeat `auth.login({prompt:'none'})` after token expiry. After
15 minutes/source-session revocation, return to Portal for a new launch.
Refresh token grants remain unsupported by policy, and no refresh token is issued.
SDK tokens remain in memory; only temporary PKCE/state/nonce use sessionStorage.
Use the normal Production client/consent flow outside Sandbox. The Sandbox cookie
is client-bound and cannot authorize the Production client or JWT APIs.

## API and data boundaries

- Existing owner APIs under `/api/developer/mini-apps/{appId}/sandbox-test`:
  GET overview, POST `/sessions` with `{}`, DELETE `/sessions/{id}`.
- Owner APIs authenticate DeveloperSession; a production Bearer token is not a
  substitute. Launch POST validates exact Portal Origin and one-time ticket.
- `/connect/authorize`, `/connect/token`, `/connect/userinfo` remain the OIDC flow.
  `profile` releases the real shared User id and profile aliases; `openid` alone
  releases only sub. `email` releases the sample email with email_verified=false.
- Pairwise sub stays distinct per Mini App despite the shared profile id. Partners
  must key their accounts/data by validated issuer/client/sub. Private writable
  test data needs their own developer/app/session partition; the backend exposes
  no Sandbox write grant. Production profile/payments/notifications APIs reject
  Sandbox tokens through their separate JWT authentication.
- `IsVerified=true` is a sample User flag; no KYC record or verified email is forged.

## Local verification

From `viora-BE` with .NET 8 and local PostgreSQL binaries:

```powershell
.\.codex-tmp\sdk8-check\sdk\dotnet.exe test Viora.Application.Tests/Viora.Application.Tests.csproj --filter "FullyQualifiedName~SandboxTestIdentityTests|FullyQualifiedName~SandboxAutoLogin" -m:1 -nr:false -p:UseSharedCompilation=false
.\.codex-tmp\sdk8-check\sdk\dotnet.exe build tools/SsoE2EHost/SsoE2EHost.csproj -m:1 -nr:false -p:UseSharedCompilation=false
node tools/SandboxIdentityE2E/run-fixture.mjs
```

These commands create their own disposable local databases, never read `.env`,
and never contact the configured application DB. E2E uses real HTTPS/OIDC with
two local partner fixtures and Chrome. Database evidence goes to
`.codex-tmp/sandbox-auto-database.json` and the mobile screenshot to
`.codex-tmp/sandbox-identity-mobile.png`; all fixture databases are removed on exit.

## Troubleshooting

- Disabled/403: verify the non-Production environment and explicit feature flag.
- Missing assets: verify Cloudinary URLs return 200; do not simulate success.
- Readiness: check client enabled, exact callback and current domain verification.
- login_required: the client-bound cookie/session expired; launch again in Portal.
- Invalid token: check session/source revocation, expiry and client version.
- Test spawn EPERM/pg_ctl failure: rerun isolated tests with permission to launch
  local child processes; do not point tests at an existing database service.
