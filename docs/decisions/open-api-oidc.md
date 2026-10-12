# Open API protocol boundary

Status: accepted for implementation; production rollout remains operator-controlled.

ANKT's MiniApp registry already owns developers, approved redirects and permissions, hashed client secrets, user grants and pairwise identities. Replacing those records would create competing approval flows. The standard OAuth/OIDC layer therefore reconciles protocol clients from the existing approved registry and stores only protocol metadata in four additional OpenIddict tables in the same PostgreSQL database.

OpenIddict 7.7.1 handles authorization codes, S256 PKCE, signing, client authentication, token validation and revocation. ANKT implements its registry and consent policy around the library. Partner access tokens use an independently configured issuer/audience and asymmetric certificates; the existing internal HS256 Bearer scheme remains separate. Each resource request additionally checks current account, application, developer and grant status. Pairwise `sub` identifies a user within one MiniApp; email never automatically merges partner accounts.

The Open API hostname exposes an exact route allowlist in both ASP.NET and Nginx. Public URLs are discoverable; authentication and scope enforcement protect data. Neither hiding the URL nor CORS replaces authorization. The existing SDK/exchange remains available while partners migrate. Disabling `OpenApi:Enabled` stops the new protocol without dropping tables or altering existing user records.

The new context uses `__OpenApiMigrationsHistory`, independent of AppDbContext's migration history. Tokens/codes are retained for at least one day and pruned through OpenIddict's store managers. Signing/encryption certificates are persistent operator-managed files, separate from TLS certificates; development certificates are used only in Development.

Consequences: DNS/TLS and host routing must be configured before rollout; certificate rotation must preserve old verification/decryption material until outstanding artifacts expire. Backup/restore must include protocol tables and private certificate files. No production deployment is performed by this source change.
