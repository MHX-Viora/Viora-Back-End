# ANKT partner login handoff — 2026-10-26

Implemented partner login on the existing PKCE profile Bridge. SDK 0.4.0 exposes
auth.getLoginCode, calling /api/miniapp/auth/code using its scoped token. An opaque
60-second code is stored hashed, bound to the parent Bridge/source session and
registered app/environment; partner/exchange consumes it atomically using a
backend-only app/environment secret. partner/session rechecks source, account,
domain/publication, expiry and credential validity. No OIDC restoration or main
JWT exposure. Partner grants use the existing table, so no new migration.

Credential registration supports safe labels: MiniAppAuth__Partners__Nts__ClientId
and environment SecretHash keys. Duplicate labels for one client fail closed.
Host/profile and server calls use separate rate-limit IP buckets (60 and default
6000/minute); server capacity is configurable with PartnerRequestsPerMinute.

Production web/native hosts compare stable source/user IDs and close on account
replacement/session invalidation. A one-second check also detects storage changes;
revocation during network loss remains best effort, so backend validation is required.
All errors remain bounded and Retry is explicit.

Files: Viora.Application/Developers/MiniAppBridgeContracts.cs;
Viora.Infrastructure/Developers/{MiniAppBridgeService,MiniAppPartnerLoginService}.cs;
viora-BE/Developer/MiniAppAuthBridgeApi.cs; focused MiniAppPartnerLoginTests and
MiniAppAuthBridgeTests; SDK host-auth/config/package version/artifacts/tests;
frontend auth-host source guard/adapters, Portal snippet and integration docs.
Tools/AuthBridgeE2E* add an isolated integration with the actual Nghệ Thuật Số
gateway/account/role services, frontend AuthProvider and role screen.

See [protocol and rollout](../operations/miniapp-partner-login.md) and the complete
Vietnamese user checklist at Digital-Creative/docs/handoffs/ankt-auto-login.md.
No live deployment/database migration/secret change, commit or push performed.

Verification: focused ANKT Bridge/partner/revocation 33 tests; Nghệ Thuật Số focused
gateway/login/session/role/Google/accounts 89 tests; its frontend ANKT/onboarding 29 tests; SDK 34
tests; host/source/native transport 8; server adapter example 5. TypeScript and web
export passed. Chrome isolated PostgreSQL integration covers concurrent account
creation, both roles, reopen, iframe refresh and another browser context. Device
Android/iOS and live rollout remain user checks in the Vietnamese handoff.

Resume review strengthened protocol separation using the actual partner grant ID,
bounded rapid repeated expiry renewals, and required a DOM for Mini App runtime
detection so standalone React Native keeps its original authentication path.
