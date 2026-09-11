# Account Refresh Token Handoff (Superseded)

> See [`auth-token-lifecycle.md`](auth-token-lifecycle.md) for the current implementation and rollout guidance.

## API
- Web login returns access metadata and sets an HttpOnly `refreshToken` cookie; native opts into both tokens in JSON.
- `POST /api/accounts/refresh-token` accepts optional JSON and falls back to that cookie.
- Success rotates a session-scoped database row and returns expiry/session metadata.
- Invalid, expired, revoked, replayed, or inactive-account token: HTTP 401 with a generic message.

## Storage and Security
- Apply migration `AddRefreshTokens` before deployment.
- Raw refresh tokens are never persisted by the backend; native stores them in SecureStore and the database stores only SHA-256 hashes.
- Rotation conditionally revokes the old row and inserts its replacement inside one transaction, so concurrent reuse has one winner.
- Configure `Jwt:RefreshTokenDays` / `Jwt__RefreshTokenDays`; default is 30 and values must be positive.
- The existing `auth` rate-limit policy covers login and refresh.

## Client Flow
Web sends credentials so the browser owns the cookie. Native sends the SecureStore token. The shared client single-flights refresh, persists both rotated credentials where applicable, retries once, and clears auth only for an invalid refresh.
