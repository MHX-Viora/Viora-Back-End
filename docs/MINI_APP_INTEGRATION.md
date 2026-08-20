# ANKT Mini App Integration

## 1. Register and review

1. Ask an ANKT admin to create/approve your Developer record and link its `AccountId`.
2. Call `POST /api/developer/mini-apps` with HTTPS `webUrl`, `callbackUrl`, `allowedDomains`, and requested permission codes.
3. Save the returned `clientId` and one-time `clientSecret` in a server-side secret store. ANKT stores only its hash.
4. Call `POST /api/developer/mini-apps/{id}/submit-review`. Only an admin can approve it.
5. Rotate a compromised secret with `POST /api/developer/mini-apps/{id}/rotate-secret`; the old secret stops working immediately.

Never put `clientSecret` in browser JavaScript, a mobile app, a URL, logs, or source control.

## 2. Launch and server-to-server exchange

ANKT opens your `callbackUrl` with a one-time `code` query value. The code contains no user data, expires after 60 seconds, and can be consumed once. Send it from the callback page to your own backend; your backend exchanges it with ANKT.

### ASP.NET Core

```csharp
var response = await http.PostAsJsonAsync("https://api.ankt.vn/api/mini-app-auth/exchange", new {
    clientId = configuration["ANKT:ClientId"],
    clientSecret = configuration["ANKT:ClientSecret"],
    code = launchCode
});
response.EnsureSuccessStatusCode();
var identity = await response.Content.ReadFromJsonAsync<AnktIdentity>();
// Find/create ExternalIdentity(provider: "ANKT", externalSubject: identity.Subject), then issue your session cookie.
```

### Node.js

```js
const response = await fetch(`${process.env.ANKT_API_URL}/api/mini-app-auth/exchange`, {
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify({
    clientId: process.env.ANKT_CLIENT_ID,
    clientSecret: process.env.ANKT_CLIENT_SECRET,
    code: req.body.code,
  }),
});
if (!response.ok) throw new Error("ANKT exchange failed");
const identity = await response.json();
// Link identity.subject to a local user and create an HttpOnly, Secure session cookie.
```

The response always contains `scopes`. A stable, pairwise `subject` is present only with `identity.login`; `displayName`/`avatarUrl`, `email`, and `phone` appear only when their corresponding scopes were approved and consented. Do not use ANKT `AccountId`; it is never exposed.

## 3. Browser SDK

Load `/mini-app-sdk/ankt-mini-app.js` from the deployed ANKT API or vendor the same version in your frontend:

```html
<script src="https://api.ankt.vn/mini-app-sdk/ankt-mini-app.js"></script>
<script>
  ANKT.ready();
  const theme = await ANKT.app.getTheme();
  const session = await ANKT.auth.getSessionStatus();
</script>
```

Available v1 methods: `app.getInfo`, `app.getTheme`, `app.close`, `app.openExternalUrl`, `device.getPlatform`, and `auth.getSessionStatus`. Events from native are `app.resume`, `app.pause`, and `theme.changed`. There is deliberately no JWT/access-token method.

## 4. Test checklist

- Callback and all WebView navigation use HTTPS and match an exact/wildcard allowed domain.
- A new permission forces new user consent.
- The same user receives the same subject in this Mini App; a different Mini App receives a different subject.
- Reusing or delaying a launch code fails; suspending the app/developer blocks new launches immediately.
- Partner session uses `HttpOnly`, `Secure`, appropriate `SameSite`, and no sensitive `localStorage` token.
