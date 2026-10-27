# ANKT Mini App Auth Bridge

## Configuration

Deploy the additive `20261026021305_AddMiniAppAuthBridge` migration using the
normal approved database-change procedure. It creates MiniAppBridgeSessions;
the existing Accounts/Users, Sandbox identity and review/publication migrations
remain prerequisites. Do not run migrations against an unverified connection.

Backend, on the same existing Render service:

```dotenv
MiniAppAuth__Enabled=true
MiniAppAuth__AllowedHostOrigin=https://mxh.ankt.vn
SandboxTest__Enabled=true
```

Frontend build environment:

```dotenv
EXPO_PUBLIC_API_URL=https://viora-back-end-xmco.onrender.com
EXPO_PUBLIC_MINI_APP_AUTH_BRIDGE_ENABLED=true
```

Keep existing main JWT, database, Developer Portal BFF and registry/domain
encryption configuration. No new shared secret, signing certificate, issuer or
OAuth callback is required for Bridge. Local .env files are ignored by Git and do not update Render/Vercel Environment.
The current backend may remain ASPNETCORE_ENVIRONMENT=Production.

Legacy Mini App SSO/OIDC routes, discovery, consent and certificate loading have been removed.
See [Render configuration](miniapp-auth-bridge-render.md) for removable environment variables
and [database archive/rollback](miniapp-legacy-sso-cleanup.md) for retained legacy data.

## Developer integration

Register the HTTPS Website URL, verify the domain and use the registered App ID
as public Bridge clientId. Production requires an active approved publication;
Sandbox requires an authenticated active owner session. Initial permission is
profile only. No email/phone/KYC or production write permission is granted.

Build the local SDK package (`npm test` in viora/packages/miniapp-sdk) and distribute
it through the project's approved package channel. It remains private/unpublished;
the repository does not claim that a public npm release exists.

```ts
import {createAnktMiniApp} from '@ankt/miniapp-sdk/bridge-auth';

const ANKT = createAnktMiniApp({
  apiOrigin: 'https://viora-back-end-xmco.onrender.com',
  hostOrigin: 'https://mxh.ankt.vn',
  clientId: 'app_<registered-app-id>',
});

await ANKT.init();
const session = await ANKT.auth.getSession();
const user = await ANKT.auth.getUserInfo();
// user: id, username, displayName, avatarUrl, coverUrl, gender, isSandbox, fullName
// session: sessionId, environment, expiresAt, scope
await ANKT.auth.logout();
```

Use the actual 47-character App ID; the placeholder above is illustrative.
The SDK creates a fresh PKCE verifier in memory, handshakes with the parent/native
host, exchanges the one-time code and sends only its scoped Mini App token to the
configured API origin. It never receives the primary ANKT JWT. No localStorage,
redirect, popup or third-party login cookie is needed. Opening the website alone
returns HOST_UNAVAILABLE. Use the dedicated `/bridge-auth` package entry.

Code expires after 60 seconds; access after 5 minutes; launch after 15 minutes.
getSession/getUserInfo silently reauthorize via the still-authenticated Host when
the local access token expires. After source logout/revocation, open again from
ANKT; there is no long-lived refresh token or fallback test-user login.

For a partner backend, `await ANKT.auth.getAccessToken()` returns only the scoped
Mini App token. Keep it in memory and send it over HTTPS to your own trusted
backend. Validate it with ANKT `/api/miniapp/auth/me`, sending Bearer token,
Origin matching the registered website origin and **X-Ankt-MiniApp-Client set to
your own constant App ID**. Do not take the expected client from an untrusted
request; the API rejects tokens issued to another Mini App even on a shared origin.
Do not authenticate your backend merely from a browser-supplied user object.

Production username currently maps to the existing User.DisplayName because this
schema has no separate username column. Use id with client/environment for identity,
not the display label. Sandbox uses the persisted template nickname/full name.
Sandbox Account/User are shared; partner writable test data must use a distinct
(clientId, sessionId) partition or reject writes. ANKT exposes only read-only profile
access to these tokens; primary JWT APIs reject both kinds of Mini App token.

## Embedding and errors

Allow `frame-ancestors https://mxh.ankt.vn` on Mini App responses. Remove conflicting
X-Frame-Options DENY/SAMEORIGIN if the site is intended to embed cross-origin.
Allow the configured API in connect-src. The platform uses exact postMessage
targetOrigin, event.origin and event.source, and verifies domain/policy again on
exchange and UserInfo. Portal/Mini App route CSP allows HTTPS child sites; other
Developer routes retain frame-src none. CSP is defense in depth; verified target
selection and message/PKCE binding are the authorization boundary.

Sandbox states: Connecting, Authenticating, Connected, Failed, Session Expired.
If the handshake does not complete, the preview reports HOST_UNAVAILABLE with
SDK/CSP/X-Frame-Options guidance. SDK typed error codes include INVALID_CLIENT,
INVALID_ORIGIN, MINIAPP_NOT_APPROVED, SANDBOX_NOT_ALLOWED, SESSION_EXPIRED and
AUTH_EXCHANGE_FAILED. Do not log codes, verifier, access tokens or Authorization.
Closing a host triggers best-effort session revocation; expired/revoked source
sessions fail on every subsequent read. Abrupt network/browser termination cannot
guarantee delivery of a close request; short access lifetime still bounds exposure.

## Native

Build a new Android/iOS binary containing the local mini-app-host module; Expo Go
and old binaries cannot implement the new native mode. Enable both public flags:
EXPO_PUBLIC_MINI_APP_AUTH_BRIDGE_ENABLED and EXPO_PUBLIC_MINI_APP_NATIVE_RUNTIME_ENABLED.
Native direct mode validates committed navigation origin and main-frame metadata
using platform APIs. It refuses foreign-origin navigation, mixed content, popups,
permissions and TLS errors. It never injects the main account JWT into the WebView.

Android module compilation is verified locally. Native device E2E requires a real
test device/emulator and reachable trusted HTTPS test endpoints. iOS build/E2E is
not available on Windows; the existing module still requires iOS 27 for its
geolocation-denial guard, so older iOS versions remain unavailable rather than
silently relaxing permission controls.

## Rollout, rollback and legacy data

Deploy matching backend, Portal/SDK and rebuilt native binaries after the additive
Bridge migration. Verify both launch environments and logout/origin boundaries.
Follow [Render rollout](miniapp-auth-bridge-render.md) and the reversible
[legacy database archive](miniapp-legacy-sso-cleanup.md). Disabling Bridge makes
Mini App login unavailable; there is no fallback OIDC path in this revision.
Restore the prior revision/configuration and reverse the archive migration only
when deployment rollback is required. Physical data deletion needs separate approval.

## Test commands and references

Backend: .codex-tmp/sdk8-check/sdk/dotnet.exe test
Viora.Application.Tests/Viora.Application.Tests.csproj -m:1 -nr:false
-p:UseSharedCompilation=false. E2E: build tools/AuthBridgeE2EHost/AuthBridgeE2EHost.csproj to
.codex-tmp/auth-bridge-e2e and run node tools/AuthBridgeE2E/run-fixture.mjs.
All fixtures use independent local databases and never load the application .env.

Security primitives: [S256 PKCE](https://www.rfc-editor.org/info/rfc7636/),
[exact-origin postMessage](https://developer.mozilla.org/en-US/docs/Web/API/Window/postMessage),
[Android sourceOrigin/main-frame metadata](https://developer.android.com/reference/androidx/webkit/WebViewCompat.WebMessageListener).
