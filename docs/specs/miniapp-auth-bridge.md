# ANKT Mini App Auth Bridge

Partner backend sessions now extend this profile Bridge through
[one-use partner login](../operations/miniapp-partner-login.md), SDK 0.4.0
auth.getLoginCode and authenticated server-to-server exchange/validation.

The official Mini App authentication path is the Host Auth Bridge. Requirements,
ordered implementation and verification are in [Bridge-only spec](miniapp-bridge-only.md).
The previous opt-in legacy OIDC compatibility decision is superseded by this
explicit retirement request. Preserve historical version snapshots and migration
history; do not expose the old protocol or credentials.

A signed-in ANKT user or authenticated Developer owner launches an approved/verified
Mini App. The host provides a one-use code bound to the public AppId, exact partner
origin, source session, environment, domain/publication policy and S256 challenge.
The SDK exchanges it for an opaque, environment-specific Mini App token kept in
memory. Main ANKT JWT credentials remain only on the trusted host/API connection.
Every access rechecks source session, app policy and domain proof. Initial scope
is profile only. Code/access/launch TTL: 60 seconds / 5 minutes / 15 minutes.

Sandbox reads the persisted SandboxIdentityTemplate Account/User and uses separate
sessions per Developer launch. Production reads the actual signed-in Account/User
and requires a live approved publication. Environment is chosen by the authenticated
host route, never a partner-supplied environment or user identifier.

See [integration guide](../operations/miniapp-auth-bridge.md),
[Render config](../operations/miniapp-auth-bridge-render.md), and
[database archive/rollback](../operations/miniapp-legacy-sso-cleanup.md).
