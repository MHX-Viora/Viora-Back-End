# ANKT Developer Platform — Stage 9 implementation plan

Status: **IMPLEMENTED — LOCAL VERIFICATION**, 2026-10-23. Approved by user:
“ok hãy bắt đầu làm”. See [handoff](../handoffs/developer-platform-stage-9.md)
for executed evidence and sample/hosting/native limits. No deployment/Stage 10.
[Spec](../specs/developer-platform-stage-9.md) ·
[source audit](../testing/developer-platform-stage-9-audit.md).
The user's section XXXII approval gate has been met. The task design below is
retained for traceability; actual delivery status is summarized here.

| Tasks | Local result |
| --- | --- |
| 1–3 | 54 intended account/owner/Sandbox/catalog operations, seven protocol entries, 57 generated DTO shapes; route/field/boundary checks. Credential retrieval operations remain outside the public automated reference; actual Console workflows are documented. |
| 4–6 | Typed content/validators, bounded build inputs, approved tooling and actual prerender/hydration prototype verified. |
| 7–14 | Shared identity/footer, independent docs/account island, full-document routing, sidebar/drawer, code/copy, TOC, theme and local search verified in Chrome. |
| 15–18 | Start/account/create/domain/credentials/OAuth/OIDC guides complete against current UI/contracts. |
| 19–21 | Workspace SDK and standalone standard-library Public samples type-check; .NET8 Confidential sample builds; existing real Confidential protocol fixture passes. Direct new .NET-sample issuer E2E is not claimed. |
| 22–25 | Sandbox/review/publishing/reference/troubleshooting/changelog; all 20 pages and per-step/related links validated. |
| 26–27 | Local Expo/static composition, exact routes, 404, metadata, sitemap/robots and public output checks pass. Actual deployed hosting verification remains a rollout gate. |
| 28–29 | Anonymous docs E2E and actual register/SMTP-verify/browser-login/Console/docs account E2E pass on exported artifact. |
| 30 | Security/source/accessibility review, operations/rollback and 19-topic handoff recorded. No open high-severity finding in reviewed scope. |

## Decisions and dependency order

- Keep `/developers`, integrated Expo/React feature and existing account system.
- Build full static HTML for docs only; hydrate the same public renderer with
  external assets. Leave the whole-app `web.output: single` configuration intact.
- Render public content independently of DeveloperSession; decorate header later.
- Maintain an explicit public endpoint allowlist and separate internal evidence.
- No backend runtime functionality, migration, deployment or Stage 10 work.

Dependency graph: verified content/contracts -> pure renderer/static prototype ->
shell/navigation and components -> authored guide groups -> artifact integration ->
isolated E2E/SEO/security review -> documented local acceptance.
Work sequentially. Do not create autonomous agent fan-out from this plan.

## Verification command keys

Commands run from frontend `../viora` unless marked BE. Planned scripts/classes
will be added by their owning tasks; they do not exist yet at this checkpoint.

| Key | Command |
| --- | --- |
| DOC | `npm run docs:check` |
| UNIT | `npm run test:docs` |
| TYPES | `node node_modules/typescript/bin/tsc --noEmit` |
| LINT | `node node_modules/eslint/bin/eslint.js features/developer/docs scripts/build-developer-docs.cjs` |
| BUILD | `npm run docs:build -- --output-dir .codex-tmp/stage9-docs-export` |
| DEV | `npm run test:developer` |
| SDK | `npm --prefix packages/miniapp-sdk test` |
| E2E | `npm run test:docs:e2e` |
| HOST | BE: `node --test --experimental-test-isolation=none tools/ProductionHostTests/*.test.mjs` |
| CONTRACT | BE .NET test command in spec, filter `FullyQualifiedName~DeveloperDocumentation` |

Every implementation task follows RED -> minimal GREEN -> relevant verification
and review. Content-only increments run DOC/UNIT plus affected example checks;
runtime/component changes run affected tests, TYPES and appropriate build. At
checkpoints run broader BUILD/regression. Do not rebuild unrelated native/entire
backend suites for each prose edit. Capture exit codes and concise evidence logs.

## Phase A — evidence, contracts and static proof

| # | Task / dependency | Authored files (relative to FE unless BE) | Acceptance / verification |
| --- | --- | --- | --- |
| 1 | Freeze public API inventory. Approval required. | `docs` internal audit in BE; `features/developer/docs/reference/api-inventory.ts`; `lib/reference-schema.ts`; `tests/reference-inventory.test.ts` | Enumerate intended owner/account/catalog/OIDC routes, actual transport/auth, private exclusions and source mapping. Fail unknown/private paths. UNIT inventory tests. |
| 2 | Account/CRUD/domain contract coverage. Depends 1. | BE `Viora.Application.Tests/DeveloperDocumentationAccountTests.cs`, `DeveloperDocumentationMiniAppTests.cs`; FE `reference/account.ts`, `reference/mini-app.ts`, `tests/reference-schema.test.ts` | DTOs, required fields, attachment contract, errors and status codes match real API fixtures; no secret capture. CONTRACT + focused UNIT. |
| 3 | SSO/version/review/publishing contracts. Depends 1. | BE `Viora.Application.Tests/DeveloperDocumentationLifecycleTests.cs`, `DeveloperDocumentationSsoTests.cs`; FE `reference/lifecycle.ts`, `reference/sso.ts`, `tests/lifecycle-reference.test.ts` | Actual protocol/management endpoints and auth schemes distinguished; Admin/host/executor excluded. CONTRACT + UNIT. Split before coding if fixture setup exceeds file limit. |
| 4 | Content schema, manifest and validators. Depends 1. | `lib/content-schema.ts`, `lib/manifest.ts`, `lib/validate.ts`, `tests/content.test.ts`, `tests/routes.test.ts` | Slugs/order/status/version metadata/links/anchors deterministic; private evidence not exportable; duplicate/unknown blocks fail. UNIT + TYPES. |
| 5 | Dependency gate and tooling. Depends approval. | `package.json`, lockfile; `scripts/build-developer-docs.cjs`; `tests/build-input.test.ts` | Review existing pinned esbuild license/advisories and installed Node/React APIs; root dev pin only. Validate output root and safe build inputs; no secret env enumeration/network. Tooling tests + TYPES. Lockfile listed separately as generated. |
| 6 | Static/hydration vertical prototype. Depends 4,5. | `docs-page.tsx`, `client.web.tsx`, `components/article.tsx`, `tests/prerender.test.ts`, `content/introduction.ts` | One real `/developers/docs/introduction` page returns full article/metadata without JS; same renderer hydrates with zero recoverable errors under script CSP. BUILD + raw-HTML test + actual browser. No large shell/content expansion before success. |

Checkpoint A: public contract boundaries reviewed; no private endpoint leakage;
static/interactive parity and current React/Expo compatibility proved locally.
If prototype requires global rendering/auth changes, stop and submit the specific
revised design for review rather than silently migrating architecture.

## Phase B — portal integration and documentation components

| # | Task / dependency | Authored files | Acceptance / verification |
| --- | --- | --- | --- |
| 7 | Shared shell and optional account controls. Depends 6. | `components/site-shell.tsx` (existing); `docs/components/docs-shell.tsx`, `docs/components/account-controls.tsx`, `docs/tests/shell.test.tsx`, `docs/docs.css` | Reuse brand/footer/tokens without ConsoleGuard; full docs readable with pending/401/503/offline session. Anonymous/authenticated header parity, no private static HTML. UNIT + TYPES + BUILD. |
| 8 | Canonical route and full-document navigation boundary. Depends 7. | `developer-screen.web.tsx`, `lib/navigation-policy.ts` (existing), `docs/lib/routes.ts`, `docs/tests/navigation.test.ts`, existing navigation-policy test | `/developers/docs[/slug]` works in portal and export; no root auth broadening or standalone Expo Router calls. Console/Sandbox unload behavior preserved. UNIT + DEV + TYPES. |
| 9 | Desktop/sidebar/mobile navigation. Depends 7,8. | `docs/components/sidebar.tsx`, `docs/components/mobile-navigation.tsx`, `docs/docs.css`, `docs/tests/navigation-ui.test.tsx` | Groups/collapse/current route/breadcrumb/previous-next, mobile drawer trap/Escape/focus restore. Native anchors and no-JS nav work. UNIT + BUILD + browser keyboard/width checks. |
| 10 | Safe code examples and tables. Depends 4,7. | `docs/components/code-example.tsx`, `docs/lib/highlight.ts`, `docs/components/content-block.tsx`, `docs/tests/code-example.test.tsx`, `docs/docs.css` | Six requested languages; escaped highlighting, label/copy/live status/failure fallback and local overflow. No HTML injection/eval/external highlighter. UNIT + TYPES + BUILD; browser clipboard check. |
| 11 | TOC, anchors and scroll tracking. Depends 4,9. | `docs/components/table-of-contents.tsx`, `docs/lib/anchors.ts`, `docs/lib/scroll-tracking.ts`, `docs/tests/toc.test.ts`, `docs/docs.css` | H2/H3, stable unique anchors, active section, hash direct/reload/back/forward, mobile collapse/reduced motion; actual scroll container. UNIT + BUILD + browser scroll test. |
| 12 | Scoped theme and baseline accessibility. Depends 7,10. | `docs/lib/theme.ts`, `docs/components/theme-toggle.tsx`, `docs/docs.css`, `docs/tests/theme.test.ts` | Light/dark docs tokens, safe persisted preference/system fallback, SSR parity, focus/contrast/reduced motion. No global portal theme mutation. UNIT + TYPES + BUILD + browser both themes. |
| 13 | Search index and query engine. Depends 4. | `docs/lib/search-index.ts`, `docs/lib/search.ts`, `docs/tests/search.test.ts`, build script if needed | Only public title/headings/body; Vietnamese accent-insensitive matching, deterministic bounds/ranking, safe result spans/anchors, no private evidence. UNIT + DOC. |
| 14 | Search dialog and header integration. Depends 7,13. | `docs/components/search-dialog.tsx`, `docs/components/docs-header.tsx`, `docs/tests/search-ui.test.tsx`, `docs/docs.css` | Header/home entry, Ctrl/Cmd+K, Escape, focus trap/restore, keyboard selection, highlights and empty state; no query HTML injection. UNIT + BUILD + browser keyboard test. |

Checkpoint B: responsive real docs shell works at 320/375/768/1440; no page-level
code/table overflow, keyboard traps or hydration errors; account failure does not
block reading. Preserve existing Developer/BFF tests and root types.

## Phase C — public content and executable examples

| # | Task / dependency | Authored files | Acceptance / verification |
| --- | --- | --- | --- |
| 15 | Home/start/integration paths/account. Depends 2,4,7. | `docs/content/home.ts`, `getting-started.ts`, `integration-paths.ts`, `developer-account.ts`, manifest | Required hero/search/CTAs/groups/common errors/real updates; exact create/config UI steps. Current verified-email password flow and disabled Developer federation distinguished from Mini App SSO. DOC + UNIT + BUILD. |
| 16 | Create/configure/domain guides. Depends 2,10,15. | `docs/content/create-mini-app.ts`, `domain-verification.ts`, `docs/examples/domain-serving.ts`, `docs/tests/domain-examples.test.ts`, manifest | Exact attachment filename/contents/path, no invented `.well-known`/password/manual domain step; Nginx/Apache/.NET/static-host samples and common failures. Test response/file constraints and DOC. |
| 17 | Credential lifecycle guide. Depends 2,3. | `docs/content/client-credentials.ts`, `docs/tests/credential-content.test.ts`, manifest | App vs environment vs version credentials separated; Public never needs secret; backend-only issuance/rotation and real revocation options; no invented endpoint. DOC + contract tests. |
| 18 | OAuth/OIDC conceptual guides. Depends 3. | `docs/content/oauth.ts`, `oidc.ts`, `docs/tests/oidc-content.test.ts`, manifest | Code/S256/state/nonce/consent/ID-token/UserInfo/discovery/JWKS; exact issuer metadata and opaque access token semantics; unsupported refresh/implicit/password explicitly excluded. DOC + UNIT. |
| 19 | Public SPA integration example. Depends 3,18. | `docs/examples/public-client.ts`, `docs/examples/public-callback.ts`, `docs/content/public-client.ts`, `docs/tests/public-example.test.ts`, manifest | Use version-verified local SDK/openid-client APIs; no custom crypto/secret; actual PKCE/nonce/state validation, callback and failures. Type-check/example unit tests + existing isolated OIDC fixture where compatible. No registry availability assumption. |
| 20 | Confidential .NET 8 sample. Depends 3,18. | `docs/examples/confidential/Program.cs`, sample `.csproj`, `README.md`; `docs/content/confidential-client.ts`; manifest | Cookie+OIDC middleware, code PKCE, external env secret, backend exchange, ID-token validation/partner session; exact callback, no browser refresh token. .NET8 sample build + isolated issuer protocol verification, no live mail/application DB. |
| 21 | SDK/Bridge reference. Depends 10,19. | `docs/reference/sdk.ts`, `docs/content/sdk.ts`, `bridge.ts`, `docs/tests/sdk-contract.test.ts`, manifest | Every exported documented method has real signature/return/errors/version/permission/browser/native scope; v1/v2 restrictions and local logout accurate. Test against compiled exports/declarations, SDK + DOC. Package private status explicit. |
| 22 | Sandbox guide. Depends 3,21. | `docs/content/sandbox.ts`, `docs/tests/sandbox-content.test.ts`, manifest | Real readiness/start/preview/SSO/bridge/diagnostics/retry flow; distinguish Browser/Mobile Preview from native runtime; no executor internals. DOC + existing Sandbox tests. |
| 23 | Review and publishing guides. Depends 3,22. | `docs/content/review.ts`, `publishing.ts`, `docs/tests/publishing-content.test.ts`, manifest | Version/readiness/submit/revise/approve/publish/update/unpublish/suspend/recovery; Approved != Published, old publication remains on draft edit. DOC + contract/Developer regression. |
| 24 | API Reference presentation. Depends 1–3,10. | `docs/components/api-reference.tsx`, `docs/content/api-reference.ts`, `docs/tests/api-reference.test.tsx`, manifest | Every allowlisted endpoint has method/path/auth/request/response/errors/rate/concurrency/example. No Admin/internal/host/secret leakage. CONTRACT + DOC + BUILD. |
| 25 | Troubleshooting/changelog and content reconciliation. Depends 15–24. | `docs/content/troubleshooting.ts`, `changelog.ts`, `docs/lib/change-entries.ts`, `docs/tests/support-content.test.ts`, manifest | All requested error symptoms map to actual codes or clearly named symptoms; cause/check/fix/links. Actual docs updates only; no invented SDK release history. All required pages/links/metadata pass DOC. |

Checkpoint C: content covers all three integration paths and requested topics;
all code examples compile/validate; source/API/SDK drift guards pass. Review
credential/OIDC instructions against actual contracts before public artifact build.

## Phase D — artifact routing, SEO and acceptance

| # | Task / dependency | Authored files | Acceptance / verification |
| --- | --- | --- | --- |
| 26 | Static artifact composition and route policy. Depends 6,8,25. | `scripts/build-developer-docs.cjs`, `server/developer/docs-policy.cjs`, `docs/tests/artifact-routing.test.ts`, `docs/tests/static-server.cjs`, BE `docs/operations/developer-platform-stage-9.md` | Known docs served before SPA; consistent canonical slash redirects; unknown docs return actual 404/noindex; preserve Expo/BFF/Sandbox/host policies/assets. Local artifact only. BUILD + routing HTTP tests + DEV/HOST. |
| 27 | SEO/sitemap/robots metadata. Depends 25,26. | `docs/lib/metadata.ts`, `docs/lib/sitemap.ts`, build script, `docs/tests/seo.test.ts`, existing `public/robots.txt` if required | Complete raw HTML text, unique title/description/canonical/OG/Twitter, safe optional JSON-LD, sitemap retains existing public entries and excludes private paths. No broad crawl unblock/CSP relaxation. DOC + BUILD + JS-disabled HTTP/browser checks. |
| 28 | Anonymous exported-site E2E. Depends 9–14,25–27. | `docs/tests/docs.e2e.mjs`, `docs/tests/browser-fixture.mjs`, `docs/tests/artifact-server.test.ts` | User steps 1–10: docs/search/Quick Start/domain/copy/navigation/API/theme/deep-link refresh/mobile. Real exported artifact and headers, 320/375/768/1440/both themes/200% zoom, zero unexplained console/network/hydration failures. E2E; screenshots and inspected a11y tree. |
| 29 | Authenticated account island E2E. Depends 28. | BE isolated `tools/SsoE2EHost` fixture extension (up to 2 files); FE `docs/tests/account.e2e.mjs`, browser fixture; evidence report | Real existing Developer login -> Console -> docs -> correct optional account controls; unavailable/expired account leaves docs readable. No seeded raw browser token or production mail. E2E steps 11–12 + DEV. Split fixture wiring first if >5 files. |
| 30 | Security/accessibility/regression review and handoff. Depends all. | BE `docs/testing/developer-platform-stage-9-security.md`, `docs/handoffs/developer-platform-stage-9.md`, operations/spec/plan status updates | Scan only public output for forbidden routes/secret formats/source maps/env leakage; manual semantic/contrast/keyboard review; tests/build and compatibility evidence. Report all 19 requested categories, remaining limits and Stage 10 conditions. No deployment or Stage 10 execution. |

Checkpoint D: raw exported pages and real browser/account flows pass. Missing
browser connection/fixture means relevant E2E is BLOCKED and local completion is
not claimed. Record privacy/CSP/account regression findings explicitly; do not
silence tests or downgrade honest unsupported labels to make content look complete.

## Risk and rollback plan

| Risk | Mitigation / rollback |
| --- | --- |
| Global Expo SSR breaks native/session imports | Pure docs-only renderer; keep global single output. Reject prototype if it needs whole-app migration. |
| Static docs and Expo navigation diverge | Share manifest/renderer; full-document boundary; test both directions, active accounts and Sandbox unload guards. |
| Catch-all rewrite steals static docs or returns soft 404 | Exact static route resolution before SPA, local filesystem/Vercel policy tests and unknown-route HTTP status checks. |
| Account/provider failures block docs or leak user state | Pure static anonymous content; isolated optional header state; no session in HTML/index/cache. |
| SDK/API docs drift or public/private mixing | Allowlist plus DTO/export contract tests; separate internal evidence; no wholesale Swagger generation. |
| Published example copies secret or weak OIDC behavior | Env-only secret, real library validation, fixture protocol checks and artifact secret/route scan. |
| CSP/SEO hydration mismatch | External same-origin assets, matching render tree/IDs, raw-HTML tests and actual browser headers/console. |
| Unsupported native or npm availability gets advertised | Explicit environment/version/status matrix backed by current Stage 8/package evidence. |

Before touching integration, keep the pre-change artifact/config baseline. Rollback
is a scoped revert of Stage 9 authored paths and restoration of the prior artifact/
docs-routing overlay, preserving all unrelated dirty work. Do not issue a broad
git reset/clean or remove user files. No schema/data rollback is needed. Document
deployment rollback for the operator; do not perform deployment automatically.

## Required review

The four decisions in spec section 10 were approved before implementation. The gate came
directly from the user's Stage 9 section XXXII: **“Trình human review. Không bắt
đầu implementation trước khi được duyệt.”** The audit/spec/plan are the concrete
review package; approval is recorded above. No additional deployment or Stage 10
authorization is implied by local implementation acceptance.
