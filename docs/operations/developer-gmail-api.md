# Gmail API on Render Free

This provider sends verification, password-reset and existing Developer review
notification emails through HTTPS port 443. No SMTP or Gmail App Password is
used. Existing account verification and session rules are unchanged.

## 1. Create Google credentials

1. Open [Google Cloud Console](https://console.cloud.google.com/), select/create
   a project and enable **Gmail API** under APIs & Services / Library.
2. Configure Google Auth Platform branding, audience and contact details. Add
   only `https://www.googleapis.com/auth/gmail.send` to Data Access. This permits
   sending mail; do not request inbox read/write or full Gmail access.
3. For initial testing of an External app, add the sending Gmail address as a
   test user. The recipients of transactional emails do not authorize Google.
4. Create an OAuth client of type **Web application**. Add this exact Authorized
   redirect URI for the one-time setup below:
   `https://developers.google.com/oauthplayground`.
5. Keep the client ID and client secret private for the backend configuration.

## 2. Authorize the sender once

1. Open [Google OAuth Playground](https://developers.google.com/oauthplayground/).
2. In settings (gear), select **Use your own OAuth credentials** and enter the
   client ID/secret from your project. Use your own client so the grant belongs
   to your project, not the Playground client.
3. Enter only `https://www.googleapis.com/auth/gmail.send` in the scope input.
   Authorize APIs, sign in as the Gmail account that will send emails and grant
   the send permission. Request offline access with consent (the Playground
   supports this); an access token alone is insufficient.
4. Exchange the authorization code for tokens. Copy the **refresh token** into
   Render's secret environment setting below. Never paste it into chat, commit
   it or put it in frontend configuration. If no refresh token is issued,
   revoke the prior project grant and repeat consent with offline access.
5. External OAuth projects in **Testing** usually issue refresh tokens that
   expire after seven days for Gmail scopes. Before relying on unattended mail,
   move to the appropriate production publishing status and complete Google's
   applicable consent/verification requirements. Production refresh tokens can
   still expire or be revoked; this is not a permanent credential guarantee.

## 3. Configure Render backend

Copy the keys from `docs/examples/developer-auth.gmail-api.env.example` into
the backend service's Environment, replacing all blanks:

```dotenv
DeveloperAuth__EmailProvider=GmailApi
DeveloperAuth__PortalUrl=https://YOUR_FRONTEND_HOST/developers
DeveloperAuth__GmailFrom=YOUR_SENDING_ACCOUNT@gmail.com
DeveloperAuth__GmailClientId=YOUR_OAUTH_CLIENT_ID
DeveloperAuth__GmailClientSecret=YOUR_OAUTH_CLIENT_SECRET
DeveloperAuth__GmailRefreshToken=YOUR_REFRESH_TOKEN
```

`GmailFrom` must be the authorized Gmail sender, or a send-as alias already
configured in that mailbox. `PortalUrl` must be the frontend URL, not the
backend Render URL, with path `/developers`, no query/hash. For local frontend
testing use its loopback origin, e.g. `http://localhost:8081/developers`.
Use exact provider value `GmailApi`; omitted provider defaults to `Smtp`.
SMTP settings and Gmail App Password are not required with this provider.
Keep an existing `DeveloperAuth__BffSharedSecret` matched to the frontend if
signed BFF ingress is enabled. Save and redeploy the backend with this code.

## 4. Verify and recover

1. Register with an address you control. Expect 202 and an actual email.
2. Open its verification link, verify the account and log in.
3. Test forgot-password email and password reset for that account.
4. A 503 `EMAIL_UNAVAILABLE` means missing provider configuration or an invalid
   PortalUrl. Check all four Gmail values and the frontend URL.
5. A 202 without email is not delivery proof: the existing identity flow records
   `EmailDeliveryFailed` and logs only the exception category when transport
   fails. Check Google API enablement, sender grant, gmail.send scope, current
   client credentials and refresh token; reauthorize if revoked/expired. Use
   resend verification after the 60-second cooldown, rather than registering
   the same account again. Google accepting mail does not guarantee inbox delivery.

The sender refreshes access on each delivery and never stores an access token
in the database. Google endpoints are fixed, redirects disabled, each HTTP
request has a 15-second timeout. Provider failures include only the operation
and HTTP status in exceptions; response bodies and secrets are excluded.
Do not enable HTTP body or Authorization header logging for this integration.

## References

- [Google message encoding/sending](https://developers.google.com/workspace/gmail/api/guides/sending)
- [Gmail scopes](https://developers.google.com/workspace/gmail/api/auth/scopes)
- [Offline access / token refresh](https://developers.google.com/identity/protocols/oauth2/web-server#offline)
- [Refresh token expiration](https://developers.google.com/identity/protocols/oauth2#expiration)
- [Render Free SMTP restriction](https://render.com/docs/free)
