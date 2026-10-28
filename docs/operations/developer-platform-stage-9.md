# Stage 9 — documentation build and rollout runbook

Scope: integrated Expo portal and static `/developers/docs` documents. These
commands prepare local artifacts; none deploys or accesses the application DB.
Stage 10, production changes and SDK publication require separate authorization.

## Source and artifact boundaries

- FE content/renderer: `features/developer/docs`; shared pure identity/footer:
  `features/developer/components/site-identity.tsx`.
- Public API inventory and generated DTO schemas: `docs/reference` within that
  feature. Backend-source evidence/tests are not bundled into the docs client.
- `scripts/build-developer-docs.cjs` writes only a child of FE `.codex-tmp`.
  Docs HTML, external JS/CSS, brand asset, sitemap/robots, docs manifest, 404 HTML
  and composed `vercel.json` belong to the review artifact.
- It does not enable SSO/host, change credentials, copy `.env`, deploy, migrate,
  alter CORS, modify Social auth or run an example against an external service.
- Existing Expo `web.output: single` is retained. Only docs get full static HTML.

## Rebuild content/contracts

From BE root, using installed .NET 8 (local SDK shown for this workstation):

```powershell
$env:DOTNET_ROOT = (Resolve-Path .codex-tmp/sdk8-check/sdk).Path
& "$env:DOTNET_ROOT/dotnet.exe" build tools/DeveloperDocumentationContracts/DeveloperDocumentationContracts.csproj -c Release -m:1 -p:UseSharedCompilation=false
& "$env:DOTNET_ROOT/dotnet.exe" tools/DeveloperDocumentationContracts/bin/Release/net8.0/DeveloperDocumentationContracts.dll
& "$env:DOTNET_ROOT/dotnet.exe" test Viora.Application.Tests/Viora.Application.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~DeveloperDocumentation'
```

The generator reads the two explicit public inventory files and reflects public
DTO shapes from the existing Application assembly. It fails unknown DTO names.
No database/configuration/API is read. Request semantics still need service/API
review: nullability/attributes alone do not describe every business validation.
Do not add Admin/executor/private host routes to make an inventory test pass.

From FE root:

```powershell
node scripts/docs-examples.cjs
npm run docs:check
npm run test:docs
node node_modules/typescript/bin/tsc --noEmit
npm run docs:build
```

`docs:build` alone produces a docs-only artifact; Console/Login links require the
normal Expo/BFF artifact for full portal navigation. Run the same version of
React/React DOM for server/client render. Examples are generated from tested
source, not copied manually into JSX. SDK remains private, not public npm.

## Compose with an Expo export

Windows npm.ps1 dropped forwarded export flags during verification. Use the
direct Node CLI for commands requiring flags:

```powershell
$env:EXPO_NO_DOTENV = '1'
# Set explicitly reviewed public deployment configuration in this shell.
# Never use actual secrets in EXPO_PUBLIC_* variables.
node node_modules/expo/bin/cli export --platform web --clear --output-dir .codex-tmp/stage9-expo-export
node scripts/build-developer-docs.cjs --base-export .codex-tmp/stage9-expo-export --output-dir .codex-tmp/stage9-docs-export
```

Canonical origin defaults to the existing `https://mxh.ankt.vn`; set
`ANKT_DOCS_PUBLIC_ORIGIN` to a validated public origin for another deployment.
It is metadata, not an issuer or backend URL. Only HTTPS (or loopback HTTP for
tests) with no credentials/path/query/fragment is accepted.

**The current Stage 9 Expo E2E export uses loopback API configuration and disabled
Mini App flags. It is a fixture artifact, not a production deployment candidate.**
Rebuild with operator-validated public configuration before any later rollout.
Do not copy private build intermediates or `.codex-tmp/stage9-docs-build` to hosting.

## Routing and headers

Current hosting workflow targets a VPS. Preserve its existing root, BFF handling,
security headers and Social fallback. Required routing order:

1. Existing Developer BFF/API rules and dedicated host/Sandbox boundaries.
2. Exact `/developers/docs` -> `developers/docs/index.html`.
3. Known docs slugs -> `developers/docs/<slug>/index.html`.
4. `/developers/docs-assets/*`, brand, sitemap and robots as static files.
5. Unknown `/developers/docs/*` -> actual HTTP 404, with noindex/helpful links;
   use generated `developers/docs-404.html` as error body while preserving 404.
6. Existing Social SPA fallback only outside these docs boundaries.

Canonical docs URLs omit trailing slash. Redirect the slash variant consistently
(308 in local fixture) rather than serving two indexable copies. Nginx operators
can use exact/prefix locations with `try_files $uri/index.html =404`, an exact
home document, and `error_page 404 /developers/docs-404.html`; preserve the full
existing security-header include when defining location headers. Do not replace
the portal's global configuration with an abbreviated example.

Docs policy: script/style from self, same-origin account fetch, self/data images,
no object/frame embedding, no external executable scripts, no inline executable
bootstrap. `server/developer/docs-policy.cjs` composes existing Sandbox/Production
policy generators without rewriting BFF destinations or weakening their policy.
Static public documents may use revalidation caching; account/BFF stays no-store.
No account data may be server-rendered or cached in a docs artifact.

For optional Vercel packaging, the generated configuration adds exact known docs
rewrites and excludes docs/asset prefixes from the SPA catch-all. Unknown docs
reach hosting's native 404. The helpful generated docs 404 body requires the host's
error-page mapping; no deployed Vercel/VPS behavior has been certified locally.

## Local browser acceptance

Build the existing isolated Developer account fixture first, from BE root:

```powershell
& "$env:DOTNET_ROOT/dotnet.exe" build tools/DeveloperE2EHost/DeveloperE2EHost.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
```

From FE root, after composing the Expo/docs artifact:

```powershell
npm run test:docs:e2e
```

The first runner verifies anonymous HTML/search/keyboard/copy/TOC/theme/mobile/SEO.
The second starts loopback SMTP and the existing account service with disposable
SQLite, registers/verifies a real fixture account, then uses actual browser login
and Console -> Docs -> Console -> Docs navigation. No cookie/token injection.
`STAGE9_CHROME` can select a Chrome executable. Each runner owns a fresh temporary
profile and closes only its own browser. Account fixture uses a random backend
port and keyed loopback shutdown; SMTP is local only. No production mail/config/DB.

Expected anonymous `/auth/me` 401 is explicitly allowed. Existing Expo
apple-mobile-web-app-capable and RN Web pointerEvents deprecation warnings are
identified separately; other runtime/CSP/network errors fail account acceptance.
Chrome DevTools MCP was unavailable because its profile was already in use; the
owned Chrome/CDP fixture supplies browser evidence. No existing browser was stopped.

## Rollout and rollback gates

Before an authorized rollout: inspect actual HTTP HTML/CSP/canonical/sitemap/404
on target hosting, verify real issuer/API availability and mail configuration,
review dependency advisories, and run browser/cookie compatibility checks. Local
crawlability is not a guarantee of Google indexing/ranking. Native Stage 8 gates
are unchanged. The .NET partner sample has compilation evidence; the reused real
Confidential protocol fixture uses its existing partner implementation, not that
sample's middleware. Do not present the latter as direct .NET sample E2E.

Keep prior hosting artifact and route configuration. Rollback restores that
artifact and removes only the Stage 9 docs routing overlay. There is no data/schema
rollback. Preserve unrelated dirty work; never use blanket git reset/clean. No
automatic deployment or rollback was performed in this implementation.
