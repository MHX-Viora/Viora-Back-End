# Shared Sandbox Account/User with bound automatic login

Date: 2026-10-26. Status: implemented and verified locally; deployment pending.
Supersedes the separate-account and initial-consent decisions in
[sandbox-test-accounts.md](sandbox-test-accounts.md) under the new user request.

One marked Account/User backs the shared profile. Developer–Mini App identity
mappings share that AccountId; each launch retains its own developer source
session, client/version, one-time ticket, cookie credential and revocation state.
Existing pairwise subjects remain per Mini App. Partner storage must use the
validated issuer/client/sub, never the shared email or public profile id.

Only a valid bound Sandbox cookie may approve first consent automatically.
Explicit prompt=consent and ordinary Production consent retain existing behavior.
All hosts, including Production, require an explicit SandboxTest:Enabled opt-in.
Production requires HTTPS issuer and Portal origins. This replaces the original
environment-wide disable at the user's request; no staging service is required.
Disabled overview calls do not seed accounts. Existing code/S256 PKCE validation,
reference tokens, scopes and fresh session checks remain in OpenIddict/services.
Public Sandbox profile claims add the actual User id and isSandbox boolean.
Internal session identity is excluded from ID token and UserInfo.

The migration drops only the unique mapping AccountId index, preserves the unique
Developer/Mini App mapping, revokes old sessions and rebinds existing marked
accounts to the template. Old account rows remain for audit. Unsafe production
mappings abort migration. Downgrade refuses duplicate shared mappings.
IsVerified marks the sample User only; no KYC submission/email verification,
password, social session, wallet or production JWT is created.

Refresh grants stay disabled; use existing SDK authorization again while the
Sandbox session remains valid. This avoids a second credential mechanism.
Backend exposes read-only UserInfo to Sandbox tokens; production JWT APIs reject
them, including profile/financial/notification writes.

Protocol references: [OpenIddict PKCE](https://documentation.openiddict.com/configuration/proof-key-for-code-exchange),
[token storage/revocation](https://documentation.openiddict.com/configuration/token-storage.html),
[ASP.NET Core environments](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments?view=aspnetcore-8.0).
