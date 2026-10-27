# Developer Gmail API handoff

## Result
- Added `GmailDeveloperEmailSender` behind existing `IDeveloperEmailSender`.
- `DeveloperAuth.EmailProvider=GmailApi` selects fixed HTTPS OAuth refresh and
  Gmail messages.send; omitted provider preserves SMTP. Existing verification,
  reset and review-notification consumers use the selected provider.
- MimeKit serializes UTF-8 MIME, base64url in JSON raw, Bearer header only.
  Redirects disabled; 15-second per-request timeout; provider errors exclude
  bodies/secrets. No access-token persistence, new dependencies or DB changes.
- Root/general env examples updated; Gmail API example and Google/Render runbook
  at `docs/operations/developer-gmail-api.md`.

## Validation
- RED: test project failed compilation for missing Gmail sender/options before
  implementation.
- GREEN: 33 tests passed, zero failed/skipped across Gmail sender, Developer
  identity/API/boundary and review outbox filter. Test command also built all
  backend/test projects successfully. `git diff --check` passed for modified
  tracked files. Full repository suite was not run.
- Coverage: DI default/selection, missing credentials, UTF-8 MIME/base64url,
  OAuth form and auth header, provider error secrecy, malformed token responses,
  cancellation and existing registration/session behavior.
- Local .NET workload notification checks stalled the initial CLI runs. Setting
  `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true` and
  `MSBUILDDISABLENODEREUSE=1` allowed serial builds/tests to complete.

## Remaining deployment prerequisites
Owner must enable Gmail API, grant gmail.send with their own OAuth client,
store sender/client ID/client secret/refresh token + actual PortalUrl on Render,
and deploy this code. No real Google grant, mail, Render changes, commit or push
performed. End-to-end inbox delivery is unverified without those credentials.
External Google OAuth Testing refresh tokens expire after seven days for Gmail
scopes; follow Google's production publishing requirements for unattended use.
Existing identity delivery failures remain neutral (202 plus audit/category log);
verify inbox delivery, then verification/login/reset. Resend after cooldown when
repairing failed delivery; duplicate registration does not resend.
