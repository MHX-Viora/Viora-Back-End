# Stage 9 — security, accessibility and evidence review

2026-10-23. Scope: public docs source, static output, router integration, optional
account decoration and local test fixtures. No new product API, auth protocol,
database schema or CORS change. No deployment/security certification of a target
hosting environment.

| Boundary / behavior | Guard and evidence |
| --- | --- |
| Public content independent of session | Pure DocsPage and static HTML; optional AccountControls fetch only after load. Expo docs branch bypasses DeveloperSessionProvider/ConsoleGuard. Anonymous/401 E2E remains readable. |
| Account confidentiality | Static initial markup is anonymous; real BFF retains HttpOnly credential. Account E2E uses password login, not injected cookie. No private account in manifest/search/static HTML. |
| Management vs protocol auth | Reference distinguishes BFF cookie+CSRF, upstream DeveloperSession and Mini App OAuth token. No Social JWT substitution or browser Confidential credential provisioner. |
| Endpoint allowlist | 54 intended management/catalog operations checked against registration; Admin/executor/private host routes excluded. Seven OIDC metadata/protocol entries matched to configuration, with real issuer evidence below. |
| Request/response drift | Reflection tests compare documented request fields and all 57 generated DTO schemas with current Application types. Semantic request notes remain explicit; generated shape is not full business validation. |
| Reference correctness | Corrected raw logo upload, no category catalog filter, page envelopes, actual version-update DTO, neutral-email 202 and Sandbox-run 202/real DTO names. Protocol and server error labels checked from source. |
| HTML/code injection | Typed blocks, React escaping, no raw HTML content block, eval or Markdown HTML renderer. Syntax lexer preserves bytes; search malicious-markup input produces no script node. |
| URL safety | Slug/anchor validation, safe link schemes, known docs link/heading validation; origin rejects credentials/query/path and non-HTTPS except loopback fixture. No user-controlled renderer endpoint. |
| Secrets/internal leakage | Public output contains placeholders and intended schemas only; check rejects private-key markers and forbidden route prefixes. Examples do not execute on read. No environment enumeration or private audit import in client. |
| Script/CSP | Raw HTML has external module script/CSS only. Docs static server and composed policy use self-origin scripts, no object/frame, strict base/form/frame ancestry. Browser monitors CSP logs/network; Developer/Sandbox baseline preserved. |
| Routing/auth scope | Exact `/developers/docs` boundary, full-document navigation both ways, root Social auth unchanged. Existing BFF rewrite targets preserved. Unknown docs return HTTP 404, not SPA 200. |
| Build output safety | Output/base-export guards reject outside workspace `.codex-tmp`; build-input tests fail invalid paths before bundling/copy. No recursive delete in artifact builder. |
| Search/privacy | Public title/heading/body index only; Vietnamese folding, query length/result bound, escaped highlights, no remote search service or session data. |
| Search keyboard/focus | Actual Ctrl+K, arrows, Escape/focus restore and result navigation. Fixed shortcut invoked from nonfocusable body to restore to trigger; test waits actual close/focus event. |
| Drawer/TOC lifecycle | Native dialog traps focus and restores trigger; stable anchors, actual scroll tracking and canceled animation frame on teardown. Mobile and desktop exercised. |
| Copy/error states | Real clipboard success and actual browser permission-denial fallback verified; no execCommand/eval/user code execution. |
| Light/dark/accessibility | Actual CSS text contrast pairs >=4.5:1; landmarks/heading AX tree; four widths, both themes, reduced-motion and accepted 200% OS text-scale emulation. This is not an audit of every assistive technology/browser. |
| SEO | Every raw HTML response includes full article, title/description/canonical/OG/Twitter before JS. Browser JS-disabled page, refresh, sitemap/robots and 404 checked. No search-engine indexing promise. |
| Fixture isolation/cleanup | Owned Chrome temp profile bounds checked before recursive cleanup. Existing Developer fixture uses disposable SQLite, local SMTP, random port and keyed shutdown. No existing browser or arbitrary PID killed. |
| SDK/SSO compatibility | SDK 37 tests; existing real Confidential issuer/review/partner fixture verifies consent/code/token/ID token/UserInfo, reload/reopen and revocation. No native support/SSO broker added. |

## Review findings resolved

- Reference was not permitted to infer response names/status/body format. Current
  DTO generation, route-source tests, real account response evidence and source
  checks now protect the documented set.
- Search shortcut from a nonfocusable body needed an explicit focus fallback.
  Actual Chrome test failed before correction and passes after it.
- The local fixture originally omitted portal manifest CSP/static routing.
  With Network/Log monitoring enabled, that mismatch was exposed and corrected
  only in the fixture to match the existing portal baseline. Docs CSP stays strict.
- Shared identity/footer primitives were extracted without importing account/
  Expo router code into the docs hydration bundle. Global theme rules were scoped
  to docs; no root Social/Developer theme replacement.

No open high-severity finding in the reviewed Stage 9 paths. This is a local code
review, not penetration testing of deployed infrastructure.

## Material limits

- Existing dependency tree: 39 npm advisories (6 moderate/33 high) at the verified
  audit checkpoint; no esbuild vulnerability entry. No unrelated forced upgrades.
  Separate SDK dependency audit reports zero; openid-client 6.8.8 registry version
  and MIT license were verified before documenting its standalone install command.
- .NET sample compiles against .NET 8/OpenIdConnect 8.0.30. NuGet vulnerability
  metadata was initially unavailable; that is not proof of no advisories. The
  sample's cookie/issuer integration was not directly driven end-to-end; the real
  Confidential protocol fixture uses the existing partner implementation.
- Existing RN Web pointerEvents and Expo PWA-meta deprecation warnings occur on
  portal pages; they are explicitly excluded, not reported as a zero-warning app.
- Native Android/iOS acceptance, public npm publication, production SMTP/issuer,
  deployed hosting redirects/headers and cross-browser cookie behavior remain
  outside this local documentation acceptance.
