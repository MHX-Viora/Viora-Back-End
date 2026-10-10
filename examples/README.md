# Runnable Mini App integration examples

These examples are independent partner applications, not ANKT authentication replacements. They demonstrate a local account store, existing login, explicit ANKT login/link intent, browser-bound state/S256 transactions, scoped exchange, confirmed unlinking and conflict prevention. Both use SQLite only in their own project; no ANKT database is accessed.

## Node.js

Requires Node 22.13+ with built-in `node:sqlite` (experimental in Node 22). No third-party packages are needed.

```powershell
$env:NODE_ENV = 'development'
$env:MINI_APP_ORIGIN = 'http://127.0.0.1:5310'
$env:ANKT_API_URL = 'https://your-ankt-api.example'
$env:ANKT_APP_ORIGIN = 'https://your-ankt-app.example'
$env:ANKT_CLIENT_ID = 'client-id-from-your-registration'
# Set ANKT_CLIENT_SECRET from your secret manager for a confidential client.
node examples/mini-app-node/server.mjs
```

`MINI_APP_DATABASE` optionally selects the SQLite path; default `mini-app.sqlite`. `PORT` defaults to 5310. The server binds loopback; use proper HTTPS termination for staging/production. `MINI_APP_ORIGIN` must equal the externally registered HTTPS origin for actual ANKT integration. Loopback HTTP is allowed only for independent sample development, not ANKT platform registration.

## ASP.NET Core

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5311'
$env:MiniApp__Origin = 'http://127.0.0.1:5311'
$env:ANKT__ApiUrl = 'https://your-ankt-api.example'
$env:ANKT__AppOrigin = 'https://your-ankt-app.example'
$env:ANKT__ClientId = 'client-id-from-your-registration'
# Set ANKT__ClientSecret from your secret manager for a confidential client.
dotnet run --project examples/mini-app-dotnet/PartnerMiniApp.csproj
```

`ConnectionStrings__Partner` selects the partner database. This example uses `EnsureCreated` for its standalone sample store; replace with your own migrations for production. Persist and protect ASP.NET Data Protection keys in production, configure trusted reverse proxies, HTTPS and distributed rate limiting.

## Configure registration

Use SSO mode, `identity.login` and optionally `profile.basic`, the actual HTTPS sample website origin and exact `/auth/ankt/callback`. Publish each domain's challenge file from your hosting setup and verify it before review. The sample pages intentionally contain no hard-coded ANKT secrets. Existing account linking requires the local password and an explicit action; SSO-only accounts cannot unlink their last login method.

## Checks

From `viora-BE`:

```powershell
node --test --experimental-test-isolation=none examples/mini-app-node/identity-store.test.mjs examples/mini-app-node/server.test.mjs
dotnet test examples/mini-app-dotnet-tests/PartnerMiniApp.Tests.csproj -m:1 -p:UseSharedCompilation=false -nodeReuse:false
```

Node tests use an in-memory store and local port 5391; they never contact ANKT's deployed API. They verify ordinary login, CSRF/origin protection, transaction binding and identity conflicts. ASP.NET tests verify account creation, linking, unlinking, uniqueness and last-auth-method protection against an in-memory SQLite store.
