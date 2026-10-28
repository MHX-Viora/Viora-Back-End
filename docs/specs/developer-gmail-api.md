# Developer Gmail API

## Objective / acceptance
Send existing verification/reset/review notification emails through Gmail HTTPS
on Render Free. Select `DeveloperAuth__EmailProvider=GmailApi`; SMTP stays the
default. Keep IDeveloperEmailSender and account verification unchanged.
Missing credentials fail readiness; provider errors must not disclose tokens,
recipients or response bodies. Use only gmail.send and backend-owned credentials.

## Structure / style
Infrastructure/Developers owns the sender/options; DeveloperApi registers it.
Follow existing sealed classes, constructor injection and CancellationToken APIs:
`Task SendAsync(string recipient, string subject, string text, CancellationToken ct)`.
No new dependencies, schema, frontend or public OAuth callback endpoints.

## Commands / verification
From backend root:
`dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~GmailDeveloperEmailSenderTests|FullyQualifiedName~DeveloperIdentityTests|FullyQualifiedName~DeveloperApiTests"`
`dotnet build viora-BE/viora-BE.csproj --no-restore -m:1 --disable-build-servers`
Use xUnit fake HTTP transport for token exchange, RFC MIME/base64url, errors,
cancellation, readiness and DI selection; existing identity/API integration tests
guard registration. Real delivery requires owner's Google grant/host environment.

## Boundaries
Always use fixed HTTPS Google endpoints, no redirects, bounded HTTP timeout,
MIME encoding and secret-free errors. Never log/store credentials in source,
skip email verification, or send real mail during automated tests.
Owner sets Google consent/client credentials/refresh token privately on Render.
