# ANKT Mini App integration

The platform supports two authentication modes. `Independent` opens the partner website and keeps its existing registration/login/session. `AnktSso` additionally offers explicit ANKT login; the partner still owns its user records and session cookies.

## Register and publish

1. Open Developer Portal from your personal profile settings in ANKT (`/developer`), register a Developer profile using the current ANKT account and await approval.
2. Create a Draft in the five-step wizard. Choose authentication mode, HTTPS website, exact origins/callbacks and requested permissions.
3. Save the one-time client secret on the partner server when using `ClientSecretPost`.
4. Publish each ownership challenge at `https://<host>/.well-known/ankt-mini-app-verification.txt`, then verify through the portal.
5. Submit a version for review. Admin approval publishes its immutable snapshot. Later edits require another review.

See the [Developer guide](MINI_APP_DEVELOPER_GUIDE.md) and [admin workflow](MINI_APP_ADMIN_WORKFLOW.md).

## Launch and SSO

Launch creates a host-private runtime session and opens `webUrl`; it does not log the partner user in. Independent apps use their own login immediately.

For SSO, the partner server starts a browser-session-bound transaction with random state and a PKCE verifier. The embedded page calls:

```javascript
ANKT.configure({ hostOrigin: 'https://your-ankt-web-host.example' });
await ANKT.ready();
const result = await ANKT.requestLogin({
  redirectUri: transaction.redirectUri,
  state: transaction.state,
  codeChallenge: transaction.codeChallenge,
  codeChallengeMethod: 'S256',
});
window.location.assign(result.launchUrl);
```

The callback validates state and its initiating browser session. Only the partner server exchanges the code with `POST /api/mini-app-auth/exchange`, supplying `clientId`, secret when required, code, exact `redirectUri`, state and `codeVerifier`. Codes expire after 60 seconds and are consumed once. An old v1 exchange without these binding fields is unsupported.

The returned pairwise `subject` requires `identity.login`. Create/find the partner identity by provider/client/subject, then issue a partner session. Linking to an existing account requires its authenticated login, reauthentication and explicit confirmation. Never match email automatically. Unlinking must preserve data and leave a usable login method.

Runnable [Node and ASP.NET examples](../examples/README.md) implement independent login, new SSO account, explicit linking, conflict handling and unlinking. Read the full [SSO contract](MINI_APP_SSO.md) before adapting them.

## Runtime and SDK

The hosted SDK is `/mini-app-sdk/ankt-mini-app.js`. Its six methods are `getPlatformInfo`, `getAppInfo`, `requestLogin`, `getGrantedPermissions`, `requestPermission` and `closeMiniApp`; see [SDK documentation](MINI_APP_SDK.md).

Native uses an isolated WebView in a rebuilt ANKT binary. Web uses a sandboxed cross-origin iframe; sites blocking embedding or OAuth providers requiring their own browser use the external-tab option. Browser cookie restrictions still apply. ANKT passwords, bearer tokens, runtime secrets and client secrets never enter partner JavaScript.

See [security](MINI_APP_SECURITY.md), [testing](MINI_APP_TESTING.md), and [deployment](MINI_APP_DEPLOYMENT.md) for acceptance requirements and remaining verification.
