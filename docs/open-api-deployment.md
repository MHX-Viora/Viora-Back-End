# ANKT Open API: local verification and VPS rollout

The new issuer is `https://openapi.mxh.ankt.vn/`. It runs inside the existing .NET backend, uses the same database, and is disabled by default. These are reviewable configuration templates; DNS, certificate issuance, database changes, service restarts and production rollout require operator approval. No such actions have been run.

## 1. What the operator receives

| File | Purpose |
| --- | --- |
| `deploy/open-api/open-api.env.example` | New opt-in settings to merge with the current backend environment |
| `deploy/open-api/open-api-migration.sql` | Additive, idempotent PostgreSQL protocol schema |
| `deploy/open-api/nginx-open-api.conf.example` | HTTPS virtual host and exact method/path allowlist |
| `deploy/open-api/ankt-backend.service.example` | Optional same-backend systemd unit template |
| `Viora.Infrastructure/OpenApi/Migrations/` | EF migration and model snapshot for OpenApiDbContext |

Keep the current connection string, internal JWT settings, CORS origins, uploads and jobs. Do not create a second database or replace the existing internal API with the Open API hostname. The internal hostname continues serving the host app; `/api/open-api/authorize` is host-authenticated and is never forwarded by the public virtual host.

Developer registration/review now also requires the additive AppDbContext migration `20261012012833_AddDeveloperPlatformLifecycle`. Reviewable SQL is `deploy/open-api/developer-platform-migration.sql`; it assumes the prior `AddHybridMiniAppPlatform` migration is present. Apply to a staging copy first; confirm legacy Developer/profile/release backfills before approving production. It is separate from the OpenApiDbContext protocol migration above. Configure SMTP and OTP hashing from `deploy/open-api/developer-platform.env.example` before accepting new verified submissions. Workspace handoff: `docs/handoffs/developer-platform.md`.

## 2. Verify locally before changing production

Install .NET 8, the existing database dependencies and the repository's normal frontend prerequisites. Development uses OpenIddict development certificates. The flow still requires the exact registered HTTPS callback; use a trusted local HTTPS callback or a staging partner domain rather than weakening callback checks.

Example PowerShell environment, using the existing local backend configuration:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:OpenApi__Enabled = 'true'
$env:OpenApi__Issuer = 'https://openapi.localhost:7043/'
$env:OpenApi__Audience = 'ankt-open-api-local'
$env:OpenApi__AuthorizationUi = 'https://localhost:8081/oauth/authorize'
$env:OpenApi__KnownProxies__0 = '127.0.0.1'
$env:EXPO_PUBLIC_OPEN_API_ISSUER = 'https://openapi.localhost:7043'
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -nr:false
dotnet build viora-BE/viora-BE.csproj --no-restore -m:1 -nr:false
```

Bind trusted local HTTPS separately using your existing ASP.NET launch profile/certificate configuration. Use distinct internal/Open API hostnames on the same listener. `openapi.localhost` must resolve to your local listener. The test suite uses in-memory SQLite and the real OpenIddict pipeline; it does not contact production or apply migrations.

To regenerate/inspect the SQL without connecting to a database:

```powershell
$env:VIORA_EF_DESIGN_CONNECTION = 'Host=127.0.0.1;Database=ankt_design_only;Username=design;Password=design'
dotnet ef migrations script --idempotent --context OpenApiDbContext --project Viora.Infrastructure/Viora.Infrastructure.csproj --startup-project viora-BE/viora-BE.csproj --output deploy/open-api/open-api-migration.sql
dotnet ef migrations has-pending-model-changes --context OpenApiDbContext --project Viora.Infrastructure/Viora.Infrastructure.csproj --startup-project viora-BE/viora-BE.csproj
```

The dummy connection is only a design-time setting. Use a protected environment/credential file for any real database operation. Never paste production credentials into terminal arguments or the repository.

## 3. Review schema and protect keys

Back up the production database and verify restore before applying anything. Review the SQL: it creates `OpenIddictApplications`, `OpenIddictAuthorizations`, `OpenIddictScopes`, `OpenIddictTokens` with foreign keys and unique lookup indexes, and `__OpenApiMigrationsHistory`. It does not modify ANKT Accounts, MiniApps, Developers, grants, wallets or their migration history. Apply this additive script with the existing approved DBA process, once authorized; the app does not run this migration automatically.

Provision separate persistent RSA certificates for signing and encryption. They must have private keys and appropriate digital-signature/key-encipherment usage and validity dates. Put password-protected PFX files outside the checkout, for example `/etc/ankt/open-api/signing.pfx` and `encryption.pfx`, readable only by the backend service account. Store the shared certificate password in `/etc/ankt/backend.env` with restrictive permissions. Keep certificates and password in encrypted backups. TLS certificates in Nginx protect transport and are separate from OAuth signing/encryption certificates.

Production startup refuses enabled Open API without its certificate files. Development keys are not a production fallback. Plan rotation while retaining old certificates for verification/decryption of outstanding artifacts; do not overwrite active keys without a staged rotation change.

## 4. Route the public origin

After approval, create the DNS record for `openapi.mxh.ankt.vn` to the intended VPS/proxy and obtain its TLS certificate through your existing certificate process. The new hostname is required for this deployment design. Install the reviewed Nginx template into the `http` configuration. Adjust the upstream port to the existing backend listener and certificate paths to actual provisioned files. Test Nginx syntax before any reload.

The template serves only approved Open API routes. `/swagger`, `/api/auth`, `/api/accounts`, `/api/admin`, wallet APIs, unknown paths and unsupported methods return 404. HTTP returns 400 rather than redirecting sensitive query strings. Forwarded headers are overwritten at the proxy, and `OpenApi__KnownProxies__0=127.0.0.1` trusts only the local Nginx hop. For a container or a different proxy, set its exact address instead; never trust every source. Keep the backend socket private via loopback/firewall/network policy. `PORT` overrides `ASPNETCORE_URLS` in the existing backend: unset it for the supplied loopback systemd example.

For multiple proxy hops, adapt the trusted-proxy list and forwarding limit in code/config after review. The supplied single-hop template must not be blindly installed behind an additional untrusted proxy. Render requires equivalent custom-domain/TLS/routing controls; this VPS Nginx template is not a Render DNS or routing command.

## 5. CORS, request limits and logging

Merge specific ANKT web origins into the existing `Cors:AllowedOrigins`; avoid wildcard credentialed CORS. Partners exchange codes and call userinfo from their own backend, so they do not need browser access to confidential-client token exchange. The SDK script can load as a script from the approved HTTPS origin. Browser-held access tokens/client secrets are not part of the integration.

Nginx rejects bodies above 16 KiB and applies an IP request limit. Application limits additionally enforce 120 requests/minute/IP, 600/minute/verified client and 60/minute/verified client plus pairwise user on issuance/resource calls. Authorization/consent checks remain mandatory. Monitor 429 rates and adjust limits for staging traffic; IP limits alone do not distinguish partner clients or shared NAT users.

Use the supplied access-log format: method, `$uri` (path only), status, time and correlation. Never use `$request`, `$request_uri`, `$args`, headers or body logs on protocol/host authorization endpoints. Disable debug request tracing/APM query capture and redact authorization headers at every proxy. The dedicated Nginx virtual host suppresses error logs because Nginx errors may include complete request queries. Monitor its safe access log and ANKT's path-only audit instead. Protect and retain logs under the existing privacy policy.

## 6. Enable a staging partner first

Deploy source with `OpenApi__Enabled=false`. Verify normal internal login, MiniApp launch and legacy exchange before enabling. Publish the updated host app with `EXPO_PUBLIC_OPEN_API_ISSUER` matching the backend issuer; deploy the partner's standard OIDC server implementation and exact callback. Confirm developer/application approval, callback domain ownership, and approved scopes in Admin.

After approved schema/routing/key setup, enable the feature in staging. Check discovery/JWKS, token issuance and a complete browser callback through the partner backend. Then verify:

1. Existing consent opens the MiniApp without an opening prompt. Missing data-sharing consent asks the user once; denial stops the flow.
2. Wrong redirect/client/verifier, expired/reused code and wrong issuer/audience fail.
3. Internal JWTs cannot call `/v1`; partner JWTs cannot call internal APIs.
4. Profile/email/phone are returned only for their approved, currently granted scopes.
5. Account/app/developer suspension, grant withdrawal and token revocation block subsequent resource calls.
6. The partner creates its own session and maps `(issuer, client_id, sub)`; it never links an account automatically by email.
7. Request logs contain no code, ticket, access token, client secret, PKCE verifier or personal data.

The actual Nghệ Thuật Số website is a separate deployment. Updating ANKT alone cannot create the website's session: its backend and callback must run the partner integration. Verify iframe cookie/session restrictions in the target browser; do not mistake a successful token exchange for a successfully persisted partner session.

## 7. Rollback and operations

If validation fails, set `OpenApi__Enabled=false` through the approved deployment process and remove/disable only the new public virtual host if appropriate. Leave the additive protocol tables in place. Retain the existing legacy SDK/exchange and internal authentication; partner code must preserve an explicit fallback/login path during rollout. Revoked tokens and expired codes stay rejected; rollback does not reinstate grants.

Do not roll back the database by dropping protocol tables while clients might still be using them. Destructive schema removal requires separate review/backup and confirmation that no active clients remain. Retain signing keys for diagnostics/rotation needs and protect them after shutdown.

The enabled backend prunes invalid/expired protocol entries through OpenIddict hourly, with a one-day cutoff. Active entries are retained by the library's pruning policy. Application audit logs follow the existing retention process and are not deleted by protocol pruning. Watch health, issuance/error/revocation rates, database growth, consent denial rate, callback failures and partner session failures before widening rollout.
