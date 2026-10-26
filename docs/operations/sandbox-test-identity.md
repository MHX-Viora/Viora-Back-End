# ANKT Sandbox Test accounts

> Superseded by [Sandbox automatic login](sandbox-auto-login.md) on 2026-10-26.
> Current implementation requires a non-Production test host and shared Account/User;
> the earlier deployment/account guidance below is historical.

The Developer Portal can launch a Mini App with an isolated ANKT test account on
the existing SSO issuer. It does not require `sandbox.ankt.vn`, a new Render
service, or enabling `MiniAppSandbox`. The optional managed iframe/executor is
under **Kiểm thử tự động nâng cao**.

## Deploy

1. Back up the intended database and review
   `20261024081450_AddSandboxTestIdentities`. Apply this additive migration before
   deploying the updated backend: current Account queries require `IsSandbox`.
   The local implementation/tests have not migrated the application database.
2. Deploy matching BE and FE revisions. Preserve existing SSO signing/encryption
   certificates, issuer, Mini App encryption keys and BFF shared secret. Existing
   SSO HTTPS/reverse-proxy requirements still apply.
3. On the **backend Render service**, keep `Sso__Enabled=true` with its valid
   existing issuer/certificates and set:

```dotenv
SandboxTest__Enabled=true
SandboxTest__PortalOrigin=https://mxh.ankt.vn
SandboxTest__SessionMinutes=15
```

`PortalOrigin` defaults to the origin of `DeveloperAuth__PortalUrl`. If the same
backend is used by local Metro, explicitly allow that local portal as well:

```dotenv
SandboxTest__AdditionalPortalOrigins__0=http://localhost:8081
```

Only exact HTTPS origins, or explicit HTTP loopback origins, are allowed. No `*`.
The start endpoint checks the browser Origin. Changes require a BE restart.

4. On the **frontend build host**, configure `EXPO_PUBLIC_OPEN_API_ISSUER` to the
   same actual issuer and rebuild. This scopes the issuer POST permission to the
   owned-app Sandbox route while leaving `frame-src 'none'` unless the optional
   managed host was explicitly configured. Existing API/BFF settings remain.

## Images and initial account creation

Two non-personal SVG assets are packaged in `viora-BE/Developer/SandboxProfile`.
They were actually provisioned to Cloudinary under fixed IDs:
`ankt/sandbox/avatar-v1` and `ankt/sandbox/cover-v1`. Default options use their
real versioned HTTPS delivery URLs. Optional `SandboxTest__AvatarUrl` and
`SandboxTest__CoverUrl` overrides must be Cloudinary image/upload URLs.

To explicitly provision/reuse assets with the configured Cloudinary account:

```powershell
Push-Location viora-BE
dotnet run --no-launch-profile -- --provision-sandbox-profile ../.codex-tmp/sandbox-profile-assets.json
Pop-Location
```

The command looks up existing resources before upload, uses fixed IDs and
`Overwrite=false`, writes public URLs only, and never connects to a database.
It is not run during startup. On first owner access, the template Account/User
is created transactionally by EF SaveChanges; missing image URLs are reported as
`SANDBOX_ASSETS_MISSING`. Templates and scoped identities are reused thereafter.
No fabricated upload URL or synthetic token is used.

The template Account uses `sandbox@example.test`. Each Developer + Mini App gets
its own Account/User with a unique internal `sandbox+<random>@example.test` email
to preserve the existing unique email index. The exposed test email remains
`sandbox@example.test`, always `email_verified=false`. Accounts have no password,
external login, real verification, social refresh token or wallet. Full name,
nickname and birthdate use template metadata rather than fake KYC submissions.

## Developer workflow

1. Sign into ANKT Developer Portal and open an owned Mini App's **Sandbox**.
2. In **Đăng nhập ANKT**, enable SSO and its Sandbox client; register the exact
   HTTPS callback and verify the website domain. The test panel reports blockers.
3. Click **Chạy thử Mini App**. Allow the popup. The issuer consumes a one-time
   ticket submitted by POST, sets a client-specific secure test cookie, then
   opens the registered website. No social password is required.
4. The partner must automatically initiate its normal authorization-code + PKCE
   flow on entry. Keep consent when required. On callback, validate state, nonce,
   issuer/audience/signature, exchange code once and create the partner session.
5. Refresh **Kiểm tra trạng thái SSO**, or let the active panel poll. Server
   observations distinguish code issuance, token issuance and successful UserInfo.
   The panel does not equate configuration readiness with an actual login.
6. **Mở lại Mini App** issues a fresh session with the same pairwise `sub`.
   **Dừng phiên** invalidates that session's code/token/UserInfo authority.

An arbitrary website without an ANKT OAuth integration cannot be logged into by
the platform. See `viora/packages/miniapp-sdk` for the Public SDK and confidential
backend examples. External Mini App deployment/data mapping remains the partner's
responsibility; its Sandbox client should use its own test database.
Map partner accounts by the validated issuer/client/`sub`, never by the shared
sample email or by profile parameters posted by a browser. In particular,
`sandbox@example.test` is unverified and must not merge test accounts or link to
an existing production user.

## API and claims

Owner APIs, behind the existing Developer session/BFF/CSRF boundary:

| Method/path under `/api/developer/mini-apps/{appId}` | Purpose |
| --- | --- |
| `GET /sandbox-test` | Sample profile, OAuth readiness, callbacks, recent sanitized sessions |
| `POST /sandbox-test/sessions` with `{}` | New 60-second launch ticket and at most 15-minute test session |
| `DELETE /sandbox-test/sessions/{id}` | Revoke owned session |

`POST /sso/sandbox-test/start` is a top-level issuer form endpoint, not a BFF
passthrough. Existing `/connect/authorize`, `/connect/token` and
`/connect/userinfo` continue the real OpenIddict flow. Launch/runtime credentials
are stored as hashes. No caller-supplied account/environment/redirect is accepted.

| Scope | UserInfo fields |
| --- | --- |
| `openid` | `sub` (stable for Developer + Mini App; never the production Account ID) |
| `profile` | `name`, `preferred_username` (display name), `nickname`, `gender`, `birthdate`, `picture`, `ankt_cover_picture` |
| `email` | `email`, `email_verified` (false for the sample) |

`ankt_cover_picture` is an ANKT extension containing the HTTPS Cloudinary cover
image URL. Internal source/session bindings are not included in UserInfo or ID
tokens. Access tokens are opaque OpenIddict references scoped to the Sandbox
resource. They cannot authenticate to the production account JWT scheme.

## Isolation and rollback

Test Accounts/Users are excluded by default query filters; explicit production
JWT issuance rejects `IsSandbox`. Every exchange/UserInfo rechecks the live owner
Developer session, mapping, app/domain/client/version, expiry and revocation.
Sandbox clients cannot fall back to real-user identities, including when the
test feature is disabled. Production client authentication remains separate.

Disable with `SandboxTest__Enabled=false` and restart; this closes test access.
Retain the schema, accounts, keys and history. The migration Down refuses to drop
`IsSandbox` while marked accounts exist, preventing their conversion to ordinary
production accounts. Do not disable Account filters or delete production users
to work around a migration or eligibility error.

## Sources

[OpenIddict claim destinations](https://documentation.openiddict.com/configuration/claim-destinations)
explains explicitly releasing claims into tokens.
[Cloudinary upload rules](https://cloudinary.com/documentation/upload_images)
documents deterministic public IDs and preventing overwrites.
