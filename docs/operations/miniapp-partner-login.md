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

New backend-only keys (Nts is an operator label, ClientId is the actual public AppId):

```text
MiniAppAuth__Partners__Nts__ClientId=app_<registered-id>
MiniAppAuth__Partners__Nts__SandboxSecretHash=<SHA256-HEX>
MiniAppAuth__Partners__Nts__ProductionSecretHash=<DIFFERENT-SHA256-HEX>
```

Generate two independent 32-byte random secrets using a local cryptographic tool.
Register their SHA-256 hex hashes under that existing app's exact public AppId in
ANKT's Render environment. Deliver each plaintext secret privately to the appropriate
partner backend. This is an operator registration; the Portal never returns it.
Hashing protects high-entropy random secrets; do not use human-chosen passwords.
An unset hash disables that environment's partner login with
PARTNER_LOGIN_NOT_CONFIGURED. Rotation replaces the hash and requires matching
partner config/restart; old credentials immediately stop validating sessions.

Partner backend-only configuration:
`ANKT_API_ORIGIN`, `ANKT_MINI_APP_CLIENT_ID`, `ANKT_MINI_APP_CLIENT_SECRET`,
`ANKT_MINI_APP_ENVIRONMENT=Sandbox|Production`. Never prefix the secret with
EXPO_PUBLIC or include it in frontend build variables. API Origin must be HTTPS.

1. Run focused backend tests and SDK build/tests; publish backend using existing
   Render .NET 8 build/start workflow. No new schema migration/dependency is needed
   beyond the already-required MiniAppBridgeSessions migration.
2. Register Sandbox hash only, configure partner Sandbox backend and verified domain.
   Deploy SDK 0.4.0 browser artifact and updated Portal/example; rebuild native ANKT
   only for changed host behavior. Keep exact CSP frame-ancestors/source checks.
3. Test Nghệ Thuật Số: open from Portal; confirm backend account creation/session and
   main screen. Reopen/refresh, expire/replay code, wrong secret, logout/switch account,
   offline Retry. Inspect network with credentials redacted. No code/token in URL/log.
4. Run same checks on Android WebView and iOS WKWebView, including reload, background,
   back/close and malicious subframes. Node transport tests do not replace devices.
5. Register production hash after live approval; point the production partner backend
   to its independent secret/database. Confirm Sandbox codes fail there. Monitor
   sanitized status/error counts and latency. Host/profile Bridge is limited to 60
   requests/minute/IP. Server exchange/validation has a separate IP bucket, default
   6000/minute, configurable by `MiniAppAuth__PartnerRequestsPerMinute` (1–60000).
   Size deployment capacity before rollout without bypassing validation or trusting cache.
6. Roll back by unsetting partner hashes and reverting partner/SDK rollout; profile
   Bridge and existing ANKT login remain available. No data purge or live changes
   were performed by this task.

## Sources

Atomic code consumption checks the affected-row count, following
[EF Core's concurrency guidance](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete#concurrency-control-and-transactions).
Native integration retains the existing main-frame channel described by the
[React Native WebView guide](https://github.com/react-native-webview/react-native-webview/blob/master/docs/Guide.md#communicating-between-js-and-native).
