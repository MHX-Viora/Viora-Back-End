# Developer registration email setup

For Gmail on Render Free, use [Gmail API setup](developer-gmail-api.md).
The SMTP sections below apply to `DeveloperAuth__EmailProvider=Smtp` (default).

## Symptom and cause

`POST /api/account/auth/register` forwards to `/api/developer/auth/register`.
`503 EMAIL_UNAVAILABLE` means `DeveloperIdentityService.RequireEmail` rejected
the email configuration before creating an account. Check the selected provider's settings
and `DeveloperAuth__PortalUrl`. An anonymous `auth/me` response of 401 is expected.

The checked local frontend `.env` selects a backend on Render. Changing the
local backend `.env` cannot configure that remote backend.

## Configure the backend actually used by the frontend

In Render, open the backend service's Environment settings. On VPS, supply the
same keys to the API container's runtime environment. Start with the
`DeveloperAuth__*` block in the root `.env.example`; keep credentials out of Git.

Required settings:

| Key | Value |
| --- | --- |
| `DeveloperAuth__PortalUrl` | Actual frontend origin plus `/developers`, e.g. `https://mxh.ankt.vn/developers` |
| `DeveloperAuth__SmtpHost` | SMTP server supplied by the email provider |
| `DeveloperAuth__SmtpPort` | Provider's TLS port: typically 587 for STARTTLS or 465 for implicit TLS |
| `DeveloperAuth__SmtpFrom` | Valid sender mailbox authorized by the provider |
| `DeveloperAuth__SmtpUser` | Provider's SMTP username, when authentication is required |
| `DeveloperAuth__SmtpPassword` | Provider's SMTP credential, entered directly in host settings |
| `DeveloperAuth__SmtpImplicitTls` | `false` for STARTTLS; `true` for implicit TLS |

`PortalUrl` must have no user info, query or fragment and its path must be
exactly `/developers` (a trailing slash is accepted). HTTP is accepted only for
loopback development. Use the frontend origin, not the backend Render origin.

If signed BFF ingress is configured, match `DeveloperAuth__BffSharedSecret` to
the frontend's `ANKT_BFF_SHARED_SECRET`. Leave
`DeveloperAuth__AllowInsecureLocalSmtp=false` for hosted environments.

Save the environment changes and restart/redeploy the backend. For local
backend use, put real values in the untracked root `.env` and restart `dotnet`.

## Gmail SMTP

Use `docs/examples/developer-auth.gmail.env.example`. Set the host to
`smtp.gmail.com`, port to `587`, and `SmtpImplicitTls=false` (STARTTLS).
Set `SmtpFrom` and `SmtpUser` to the same full Gmail address you control.
Enable Google 2-Step Verification and generate an App Password; put that
credential in `SmtpPassword`, not your normal Google password. Enter the
credential directly in the backend host's secret environment settings.

Render Free blocks outbound SMTP on ports 465 and 587. Gmail SMTP therefore
requires a backend host/plan permitting outbound SMTP. Changing the code or TLS
mode cannot remove this hosting restriction. Do not upgrade a paid plan without
the account owner's approval.

References: [Google SMTP](https://developers.google.com/workspace/gmail/imap/imap-smtp),
[Google App Passwords](https://support.google.com/mail/answer/185833),
[Render Free restrictions](https://render.com/docs/free).

## Verify

1. Register using an email address you control. The request should return 202.
2. Confirm the verification email arrives, open the link and verify the account.
3. Log in and confirm that `auth/me` returns the authenticated account.
4. If registration returns 202 but no email arrives, inspect backend logs for
   `Developer email delivery failed; category ...`. The current implementation
   catches transport errors, so 202 alone does not prove SMTP delivery works.
   Check credentials, sender authorization, TLS mode and outbound SMTP access.
5. After correcting delivery, use the resend-verification form (60-second
   cooldown). Repeating registration for an existing account does not resend.

Do not bypass email verification or mark pending accounts active to hide a
missing SMTP configuration. Do not log credentials or verification links.
