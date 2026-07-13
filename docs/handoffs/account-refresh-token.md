# Account Refresh Token Handoff

## API
- Login success returns only `accessToken` in JSON and sets an HttpOnly `refreshToken` cookie.
- `POST /api/accounts/refresh-token` has no body and reads that cookie.
- Success: HTTP 200 with `accessToken` and a rotated cookie.
- Invalid, expired, revoked, replayed, or inactive-account token: HTTP 401 with a generic message.

## Storage and Security
- Apply migration `AddRefreshTokens` before deployment.
- Raw refresh tokens are returned once and never persisted; the database stores SHA-256 hashes.
- Rotation uses a conditional database update inside a transaction, so concurrent reuse has one winner.
- Configure `Jwt:RefreshTokenDays` / `Jwt__RefreshTokenDays`; default is 30 and values must be positive.
- The existing `auth` rate-limit policy covers login and refresh.

## Client Flow
Call the refresh endpoint with credentials enabled so the browser sends the cookie. Replace the local access token with the response and force login on HTTP 401; JavaScript never reads the refresh token.
