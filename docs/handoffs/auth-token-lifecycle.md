# Handoff: Access/Refresh Token Lifecycle

## Root Cause

- The backend refresh token existed only in an HttpOnly cross-site cookie. This is appropriate for Web, but React Native cookie persistence/rotation was not a reliable credential store.
- The backend rotated its database row and cookie, while the client parsed and saved only `accessToken`. If native did not retain the rotated cookie, its next refresh sent the old/missing credential and received “Refresh token không hợp lệ hoặc đã hết hạn.”
- Every password/Google login inserted a new row. Rows had no session identity, refresh overwrote the current row, revoked/expired rows had no scheduled cleanup, and ordinary logout without a cookie revoked all account tokens.
- The client already had a useful single-flight coordinator, but forced one SignalR restart per waiting 401 and did not refresh during initial session hydration.

## New Lifecycle

1. Password or Google login creates a random 64-byte refresh token, hashes it with SHA-256, generates a server-side `SessionId`, and stores one active row.
2. Web receives the refresh token only as an HttpOnly/Secure cookie. Native opts into the response value with `X-ANKT-Refresh-Token: body` and stores it in Expo SecureStore.
3. Refresh accepts the optional JSON token first, then falls back to the cookie. The server validates hash, expiry, revocation, account state, and session.
4. In one transaction, a conditional update revokes the old row and links `ReplacedByTokenId`; a replacement row is inserted for the same session.
5. A concurrent rotation has one winner. Reuse of a rotated token revokes active tokens only in that session. Responses remain generic and logs contain no token/hash/header.
6. Client single-flight persists the new access token and native refresh token before retrying each original request once.
7. Startup/resume refreshes an expiring JWT before authenticated sync. Invalid refresh clears session and routes to Login; transient network failure does not destroy credentials.
8. SignalR hub factories read the latest token. No forced restart or realtime protocol change was added.

## Multi-device and Logout

- Each login gets an independent session; there is no unique `AccountId` constraint.
- The partial unique index permits at most one unrevoked token per non-null `SessionId`.
- Legacy rows keep `SessionId = NULL` and are never guessed/deduplicated by account.
- Ordinary logout sends the current refresh token and revokes only that token/session. Password-change behavior intentionally continues revoking all account sessions.

## Migration and Production Audit

Migration `20260911080533_HardenRefreshTokenLifecycle` only adds nullable columns and indexes. It does not mutate or delete existing rows and retains the legacy `ReplacedByTokenHash` column.

Run these read-only queries before deployment/cleanup:

```sql
SELECT
  COUNT(*) FILTER (WHERE "ExpiresAt" > NOW() AND "RevokedAt" IS NULL) AS active,
  COUNT(*) FILTER (WHERE "ExpiresAt" <= NOW()) AS expired,
  COUNT(*) FILTER (WHERE "RevokedAt" IS NOT NULL) AS revoked
FROM "RefreshTokens";

SELECT "AccountId", COUNT(*) AS active_count
FROM "RefreshTokens"
WHERE "ExpiresAt" > NOW() AND "RevokedAt" IS NULL
GROUP BY "AccountId"
HAVING COUNT(*) > 5
ORDER BY active_count DESC;

SELECT "SessionId", COUNT(*) AS active_count
FROM "RefreshTokens"
WHERE "SessionId" IS NOT NULL AND "RevokedAt" IS NULL
GROUP BY "SessionId"
HAVING COUNT(*) > 1;
```

The hosted cleanup runs every 24 hours and deletes only rows with `ExpiresAt` or `RevokedAt` older than `Jwt:RefreshTokenRetentionDays` (default 14; allowed 7–30). It never deletes a currently active token.

## Verification

- Backend lifecycle tests: login issuance, same-session rotation, second refresh, reuse detection, multi-session isolation, current-device logout.
- Backend full suite: 64/64 passed; solution build: 0 warnings/errors.
- Client full suite: 294/294 passed; includes five concurrent 401s sharing one refresh, both-token persistence, startup hydration, invalid-session clearing, Web/native storage split, and SignalR token-factory guards.
- Client TypeScript and Expo lint passed; `npm audit --omit=dev` found 0 vulnerabilities.
- EF migration SQL reviewed: only two nullable columns and four indexes; no data mutation/deletion.

## Deployment Notes

- Deploy migration/backend before the native client update.
- Existing native sessions self-migrate if their old cookie still exists: the next refresh returns a body token for SecureStore. A device that already lost both cookie and plaintext token cannot recover a one-way DB hash and needs a one-time login.
- Validate on staging with Android + Web sessions, forced short access lifetime, five concurrent protected calls, app restart, and active chat/call connections. No production database or physical-device E2E was used in this implementation session.

## Preserved

- JWT signing, issuer/audience, claims, access-token middleware, endpoint paths, CORS policy, chat/call/notification business logic, SignalR hubs/events, and password-change all-device revocation.
