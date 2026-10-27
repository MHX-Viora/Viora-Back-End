# ANKT Developer HTML email handoff

## Final behavior
- Verification and existing forgot-password emails now contain plain text plus
  HTML in multipart/alternative. Subject, token generation, 24h/30m lifetimes,
  single-use consumption, cooldown, fragment URL and verification API unchanged.
- Final user styling overrides: red #9D0000 replaces orange; no code icon/image;
  header text ANKT Developer; MIME From display name **ANKT**, configured mailbox
  unchanged (user's authorized sender mxh@baochi.online).
- HTML uses table layout, inline CSS, system fonts, 600px fluid container,
  Outlook conditional fixed-width wrapper, 54px CTA, fallback URL, Vietnamese
  copy, hidden preview, security notice and public portal copyright footer.
- Dynamic slots are HTML-encoded in one Regex pass; URL scheme validation allows
  HTTPS and loopback HTTP without changing the supplied URL. Names containing
  template markers cannot trigger recursive substitution. No email/token logging.
- No .env, OAuth credentials/endpoints, provider registration or new send triggers
  changed. Gmail still posts canonical base64url MIME to users/me/messages/send.

## Files

Application:
- `Viora.Application/Developers/DeveloperEmailContent.cs` — rich body record.
- `Viora.Application/Developers/DeveloperContracts.cs` — additive rich SendAsync;
  default delegates to text for compatibility with existing integrations.

Infrastructure:
- `Viora.Infrastructure/Developers/DeveloperEmailTemplates.cs` — reusable safe
  action-email model/renderer, verification/reset copy, embedded-layout cache.
- `Viora.Infrastructure/Developers/EmailTemplates/DeveloperActionEmail.html` —
  shared HTML presentation, separate from account business logic.
- `Viora.Infrastructure/Developers/DeveloperEmailMessage.cs` — shared MIME factory,
  authorized address preservation, display name, text/plain before text/html.
- `Viora.Infrastructure/Developers/DeveloperIdentityService.Tokens.cs` — replace
  body construction at the existing send point only.
- `Viora.Infrastructure/Developers/GmailDeveloperEmailSender.cs` and
  `SmtpDeveloperEmailSender.cs` — rich overload and shared MIME construction.
- `Viora.Infrastructure/Viora.Infrastructure.csproj` — embed layout in assembly.

Tests/tooling:
- `Viora.Application.Tests/DeveloperEmailTemplateTests.cs` — render, escaping,
  CTA/fallback equality, URI safety, colors/no icon/assets, MIME ordering/sender.
- `Viora.Application.Tests/GmailDeveloperEmailSenderTests.cs` — actual Gmail
  sender with fake HTTP Google endpoints, multipart decode/base64url and name.
- `Viora.Application.Tests/DeveloperIdentityTests.cs` — real SQLite registration,
  unchanged token links/lifetimes/send count and secret-free failure logging.
- `tools/DeveloperEmailPreview/DeveloperEmailPreview.csproj` and `Program.cs` —
  offline HTML/text/EML examples with synthetic tokens only; never loads .env.

Documentation: this handoff, `docs/specs/developer-email-html.md` and
`docs/plans/developer-email-html.md`. No commit or push was requested/performed.
Prior user exclusion of docs/Markdown/logs from commits remains in force.

## Actual verification
- Focused suite: **49 passed, 0 failed, 0 skipped** after final changes.
- Backend build: **0 errors, 0 warnings**. Preview utility build also succeeded.
- Full existing suite run before the last additional layout guard test:
  **873 total: 792 passed, 80 failed, 1 skipped** (2m26s).
  Isolated reproduction confirmed `IsolatedPostgres.Start`/cleanup fails at
  `pg_ctl` exit 1 before email/business logic. Another isolated reproduction
  confirmed `PublicWebFetcherTests` fixture X509 import fails with
  `CryptographicException: Access denied` at PublicWebFetcherTests.cs:173.
  These samples explain environment-dependent fixture failures; every one of
  the 80 failures was not separately diagnosed. Full-suite green is not claimed.
- Chromium: inspected actual rendered verification at desktop 1280px and mobile
  375px/320px; scrollWidth never exceeded viewport. Verified 120-character name
  at 320px, reset at 375px, 54px-tall CTA, no external resources and no image/icon.
  Screenshots visually inspected in browser tool. Browser tool denied saving
  screenshots to this workspace path, so no screenshot files are delivered.
- Rendered preview: `.codex-tmp/email-preview/verification.html`; matching .txt,
  .eml and reset/long-name examples generated from the actual renderer.
- `git diff --check` passed for tracked task files. No real email sent: owner
  explicitly chose to test sending later. Gmail API tests use fake transport,
  not Google credentials or a real mailbox.

## Reproduce preview

From backend root, set DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true and
MSBUILDDISABLENODEREUSE=1 if needed on this Windows machine.

```text
dotnet build tools/DeveloperEmailPreview/DeveloperEmailPreview.csproj --no-restore -m:1 --disable-build-servers /p:UseSharedCompilation=false
dotnet tools/DeveloperEmailPreview/bin/Debug/net8.0/DeveloperEmailPreview.dll
```

If first-time restore is needed, use `dotnet restore` with `-m:1` (parallel
MSBuild nodes failed silently in this sandbox). No packages were added.

## Remaining checks / extension
- Owner deploys backend and retries registration/resend. Check actual From name
  and authorized address, Vietnamese rendering, CTA/fallback including #token,
  verification then login. Test forgot-password similarly. No production config
  needs changing for this presentation update. Keep PortalUrl correct for the
  frontend used: localhost:8081 in local testing, public portal in production.
- Actual Gmail desktop/mobile, Outlook Windows/mobile, dark mode, inbox delivery,
  Gmail sender rewriting/contact labels and Outlook conditional behavior have not
  been verified. Browser responsive checks are not native email-client evidence.
- New event copy can render `DeveloperActionEmailTemplate` through the same layout
  and MIME pipeline: registration/reset success, Developer approval/rejection,
  Mini App decisions, security warnings. Add explicit named renderer methods and
  tests when business requirements are known; wire only existing approved events.
  Existing Mini App outbox event generation/delivery remains untouched/plain text.
  No success/approval/rejection/security sends were introduced by this change.
