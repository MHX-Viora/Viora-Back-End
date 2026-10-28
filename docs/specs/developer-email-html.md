# ANKT Developer HTML email

## Scope and acceptance
Add a reusable, embedded table/inline-CSS HTML layout and Vietnamese verification
and password-reset content to the existing token email triggers. Use text branding,
red #9D0000, navy #101827, white; mobile fluid / Outlook 600px container,
CTA + exact fallback URL, safety notice, preview and platform footer.
Plain text and HTML must form multipart/alternative. Set From display name to
ANKT while preserving the configured authorized mailbox. Per final user direction,
remove the orange code icon and use #9D0000 throughout former orange accents.

## Architecture and boundaries
Application owns DeveloperEmailContent and an additive sender overload preserving
existing plain-text callers/test doubles. Infrastructure owns template renderer,
embedded HTML and common MIME factory used by Gmail/SMTP. No added email triggers,
OAuth changes, token generation/expiry/link or verification endpoint changes.
No real secrets or sensitive message logging. All dynamic HTML slots encoded in
a single pass; reject unsafe action URI schemes. No dependencies/assets/scripts.
Future approval/rejection/security/success content can use the same layout/model
only when the corresponding business event has an approved send trigger.

## Commands and verification
From backend root: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~DeveloperEmail|FullyQualifiedName~GmailDeveloperEmailSenderTests|FullyQualifiedName~DeveloperIdentityTests|FullyQualifiedName~DeveloperApiTests|FullyQualifiedName~MiniAppReviewOutboxTests"`.
Set DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true for this machine.
Build: `dotnet build viora-BE/viora-BE.csproj --no-restore -m:1 --disable-build-servers`.
xUnit covers render, escaping, CTA/fallback equality, MIME/base64url, provider
selection, failure logging and existing token behavior. Inspect synthetic-only
HTML at desktop/mobile sizes in Chromium. Actual Gmail/Outlook rendering/delivery
requires an accessible mailbox/owner test; report any unverified clients honestly.

## Conventions
Sealed records/classes; existing constructor injection, CancellationToken,
explicit MIME factory and embedded asset. Never alter .env or log content/tokens.
