# ANKT SSO and account linking

## Contract

Use SSO only for apps registered with `authenticationMode: "AnktSso"`. Register exact HTTPS callback URLs and origins and obtain admin approval for `identity.login`. This platform uses its own code-exchange API; it does not expose ANKT access tokens or an OIDC ID token to partners.

1. Your backend creates an unpredictable state, a PKCE verifier (43–128 allowed characters) and an S256 challenge. Bind these to a short-lived transaction in the partner's browser session, with explicit `login` or `link` intent.
2. Keep the verifier server-side for confidential web apps. Send state/challenge/registered callback to your frontend.
3. Call `ANKT.requestLogin({redirectUri, state, codeChallenge, codeChallengeMethod: "S256"})` inside the trusted runtime.
4. ANKT validates session, app/client status, registered callback and required grants, prompting the user for missing permissions.
5. The result is `{launchUrl, expiresIn}`. Validate its callback destination and navigate to it. It contains a one-time code and your state; code lifetime is 60 seconds.
6. Your backend rejects state not bound to this browser/intent, consumes its transaction once, and calls `POST /api/mini-app-auth/exchange`.

```json
{
  "clientId": "your-client-id",
  "clientSecret": "read-from-server-secret-store",
  "code": "code-from-callback",
  "redirectUri": "https://partner.example/auth/ankt/callback",
  "state": "original-state",
  "codeVerifier": "original-verifier"
}
```

Public clients configured with `clientAuthenticationMethod: "None"` omit `clientSecret`; S256 PKCE remains required. Confidential clients use `ClientSecretPost` plus PKCE. Do not use implicit grant. Callback comparison is exact; state and verifier cannot be substituted from a different transaction.

After exchange, immediately redirect to a clean URL and create your own session with a Secure, HttpOnly cookie and a suitable SameSite policy. Disable callback query-string logging; use `Cache-Control: no-store` and `Referrer-Policy: no-referrer`.

The examples use `SameSite=None; Secure` on HTTPS for cross-site iframe compatibility, retaining origin/CSRF checks for mutations. Local HTTP development uses Lax. A browser can still block third-party cookies; neither an iframe nor the SDK bypasses that restriction. Native top-document WebViews and external-tab login have different cookie contexts.

## Claims

The response contains `scopes`. `subject` is present only with `identity.login` and is stable for this ANKT user/client, distinct across clients. `profile.basic` permits display name/avatar; `profile.email` and `profile.phone` separately permit those fields. A grant is not business authorization inside your app.

Only `identity.login` is required to complete SSO. The first consent prompt can offer the app's approved optional profile claims; declining those claims still permits login and the exchange omits their values. Further optional grants require explicit `requestPermission` consent.

Use `(provider, subject)` as the local external identity key; for deployments supporting multiple ANKT clients also include the client/provider context. Do not use email as the identity key.

## Linking existing accounts

For a new user, create a local account and attach the verified subject. For an existing local account, require local login and explicit confirmation/reauthentication before starting an SSO transaction with `link` intent. Bind its local account ID and browser session to the transaction. Reject a subject already linked to another account and an account already linked to another subject. Unique constraints and a transaction enforce these rules under concurrency.

Unlink requires local reauthentication and confirmation. Delete the identity association, preserve the local account/business data and invalidate outstanding linking transactions. Refuse unlinking the last usable authentication method. Partner logout affects the partner session; ANKT logout and permission revoke do not automatically delete partner accounts or revoke independently issued partner sessions.

This follows the account-linking principles in [OWASP Authentication](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html#secure-federated-account-linking) and code/redirect/PKCE guidance in [RFC 9700](https://www.rfc-editor.org/rfc/rfc9700.html).

## Runnable examples

- [Node.js example](../examples/mini-app-node/server.mjs): Node 22 with built-in SQLite, own login, SSO transactions, confirmed linking/unlinking and rate limits.
- [ASP.NET Core example](../examples/mini-app-dotnet/Program.cs): .NET 8, EF SQLite, cookie auth, antiforgery, protected PKCE verifier and account identity service.
- [Example setup](../examples/README.md) explains configuration and test commands. No real secrets or accounts are seeded.

Before adapting either example for a public production service, add your deployment's identity recovery/MFA, distributed throttling, HTTPS/reverse-proxy setup, backups, monitoring and standard application migration process. Examples demonstrate integration, not a replacement for your production identity stack.
