# ANKT Developer Platform — Stage 9 specification

Status: **IMPLEMENTED — LOCAL VERIFICATION**, 2026-10-23. Approval:
“ok hãy bắt đầu làm”. Executed evidence and limits are in the handoff; no
production rollout, direct new .NET-sample issuer E2E or native acceptance claimed.
Source: user-supplied Stage 9 requirements I–XXXIV. The proposed decisions below
are approved; deployment, SDK publication and Stage 10 remain excluded.
Read with the [audit](../testing/developer-platform-stage-9-audit.md) and
[implementation plan](../plans/developer-platform-stage-9.md).

## 1. Assumptions and objective

- Vietnamese is the initial documentation language; retain technical API names.
- Keep the integrated Expo 54/React portal, `/developers` prefix, existing account
  system and current Social/SSO protocols. No new framework/CMS/backend service.
- Documentation is publicly readable even if account APIs are offline. Account
  status decorates the header after load; it never authorizes access to content.
- Stage 5–8 local evidence is labeled accurately; native is not accepted. A
  disabled or undeployed feature is not described as publicly available.
- Default canonical origin uses the existing public portal origin from site.ts;
  a validated build-time public origin override supports staging. Never derive
  canonical URLs from request Host or use the portal URL as an assumed issuer.

Deliver a searchable, crawlable guide from account creation to Mini App update,
with real API/SDK contracts and clear non-SSO, SSO and SDK integration paths.
Success is usable exported pages and verified examples, not merely new routes.

## 2. Tech stack and commands

Retain Expo ~54.0.34, Router ~6.0.23, installed React/React DOM/TypeScript, .NET 8,
EF Core/OpenIddict and Node test tooling. Structured TypeScript content avoids
MDX/raw HTML execution. No DB schema or protocol changes are needed.

Proposed tooling change for approval: add root **dev-only esbuild 0.28.2**, the
version already pinned by the local SDK, for a bounded docs client/server bundle.
Audit compatibility/advisories before install; do not upgrade unrelated packages.
Do not rely on a transitive package remaining installed. Existing React DOM
server/client APIs provide build-time rendering and hydration.

Implementation note: React DOM 19.1.0 type declarations were also required and
added as a matching dev-only dependency. The framework/runtime versions remain
unchanged. Initial exported browser/account/protocol evidence is in the handoff.

Existing commands from `../viora`:

```powershell
npm run test:developer
npm --prefix packages/miniapp-sdk test
node node_modules/typescript/bin/tsc --noEmit
npm run build:web -- --output-dir .codex-tmp/stage9-expo-export
```

Planned commands, added only after approval (not currently executable scripts):

```powershell
npm run docs:check
npm run docs:build -- --output-dir .codex-tmp/stage9-docs-export
npm run test:docs
npm run test:docs:e2e
node node_modules/eslint/bin/eslint.js features/developer/docs scripts/build-developer-docs.cjs
```

Backend contract verification from this repository:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .codex-tmp/sdk8-check/sdk).Path
$env:MSBUILDDISABLENODEREUSE = '1'
& "$env:DOTNET_ROOT/dotnet.exe" test Viora.Application.Tests/Viora.Application.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~DeveloperDocumentation'
```

DeveloperDocumentation test classes are planned, not present at this checkpoint.
Tests use isolated fixtures; no configured application database, production
traffic, real email or credentials. Tool sandbox failures are reported separately.

## 3. Architecture and project structure

Proposed design: **docs-only static rendering inside the existing portal build**.
Keep global Expo `web.output: single`; changing the whole Social app to static
rendering would enlarge scope and pull native/session modules into server builds.

`features/developer/docs/` owns a shared pure React docs renderer, typed public
content and a small external hydration entry. The build renders every known docs
route with React DOM server, then bundles the same renderer for `hydrateRoot`.
No runtime server or second account system. Content has no async server fetch or
Suspense requirement. A prototype must prove server/client parity before expansion.

```text
../viora/features/developer/docs/
  content/                  authored public topic groups
  reference/                allowlisted API/SDK contract descriptions
  components/               shell, code, TOC, navigation, search
  lib/                      schema, routes, anchors, search, metadata
  docs-page.tsx             shared pure page renderer
  client.web.tsx            hydration and optional account decoration
  tests/                    unit, artifact, example and browser checks
../viora/scripts/build-developer-docs.cjs
../viora/server/developer/docs-policy.cjs
../viora/.codex-tmp/stage9-docs-export/  local review artifact, not committed
docs/{specs,plans,testing,handoffs}/     internal evidence; never bundled publicly
```

Use `renderToString` for the synchronous authored tree and `hydrateRoot` on the
same page/manifest/identifier prefix. No hand-written second HTML content copy.
Escape serialized public bootstrap data; no inline executable script, raw HTML
from content, or private manifest fields. Assets load from same-origin external
files compatible with existing script CSP.

Reuse brand, header/footer visual primitives, CSS tokens and account API contract.
Extract session-independent pieces as needed. Header account controls form an
optional client island with safe anonymous initial markup; account lookup failure
must leave docs readable. Do not mount ConsoleGuard or MiniAppsProvider for docs.
No session/private details in static HTML, public caches or search index.

Known docs links enter the static document through full-document navigation in
both directions across Expo/static boundaries. Update navigation-policy narrowly;
do not call Expo Router from a standalone hydrated document. Same-page anchors
remain native. Existing Console/Sandbox unload and CSP boundaries remain intact.
The Expo developer route may render the same docs tree for development, but
acceptance always uses actual static exported files.

## 4. Routing, hosting and SEO

Canonical URLs: `/developers/docs` and `/developers/docs/{slug}` without trailing
slash. Generate explicit HTML paths/files plus one canonical redirect policy.
No root `/docs` rewrite. Unknown docs routes return HTTP 404 with noindex and
helpful links; do not fall through to SPA index and report a false 200.

Build composition preserves the Expo artifact, BFF routes, existing public assets,
Developer/Sandbox/Production host headers and private-route rules. Serve known
docs files before SPA fallback. Prepare local, reviewable VPS filesystem routing
and optional Vercel configuration; do not change or run deployment workflow.
Test file resolution and unknown-route status on a local server with real headers.

Each HTML response must contain article text, H1/H2/H3, links, unique title/meta
description, validated canonical, Open Graph and Twitter card metadata before JS.
Use a real existing brand asset for social preview or omit optional image metadata.
Add appropriate static TechArticle/BreadcrumbList JSON-LD only if compatible with
CSP and demonstrably useful; no fabricated ratings/authors/release history.
Generate developer sitemap from the public manifest while retaining unrelated
public entries. Existing robots rules keep console/account/BFF paths excluded.
Indexing eligibility is tested; Google indexing or ranking is not guaranteed.

## 5. Content structure and version metadata

All paths below are relative to `/developers/docs`:

| Group | Pages |
| --- | --- |
| Start | home, introduction, getting-started, integration-paths, developer-account |
| Mini App | create-mini-app, domain-verification, client-credentials |
| ANKT Login | oauth, oidc, public-client, confidential-client |
| SDK/Host | sdk, bridge |
| Lifecycle | sandbox, review, publishing |
| Reference/support | api-reference, troubleshooting, changelog |

Home includes required hero/search/CTAs, Quick Start, integration paths, common
errors and real documentation updates. Getting Started provides location/action,
expected result, likely failure and related link for every requested step.

Typed manifest fields: slug, title, summary, group/order, docs revision, updatedAt,
status, SDK version if relevant, actual API version state, sections, related pages.
Represent content as paragraphs/lists/callouts/code/tables/links/H2/H3. Stable
explicit heading keys produce unique deterministic anchors; validator rejects
duplicate slugs/heading keys, dangling links and unsupported block kinds.
No invented API v2, public release date or SDK publication history. API management
routes are currently unversioned; Swagger's v1 document label is not URL versioning.

Support status uses five categories: implemented-and-verified, implemented-but-
unverified, planned, unsupported, deprecated. Keep internal source/test evidence
in a separate non-exported audit registry. Public wording distinguishes local
verification from deployed availability and browser/native/protocol boundaries.

## 6. API Reference strategy

Explicit allowlist only: Developer-facing account/app/configuration/domain/SSO
configuration/version/review/publication/notification endpoints, relevant public
catalog reads, partner OAuth/OIDC/discovery/JWKS endpoints. Every published entry
must resolve to its route/controller, DTO, validation, auth/filter and rate policy.
OpenAPI is supplemental input; never export its complete shared document.

Exclude Admin, internal executor, infrastructure/operations and private first-
party host launch/redeem/runtime transport. Credential provisioning requires
special review of intended backend-only usage and safe placeholder examples;
never surface a browser credential retriever or interactive secret viewer.

Per endpoint: method/path, purpose, transport/audience, auth scheme, parameter/body
schema and constraints, success status/schema/example, actual error envelope/codes,
pagination/idempotency/concurrency, configured rate policy and complete example.
Schema metadata is checked against actual DTO/API fixtures; reference drifts fail
tests. Do not claim custom backend error envelopes for OpenIddict protocol errors.
List absent guarantees as unspecified rather than inventing retry/reset headers.

Explain portal BFF cookie+CSRF requests separately from upstream DeveloperSession
tooling and OAuth bearer requests. No Social JWT substitution. Samples use visible
placeholder values, no captured credentials, no automatic “try API” execution.

## 7. Integration guides and examples

- Plain website: HTTPS/config/domain/review/publish, SSO optional. Explain any
  current review/Sandbox bridge evidence gate rather than promising that missing
  SDK always satisfies submission readiness.
- Domain: download existing attachment `ankt-verify-{32hex}.txt`, upload byte-for-
  byte at website root and verify; no manual re-entry/password prompt invented.
  Nginx/Apache/ASP.NET Core/static-host examples serve the actual filename/content
  with text/plain, public HTTPS, no redirects/auth. Real tokens never appear.
- Credentials: distinguish app credentials, Sandbox/Production OAuth clients and
  reviewed-version Production material. Confidential secret stays in partner
  backend environment. Explain current issuance/rotation/revocation mechanisms;
  no invented revoke endpoint or password step.
- Public client: existing audited OIDC library/SDK, code+S256/state/nonce/exact
  redirect/issuer/audience/signature validation; memory credentials and explicit
  relogin after reload as applicable. No custom cryptography.
- Confidential: runnable .NET 8 cookie+OIDC sample, external secret configuration,
  backend token exchange, validated ID token and partner session; no browser
  refresh token, no assumed JWT access token, no hardcoded secret.
- SDK: actual `new MiniAppSdk`, initialize, auth methods, popup helpers and
  connectHostBridge, not a fabricated universal `platform` singleton. Each method
  lists signature/arguments/returns/errors/permissions/version/environment/example.
  `auth.logout` is local SDK state clearing, not global provider logout. Document
  Public and Confidential ownership boundaries and v1/v2 limitations.
- Sandbox/review/publishing: real UI labels and paths, readiness, diagnostics,
  version submission/resubmission, approval versus publication, update/unpublish,
  suspension and explicit recovery. Preview is not Android/iOS emulation.
- Troubleshooting includes all requested categories, actual code if implemented,
  otherwise clearly named symptom, cause/check/fix and related link.

Compile/type-check examples against the local SDK. Build the .NET sample against
.NET 8 without live login/network secrets. A compiled sample alone is not a real
issuer integration pass; add isolated protocol checks using existing fixtures.
Code snippets are sourced from the tested examples to prevent copy drift.

## 8. UX/UI, code style and accessibility

Modern documentation within existing ANKT identity: restrained branded header,
grouped collapsible left navigation, readable article and right H2/H3 TOC. Reuse
existing CSS variable names; scope additional dark-mode values to docs only.
No global theme change. Home is a documentation entry point, not an admin dashboard.

Mobile uses an accessible drawer and collapsible TOC. Code/tables scroll locally
without page overflow. CodeExample supports TS/JS/C#/Bash/JSON/HTTP labels, escaped
syntax tokens, copy button/live feedback and denied-clipboard fallback. No eval,
raw dangerous HTML, runtime CDN highlighter or user-executable snippets.

Search indexes only published manifest title/headings/body and provides Ctrl/Cmd+K,
dialog focus trap/restore, Escape, keyboard selection, safe highlighting, anchored
result URLs and empty state. Accent-insensitive Vietnamese matching; bounded
results/query length. No session data, private draft or internal audit content.
Static page remains readable when search JS fails.

TOC uses stable anchors, the actual docs scroll container, reduced-motion-aware
scrolling, active section and hash/back/forward behavior. Avoid duplicate IDs.
Verify semantics/landmarks, headings, skip link, screen-reader names, aria-current,
keyboard operation, contrast in both themes, focus visibility and 200% text zoom.

Example style (proposed types, not runtime implementation):

```ts
type SupportStatus = 'implemented-and-verified' | 'implemented-but-unverified'
  | 'planned' | 'unsupported' | 'deprecated';
type DocHeading = { id: string; level: 2 | 3; title: string };
```

Use explicit types, pure content/search functions and small components. Follow
existing Node test conventions, minimal diffs and approximately five authored
files per task. Generated artifacts remain in bounded `.codex-tmp` output.

## 9. Testing strategy and success criteria

1. Content validators reject missing pages, duplicate slugs/anchors, broken links,
   unsupported SDK calls, API/schema mismatches and private endpoint/secret leaks.
2. Static artifact tests read every raw HTML page and assert complete content,
   metadata/canonical/links, sitemap/robots and correct asset/CSP behavior.
3. Unit tests cover route/anchor/search/highlight/TOC/code copy/theme state; real
   browser tests cover dialog/drawer/keyboard/scroll/clipboard/focus/contrast.
4. Exported E2E completes all 12 requested steps, with anonymous and actual
   Developer-authenticated states. Use existing isolated account fixture; no
   token-seeded UI presented as real login. Unavailable browser gets BLOCKED,
   never PASSED; independent checks continue.
5. Browser widths 320/375/768/1440, both themes, 200% text zoom, JS disabled,
   failed account API, direct deep link/refresh/hash and unknown-route 404 pass.
6. Regress Developer/Console/BFF, Sandbox navigation/CSP, SDK, root auth/Wallet and
   Production host boundaries. No app-wide static-rendering regression accepted.
7. Searchable/SEO-ready pages require no login; every claimed supported capability
   has appropriate evidence and honest environment/package/deployment limits.
8. Build and focused review pass; final handoff maps all 19 requested report topics
   with executed commands/logs and unresolved conditions. Stage 10 is excluded.

## 10. Boundaries, rollback and open questions

Always: preserve unrelated dirty edits; author public content only from contracts;
verify actual exported HTML; keep Developer/SSO/Social sessions separate; audit
new dependency before installation; use safe fixture origins and build inputs.

Review first: this spec/plan, new dependency pin, any design change that needs a
new public endpoint, new auth capability or broader deployment/header boundary.
Never: deploy, run production migration/email, publish SDK, change SSO/registry
behavior to match docs, expose Admin/internal endpoints or credentials, weaken
CSP/CORS, migrate frameworks, claim unexecuted tests, proceed to Stage 10.

Rollback: remove the docs artifact/routing overlay using the prior build artifact;
retain original Expo/BFF/header config and current old docs routes until static
parity is proven. No data rollback. Prepare instructions; do not execute rollout.

Human review decisions (approved 2026-10-23; retained design record):

1. Approve `/developers/docs` and Vietnamese-first scope with honest support labels?
2. Approve docs-only React prerender/hydration and exact static-route overlay,
   retaining global Expo single output and existing account system?
3. Approve root dev-only esbuild pin subject to advisory/compatibility checks?
4. Approve no public npm availability/native support claims until separately proven?

Use current repository public origin as default; any real deployment origin,
upstream API/issuer availability and hosting routing must be operator-verified
before rollout. These are deployment prerequisites, not a reason to fabricate data.

## Official references checked for this proposal

- [Expo static rendering](https://docs.expo.dev/router/web/static-rendering/):
  explains static HTML/export; current docs include later SDK features. Do not
  assume those newer features exist in the installed SDK 54.
- [React renderToString](https://react.dev/reference/react-dom/server/renderToString)
  and [hydrateRoot](https://react.dev/reference/react-dom/client/hydrateRoot):
  synchronous authored render and matching client tree; IDs must match. Current
  docs may show newer React versions, so verify against installed declarations.
- During sample implementation, verify .NET 8 OIDC middleware and installed
  openid-client APIs against official version-appropriate docs before coding.
