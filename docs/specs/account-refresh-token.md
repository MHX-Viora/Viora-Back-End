# Spec: Account Refresh Token

## Objective
Allow an active account to obtain a new access token without entering credentials again, while rotating refresh tokens to prevent replay.

## API Contract
- Login success returns `accessToken` in JSON and writes the refresh token to the `refreshToken` cookie.
- `POST /api/accounts/refresh-token` reads the refresh token from that cookie and has no request body.
- Success returns `{ "accessToken": "..." }` and rotates the cookie.
- Unknown, expired, revoked, replayed, or unavailable-account tokens return HTTP 401 with one generic Vietnamese message; malformed request bodies return HTTP 400 through API validation.

## Security and Storage
- Refresh tokens are opaque cryptographically-random values; only SHA-256 hashes are stored.
- The browser cookie is `HttpOnly`, `Secure`, `SameSite=Strict`, and scoped to the refresh endpoint.
- Default lifetime is configured by `Jwt:RefreshTokenDays` (30 days).
- Every successful refresh revokes the submitted token and creates a replacement.
- Access JWT behavior, issuer, audience, signing algorithm, and claims remain unchanged.
- Authentication endpoints use the existing `auth` rate-limit policy.

## Structure and Style
- Contracts and orchestration: `Viora.Application/Accounts`.
- Cryptographic token creation: `Viora.Infrastructure/Security`.
- EF Core persistence and migration: `Viora.Infrastructure/Persistence`.
- HTTP boundary and validation: `viora-BE/Controllers`.
- Tests: xUnit in `Viora.Infrastructure.Tests`.

## Commands
- Test: `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-restore`
- Build: `dotnet build viora-BE.sln --no-restore`

## Boundaries
- Always: validate input, rotate on use, store hashes only, reject non-active accounts.
- Ask first: supporting multiple refresh-token transport mechanisms or changing JWT signing.
- Never: log/persist raw refresh tokens or reveal why refresh failed.

## Success Criteria
- Login exposes only the access token in JSON and writes the refresh cookie.
- One valid refresh succeeds once, returns a new access token, and rotates the cookie.
- Reuse and invalid/expired refresh tokens return 401.
- Migration, tests, and build succeed.
