# Mini App SSO retirement dependency and security audit

Scope: backend, Portal/BFF, Web/native host adapters, SDK, tests, EF and deployment
examples. Source references were audited; production telemetry, partner deployment
inventory and the live database were not accessed. This implements the user's
explicit source retirement request. No production deployment or live migration.

| Area | Decision and dependency |
| --- | --- |
| Primary JWT/RefreshToken, Developer auth, Gmail | Keep. Source session checks extracted into MiniAppHostSessionValidator; no alternate login credential created. |
| Mini App registry, domain verification, approval/publication, permissions/audit | Keep. Bridge uses AppId and verified/sealed target; edits and revocations invalidate policy. |
| MiniAppBridgeSession/code/S256/exchange/scoped tokens | Keep. Unrelated to removed OIDC authorization grants. |
| SandboxIdentityTemplate/Account/User | Keep one persisted test identity, separate launch sessions. Removed old SandboxIdentitySession redirect/cookie authentication. |
| OpenIddict registrations/packages, Sso controllers/services/options/views, consent/discovery/connect routes | Remove. No remaining runtime authorization-server consumer. |
| Portal OIDC tabs/forms/callback state/BFF calls | Remove; seven tabs remain. Version editor uses AppVersion and immutable metadata snapshot. |
| SDK OIDC auth/popup/discovery/client-secret modules and examples | Remove; root and bridge-auth entries expose host-based init/session/UserInfo. Private local package instructions only. |
| Managed Sandbox diagnostics/production host infrastructure | Keep generic runtime/domain/review/audit functions; remove legacy protocol hooks and popup permissions. |
| Historical snapshots/migrations | Keep for immutable hash and database upgrade compatibility; new snapshots omit legacy configuration. |
| Legacy persisted records | Reversible schema archive; no DROP or row deletion. See exact inventory in cleanup runbook. |

## Security boundaries

Codes and tokens use 32 random bytes and SHA256 server-side hashes. S256 is mandatory.
Exchange binds code/session/client/origin and consumes atomically. Tokens use separate
Sandbox/Production prefixes and cannot authenticate primary JWT routes. UserInfo
requires the expected client header and exact registered origin, even for same-origin
Mini Apps. Neither AppId nor Origin proves user identity; source sessions do.

Every access rechecks active source account/session, approved publication, current
operational policy and domain proof. Sandbox checks developer ownership, source session
and persisted test account status. Logout, expiry, app deletion/suspension and domain
revocation fail closed. Publishing/moderation revoke affected Bridge sessions inside
the command transaction. Domain changes also invalidate policy bindings.

Web requires exact event.source/event.origin and reply targetOrigin. Native adapters
retain current-URL, platform-origin and main-frame validation. Main JWT/code is absent
from partner URL/query and cross-frame messages; Mini App token is memory-only.
No refresh-token, consent, callback, redirect or client-secret fallback is provided.

## Practical limits

A partner backend must validate the scoped token using its own constant AppId and
partition Sandbox writes by client/session. Shared sample user ID is not isolation.
Unexpected browser shutdown can prevent best-effort revoke; short TTL and source/policy
checks remain. Domain takeover detection depends on proof validity/reverification.
Android device and iOS device E2E remain separate release requirements when hardware
is unavailable. Actual executed tests and evidence are recorded in the handoff.
