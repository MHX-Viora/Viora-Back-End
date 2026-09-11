# Spec: Access/Refresh Token Lifecycle
## Objective

Make access-token expiry transparent across ANKT native and Web clients. Rotate refresh tokens atomically per server-generated session, retain enough history to detect reuse, support concurrent device sessions, and remove old revoked/expired rows after a configurable retention period.

## Tech Stack and Commands

- Backend: ASP.NET Core 8, EF Core 8, PostgreSQL; `dotnet test viora-BE.sln --no-restore --maxcpucount:1`
- Client: Expo/React Native/Web, TypeScript; `npm test`, `npm run lint`
- Build: `dotnet build viora-BE.sln --no-restore --maxcpucount:1`; client typecheck via `npx tsc --noEmit`

## Existing Boundaries

- Preserve JWT HS256 signing, issuer/audience, claims, bearer middleware, endpoint URLs, and SignalR hub protocols.
- Preserve HttpOnly refresh cookie support for Web and older clients.
- Native refresh tokens use the existing Expo SecureStore adapter; Web does not persist refresh tokens in browser-readable storage.
- Login/Google login, refresh, and logout accept the existing calls while adding optional refresh-token body transport for native.

## Required Behavior

- Login returns access/refresh tokens, their expiry timestamps, and a server-generated session ID; only the refresh-token hash is stored.
- Each login creates an independent session. A session has at most one unrevoked refresh token.
- Refresh validates token, account, expiry, revocation, and session; it revokes the old row and inserts the replacement in one transaction.
- Concurrent refresh of one token has one winner. Reuse of a rotated token is rejected and revokes the replacement session chain.
- A scheduled backend cleanup deletes only revoked/expired rows older than retention; it never deletes active rows.
- Logout revokes only the supplied current-session refresh token. Password changes may continue revoking all account sessions.
- Client refresh is single-flight, stores both returned tokens on native, retries an original request once, and never intercepts the refresh request itself.
- Startup refreshes an expiring access token before authenticated sync/SignalR starts. Invalid refresh clears the local session; transient network errors do not.
- SignalR token factories continue reading the latest access token; any explicit realtime restart occurs once per successful refresh.

## Testing Strategy

- Backend unit tests cover issuance, session-preserving rotation, old-token rejection/reuse, independent sessions, and current-session logout.
- Repository behavior is protected by conditional update plus a PostgreSQL partial unique index and migration review.
- Client unit/contract tests cover single-flight, both-token persistence, one retry, startup refresh, invalid-session clearing, storage isolation, and SignalR token-provider behavior.
- Full backend/client tests, build/typecheck, lint, migration script generation, and diff review are release gates.

## Boundaries

- Always: hash refresh tokens, validate at the API boundary, use transactions, redact token material from logs, retain legacy rows safely.
- Ask first: changing JWT signing/claims, removing cookie transport, or deleting active production tokens.
- Never: store plaintext refresh tokens in the database or Web storage, log token/header/hash values, revoke all devices on ordinary logout, or rewrite realtime architecture.

## Success Criteria

- The 12 scenarios in the supplied audit request are covered by automated tests where the repository permits and documented manual/integration verification otherwise.
- No duplicate unrevoked token can exist for a non-null session ID.
- Existing legacy rows remain valid until expiry/revocation and are not grouped by account as one device.
