# Sandbox test identity

The official Sandbox flow uses [Mini App Auth Bridge](miniapp-auth-bridge.md).
Follow [Sandbox automatic login](sandbox-auto-login.md) for setup and verification.

The shared database Account/User is provisioned idempotently with nickname
`ankt_sandbox_test`, Sandbox flags, profile fields and Cloudinary avatar/cover.
`SandboxIdentityTemplates` points to this real Account/User. No test password is
entered or transmitted to Mini Apps. Each developer launch receives an independent
owner-bound Bridge session.

The former Sandbox OIDC identity/session tables are retired by the reversible
[legacy store archive](miniapp-legacy-sso-cleanup.md); the shared template and
Accounts/Users are preserved. Retained legacy audit data is not deleted.
