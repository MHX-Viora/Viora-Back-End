# Mini App JavaScript SDK v2

Load the hosted `/mini-app-sdk/ankt-mini-app.js` from the ANKT API. Configure the exact ANKT web parent origin before accepting iframe handshakes:

```html
<script src="https://your-ankt-api.example/mini-app-sdk/ankt-mini-app.js"></script>
<script>
  ANKT.configure({ hostOrigin: 'https://your-ankt-app.example' });
  async function beginLogin(transactionFromYourBackend) {
    const result = await ANKT.requestLogin({
      redirectUri: transactionFromYourBackend.redirectUri,
      state: transactionFromYourBackend.state,
      codeChallenge: transactionFromYourBackend.codeChallenge,
      codeChallengeMethod: 'S256'
    });
    // Validate the registered callback destination before navigating.
    location.replace(result.launchUrl);
  }
</script>
```

The actual verifier and any client secret remain in your backend. Generate fresh state/challenge for each short-lived transaction; do not reuse a sample constant.

## Methods

| Method | Result / behavior |
|---|---|
| `getPlatformInfo()` | Runtime platform/version/capabilities |
| `getAppInfo()` | Public app identity/auth mode/published version |
| `requestLogin(params)` | Explicit SSO request with exact callback, state and S256 challenge; prompts for missing approved consent |
| `getGrantedPermissions()` | Currently granted scope codes |
| `requestPermission({permission})` | ANKT-owned consent for an approved scope, then current grant codes |
| `closeMiniApp()` | Close the active runtime session |

Only approved scopes can be requested. Ordinary independent mode does not support `requestLogin`. SDK responses do not contain ANKT access/refresh tokens or client secrets. Permission revocation is available in the app's ANKT detail page.

## Protocol and lifecycle

Host READY: `{type:'ANKT_MINI_APP_READY',appId,sessionId,nonce}`.

Page request: `{type:'ANKT_MINI_APP_REQUEST',appId,sessionId,nonce,id,method,params}`.

Host response: `{type:'ANKT_MINI_APP_RESPONSE',appId,sessionId,nonce,id,result}` or `{...,error:{code,message}}`.

The host checks document origin/source, app/session/nonce, payload schema/size, duplicate IDs, request rate and current backend session/grants. SDK accepts a web READY/response only from the configured parent origin and actual parent window. Native uses a trusted injected callback for READY/response and only enables it in the top trusted document. Navigation creates a new nonce; old requests/responses cannot cross that boundary. Pending calls time out rather than waiting indefinitely.

Treat app/session IDs and nonce as message bindings, not authentication credentials. Code exchange is authenticated independently. Native detection/bridge availability alone does not authenticate a caller.

## Web and OAuth limitations

The iframe must be permitted by the partner's CSP/frame headers. External-tab fallback does not provide a hidden bridge or automatic SSO. Partner cookies needed inside a cross-site iframe generally require `SameSite=None; Secure`; browser third-party-cookie policies may still block them. Test your real browser policy and provide your own supported top-level login path where needed.

Use the system browser for identity providers that prohibit embedded login. The host cannot promise generic OAuth callback/session transfer for arbitrary third-party websites.
