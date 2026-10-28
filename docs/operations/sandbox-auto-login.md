# Sandbox automatic login

Sandbox uses the same Mini App Auth Bridge as Production. Configure the existing
backend using [Render setup](miniapp-auth-bridge-render.md); no separate service,
login page, callback, consent screen or test-account password is required.

## Setup

1. Review/apply the additive Sandbox account and Bridge migrations to the verified
   database through the deployment process. Do not remove Accounts/Users or the
   shared SandboxIdentityTemplates row.
2. Set `MiniAppAuth__Enabled=true`,
   `MiniAppAuth__AllowedHostOrigin=https://mxh.ankt.vn`, and
   `SandboxTest__Enabled=true`. Preserve primary JWT, DeveloperAuth, Gmail and BFF
   configuration. The backend can remain in ASP.NET Production environment.
3. Deploy matching Portal/SDK. Register an HTTPS Website URL and verify its domain.
   The public client ID is the App ID; it also identifies Production launches.
4. Sign into Developer Portal, select the owned Mini App and open Sandbox.

The backend checks the active DeveloperSession and ownership before choosing the
persisted `ankt_sandbox_test` Account/User. Creation is idempotent under a shared
provisioning lock. It preserves linked Account/User, Cloudinary avatar/cover and
Sandbox flags. Each launch has its own developer-bound Bridge session; shared
profile identity does not share session authority.

## Integration

Use the actual private local SDK artifact; see
[Mini App integration](miniapp-auth-bridge.md). The SDK calls are:

```ts
await ANKT.init();
const session = await ANKT.auth.getSession();
const user = await ANKT.auth.getUserInfo();
```

The iframe/native host checks source and origin and receives a one-time code
bound to the SDK PKCE challenge. Exchange issues a scoped Sandbox token; the
primary ANKT JWT stays in the host. Production launches instead bind to the
signed-in real Account/User and an active approved publication. A request cannot
choose a Sandbox or Production identity by setting an environment field.

Codes expire after 60 seconds, access tokens after 5 minutes, launch sessions
after 15 minutes. Source logout/revocation invalidates later exchanges/reads.
Reopening Sandbox creates an independent session. The SDK keeps tokens in memory
and can reauthorize while its host session remains active.

## Verification and errors

Verify owner Sandbox launch, UserInfo (`isSandbox=true`, linked profile/images),
wrong-owner rejection, wrong-origin/client rejection, expired/replayed code,
revocation, primary JWT API rejection and Production token isolation. Browser and
native device checks must be reported as unrun when infrastructure is unavailable.

Connection states distinguish pending, connecting, connected and a specific
failure. HOST_UNAVAILABLE usually requires checking SDK installation, parent
embedding, `frame-ancestors`, X-Frame-Options and the configured exact host origin.
Never log code/verifier/access token values.

Legacy cookie sessions and consent stores are archived reversibly by
[the cleanup migration](miniapp-legacy-sso-cleanup.md); physical deletion is deferred.
