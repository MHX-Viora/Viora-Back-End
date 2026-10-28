# Mini App partner login — 2026-10-26

## Root cause and flow
`init/getUserInfo` previously established ANKT profile access only. They did not
authenticate a partner backend or create an account/session in that backend.
SDK 0.4.0 adds `auth.getLoginCode()`. It initializes the existing secure Bridge,
then POSTs `/api/miniapp/auth/code` using its scoped Mini App token and exact Origin.
Returns `{ "code": "43-character-random-value", "expiresIn": 60 }`.
The ANKT main access token stays in the trusted host; partner credentials stay on servers.

Mini App sends only the code to its backend. That backend calls:

```http
POST /api/miniapp/auth/partner/exchange
Authorization: Basic base64(registeredAppId:environmentSpecificSecret)
Content-Type: application/json

{"code":"one-use-code"}
```

This HTTPS endpoint rejects requests with Origin headers. Do not proxy browser
Authorization/Origin to it. No query strings, code logging or request-body logging.
The response is:

```json
{
  "user": {"id":"ankt-user-uuid","displayName":"Nguyen Van A","gender":"Male"},
  "miniAppId":"app_registered-public-id",
  "environment":"Sandbox",
  "sessionId":"partner-grant-uuid",
  "expiresAt":"2026-10-26T12:15:00Z"
}
```

Avatar/cover are omitted when null. Identity includes profile fields only; email,
phone, main tokens and secrets are never returned. Environment comes from the
authenticated source grant, not request parameters. Sandbox identity is the
existing configured persisted test Account/User; production requires a live
approved publication and domain proof.

Backend upserts by `(miniAppId, environment, user.id)` with a unique database
constraint, then issues its own opaque session token or secure HttpOnly cookie.
Keep Sandbox and Production databases/session namespaces separate. Production
backend must require `environment === 'Production'` in the verified response.
The same Client ID uses different secrets for each environment.

Use [the server adapter](../examples/miniapp-partner-backend.mjs) with your existing
account repository and session store. Implement `accounts.findOrCreate(identity)`
atomically and `sessions.create/get/revoke` using durable storage. Store only hashes
of partner bearer tokens. `sessions.create` returns your existing login response;
its expiry must not exceed the ANKT grant expiry. Never key accounts by display name.
Mount `auth.login(body.code)` behind `POST /api/auth/ankt`; protect cookie-based
login against login CSRF and pin its allowed Origin to your own website.

## Frontend TypeScript integration

```typescript
import {createAnktMiniApp} from '@ankt/miniapp-sdk/bridge-auth';
const ANKT = createAnktMiniApp({clientId: 'app_your_registered_client_id'});
let partnerToken: string | undefined;
async function login() {
  partnerToken = undefined; // Never retain another host/account's session.
  const {code} = await ANKT.auth.getLoginCode();
  const response = await fetch('/api/auth/ankt', {
    method: 'POST', credentials: 'same-origin', redirect: 'error',
    signal: AbortSignal.timeout(10000),
    headers: {'Content-Type': 'application/json'}, body: JSON.stringify({code}),
  });
  if (!response.ok) throw new Error('PARTNER_LOGIN_FAILED');
  const session = await response.json();
  if (typeof session.token !== 'string') throw new Error('INVALID_PARTNER_SESSION');
  partnerToken = session.token;
  document.dispatchEvent(new CustomEvent('ankt:partner-login', {detail: session}));
}
// Call once when mounted. Catch errors in your UI; Retry invokes login explicitly.
void login().catch(() => document.dispatchEvent(new Event('ankt:login-error')));
window.addEventListener('pagehide', () => {partnerToken=undefined; ANKT.dispose();});
```

Handle `ankt:partner-login` in the partner router to enter its main screen. The
Portal also provides a copyable plain JavaScript example. Standalone websites
correctly fail HOST_UNAVAILABLE. Do not put one-use codes in URLs or storage.

## Session lifecycle

For every protected backend request, before restoring the partner session call:

```http
POST /api/miniapp/auth/partner/session
Authorization: Basic base64(registeredAppId:environmentSpecificSecret)
Content-Type: application/json

{"sessionId":"previously-verified-partner-grant-uuid"}
```

Validate returned app/environment/user against stored session metadata. No identity
means no access. Main-session expiry/logout, app/domain suspension, host revocation,
grant expiry and credential rotation invalidate this check. Partner grants last
at most the parent Bridge launch (15 minutes); code expiry is 60 seconds. The
profile token's five-minute renewal does not extend existing partner grants.
On partner expiry, ask for one fresh code; surface network/config failures and
offer explicit Retry. Do not retry code exchange: it may already be consumed.
On ambiguous network failure, acquire a fresh code on the next explicit attempt.

On reopen/refresh, initialize ANKT and establish current identity before showing
cached partner UI. Restore only after validating both current ANKT identity and
stored partner identity/environment; otherwise perform fresh code login. The
example deliberately starts with a fresh grant each mount. The backend may reuse
its account row while issuing a new partner session. ANKT production hosts close
on session invalidation and detect source/account replacement by checking stable
session/user IDs every second. Host revocation is best effort during network loss;
backend validation and expiry remain mandatory. Never accept cached identity offline.

## Configuration and Render rollout

Existing: `MiniAppAuth__Enabled=true`, `MiniAppAuth__AllowedHostOrigin` (exact HTTPS),
`SandboxTest__Enabled=true`, existing SandboxTest identity configuration,
`EXPO_PUBLIC_MINI_APP_AUTH_BRIDGE_ENABLED=true`; native runtime flag as currently
configured. Keep current ANKT JWT/refresh settings and disabled legacy SSO unchanged.

Credentials are now self-service and database-backed. The owner opens the Mini App
integration page, selects Sandbox/Production and copies its generated Client Secret
into the partner backend. Adding a Mini App requires no app-specific ANKT env keys.
Rotation/revocation are performed in that page, not by changing environment hashes.

For existing env registrations, follow the explicit one-time import in
[database credential rollout](miniapp-database-credentials.md) before removing old
`MiniAppAuth__Partners__*` keys. No runtime env fallback remains.

## Sources

Atomic code consumption checks the affected-row count, following
[EF Core's concurrency guidance](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete#concurrency-control-and-transactions).
Native integration retains the existing main-frame channel described by the
[React Native WebView guide](https://github.com/react-native-webview/react-native-webview/blob/master/docs/Guide.md#communicating-between-js-and-native).
