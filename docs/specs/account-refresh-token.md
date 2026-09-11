# Spec: Account Refresh Token (Superseded)

> Superseded by [`auth-token-lifecycle.md`](auth-token-lifecycle.md). The newer spec adds native SecureStore transport, session-scoped rotation, reuse detection, retention cleanup, and startup/single-flight client behavior while retaining the HttpOnly Web cookie.

## Objective
Allow an active account to obtain a new access token without entering credentials again, while rotating refresh tokens to prevent replay.

## API Contract
- Login success writes the refresh cookie; native clients may opt into refresh-token JSON transport.
- `POST /api/accounts/refresh-token` accepts an optional JSON refresh token and falls back to the cookie.
- Success returns token expiry/session metadata and rotates both the row and cookie.
- Unknown, expired, revoked, replayed, or unavailable-account tokens return HTTP 401 with one generic Vietnamese message; malformed request bodies return HTTP 400 through API validation.

## Security and Storage
- Refresh tokens are opaque cryptographically-random values; only SHA-256 hashes are stored.
- The browser cookie is `HttpOnly`, `Secure`, `SameSite=None`, and scoped to account auth endpoints.
- Default lifetime is configured by `Jwt:RefreshTokenDays` (30 days).
- Every successful refresh revokes the submitted token and creates a replacement.
- Access JWT behavior, issuer, audience, signing algorithm, and claims remain unchanged.
- Authentication endpoints use the existing `auth` rate-limit policy.

## Structure and Style
- Contracts and orchestration: `Viora.Application/Accounts`.
- Cryptographic token creation: `Viora.Infrastructure/Security`.
- EF Core persistence and migration: `Viora.Infrastructure/Persistence`.
- HTTP boundary and validation: `viora-BE/Controllers`.
- Tests: xUnit in `Viora.Application.Tests`.

## Commands
- Test: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore`
- Build: `dotnet build viora-BE.sln --no-restore`

## Boundaries
- Always: validate input, rotate on use, store hashes only, reject non-active accounts.
- Ask first: changing JWT signing or removing the existing Web cookie transport.
- Never: log/persist raw refresh tokens or reveal why refresh failed.

## Success Criteria
- Web exposes only the access token in JSON; native receives both tokens and stores refresh in SecureStore.
- One valid refresh succeeds once, returns new tokens, and preserves the session ID.
- Reuse and invalid/expired refresh tokens return 401.
- Migration, tests, and build succeed.
