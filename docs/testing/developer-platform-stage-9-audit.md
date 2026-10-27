# Stage 9 — pre-implementation audit

2026-10-23. Read-only source audit plus planning documents. No Stage 9 product
implementation, deployment, account operation, email or application DB access.
Classification describes the local checkout, not a deployed service guarantee.

## Findings across the 20 requested areas

| # | Area | Source / finding | Stage 9 consequence |
| --- | --- | --- | --- |
| 1 | Routes | `../viora/app/developers/{index,[...path]}.tsx` route to the integrated portal. `lib/site.ts` sets `/developers`. | Use `/developers/docs[/slug]`; no root `/docs` alias by default. |
| 2 | Layout | `developer-screen.web.tsx`, `components/site-shell.tsx`, `components/docs-shell.tsx`. Existing docs shell has two pages, no search/TOC. | Extend existing feature and identity; separate docs content/rendering. |
| 3 | Authentication | Root layout excludes `/developers` from Social auth; ConsoleGuard is applied only to console paths. DeveloperScreen wraps all pages in DeveloperSessionProvider. | Public docs rendering must not await or require session; optional account controls isolated. |
| 4 | Mini App CRUD | `viora-BE/Developer/MiniAppApi.cs`, `MiniAppContracts.cs`, frontend `mini-app-client.ts`. | Contract-backed owner-only CRUD reference; distinguish logo upload and create/configuration stages. |
| 5 | Domain proof | `MiniAppIntegrationApi.cs`, `MiniAppDomainLifecycle.cs`, `MiniAppIntegrationContracts.cs`, frontend `mini-app-integration.ts`. | Exact root filename `ankt-verify-{32 lowercase hex}.txt`; download current attachment, preserve contents; no `.well-known` or password step invented. |
| 6 | OAuth/OIDC | `SsoRegistration.cs` enables code flow with required S256 PKCE, reference access tokens, openid/profile/email. | No implicit/password/refresh grant instructions. Reference tokens are not JWTs to decode locally. |
| 7 | Discovery | OpenIddict options and `MiniAppSsoApi.Server()` expose actual issuer/discovery/JWKS metadata. Existing HTTP tests verify discovery alignment. | Read deployment metadata; use placeholder issuer, never infer a production issuer from portal URL. |
| 8 | SDK | `packages/miniapp-sdk/package.json`: `@ankt/miniapp-sdk`, `0.1.0`, `private: true`, openid-client 6.8.8. Exports config/auth/bridge/popup. | Source/workspace integration exists; public registry availability unverified. No unconditional registry install command. |
| 9 | Bridge | `src/bridge.ts`: connectHostBridge; v1 context/close plus locally derived environment/inside helpers; v2 Sandbox adds external navigation/diagnostics. | Publish per-method protocol/environment support, not a single universal capability list. Native acceptance remains gated. |
| 10 | Sandbox | Developer-owned routes in `MiniAppSandboxApi.cs`, separate host/runtime and internal executor routes. | Document owner workflow and safe diagnostics; exclude executor and private host transport from public API reference. |
| 11 | Review | `MiniAppReviewApi.cs`, `MiniAppVersionApi.cs`, notification API and frontend review/version components. | Document owner readiness/version/submission/status/revision; omit Admin decisions/assignment APIs. |
| 12 | Publishing | `MiniAppPublishingApi.cs`, immutable publication services and frontend publishing component. | Approved differs from Published; restore does not republish; draft changes do not replace active publication. |
| 13 | Production | `Production/MiniAppPublicApi.cs` anonymous bounded catalog/detail; host/session APIs are first-party runtime transport. | Catalog reference may be public; no partner integration instructions for private launch/redeem/runtime internals. |
| 14 | OpenAPI | `Program.cs` adds Swagger; middleware enabled only in Development. Shared document can include unrelated/private APIs. | Never mirror Swagger wholesale. Produce explicit allowlisted reference with schema/auth/validation tests. |
| 15 | Existing docs | `pages/docs.tsx` still states Stage 2 and labels CRUD/domain/SSO/Sandbox/review planned. Quick Start is foundation-era. | Replace stale content through source-backed pages after approval. |
| 16 | Configuration | Existing BFF uses server-side ANKT_API_BASE_URL; Expo public config is inlined at build. SSO/host features have separate flags/options. | Read only safe config code/names; do not ingest `.env` or appsettings secrets into docs/search/bundle. |
| 17 | Deployment | Current frontend workflow targets VPS; `server/developer/vercel-base.json` also exists as an artifact policy baseline. Its broad SPA fallback excludes existing static assets. | Prepare both filesystem-first VPS routing guidance and composed optional Vercel artifact policy. No workflow execution/deployment changes. |
| 18 | SEO | `app.json` has `web.output: single`; existing metadata uses Head at runtime. `public/robots.txt`, `developer-sitemap.xml` list public pages. | Current crawlability not proven. Generate full docs HTML and verify raw responses with JS disabled. Preserve existing sitemap entries and private route exclusions. |
| 19 | Tests | Node/tsx Developer+BFF suites, SDK tests, Stage 8 fixture/browser tools, root tsc/lint; Chrome DevTools tools available. | Browser connection and actual exported-artifact behavior must be verified later. Tool availability is not an E2E pass. |
| 20 | Rules/skills | User-provided AGENTS applies; spec/plan approval is explicitly required by Stage 9 section XXXII. | Stop after reviewable spec/plan. Preserve 277 BE / 260 FE / 13 Admin existing status entries observed at audit start. |

## Capability classification and evidence limits

| Capability | Classification | Evidence / limits |
| --- | --- | --- |
| Developer password account, verification, sessions | Implemented and verified locally | Existing Stage 2–5 tests/handoffs; current identity service requires verified email for password login and configured mail for registration. Mini App SSO does not enable Developer federation login. |
| Mini App CRUD, credentials, domain proof | Implemented and verified locally | Stage 3/4 tests/handoffs plus current endpoints/DTOs. Public internet domain/deployment is not newly tested here. |
| SSO code+PKCE, Public/Confidential, consent | Implemented and verified locally | Stage 5/8 actual isolated issuer/partner evidence. No public issuer reachability claim. |
| SDK browser API | Implemented and verified locally | Existing SDK tests and Stage 8 partner evidence; package remains private. |
| Sandbox web, review and publishing | Implemented and verified locally | Stage 6/7 acceptance reports and current routes; Mobile Preview is not a native simulator. |
| Production catalog/Web host | Implemented and verified locally | Stage 8 isolated Web acceptance; most recent continuation: 146 Production backend, 50 frontend and 16 host checks pass. These tests were run in the preceding continuation, not rerun for this planning-only audit. |
| Android/iOS host and native SSO | Implemented but unverified as a complete feature | Android debug build passes; emulator connected; permission/business/native SSO proof absent. iOS build/E2E absent. See Stage 8 native checkpoint. |
| Complete searchable crawlable public docs | Planned | Two old docs pages exist; Stage 9 implementation not started. |
| Public npm distribution | Unsupported as a documented available channel | Local manifest is private; registry publication is not authorized or verified. |
| Refresh grant, implicit/password grants, device bridge capabilities | Unsupported | Server/bridge configuration does not support these as partner features. |
| Legacy separate Next portal and Stage 1/2 deployment descriptions | Deprecated/superseded | Frontend Stage 1/2 plans explicitly point to integrated FE handoff; Stage 3 confirms Expo architecture. Historical records are retained. |

## Historical document audit

- Frontend `docs/plans/developer-platform-stage-{1,2}.md` exist and explicitly mark
  the separate portal architecture historical. Backend handoffs for both exist.
- Backend plans for Stages 3–8 and specs for Stages 5–8 exist; Stage 3/4 plans
  contain their scope/contracts. Do not invent absent separate Stage 1–4 specs.
- Stage 5 handoff has real protocol/browser evidence; Stage 6 and Stage 7 have
  explicit local acceptance reports. Stage 8 Web is locally accepted; its native
  handoff supersedes historical no-device observations without granting acceptance.
- Earlier totals are historical evidence, not an assertion that all old tests
  were executed in this Stage 9 audit.

## Reference strategy boundaries

Portal browser requests use `/developers/api/account/...` with same-origin
HttpOnly Developer cookie and the existing CSRF header. Upstream management APIs
use `/api/developer/...` and `Authorization: DeveloperSession <opaque-session>`.
Neither is the partner OAuth bearer scheme or a Social JWT. Publish these as
different transports; never instruct copying an HttpOnly cookie into JavaScript.

SSO credential provisioning is backend tooling only and explicitly rejected for
browser-origin requests; BFF does not proxy it. Legacy app credentials, per-
environment SSO clients and reviewed version Production credentials need separate
labels. Public documentation must not suggest they are interchangeable.

Endpoint-level schema/error/rate tables still require explicit extraction and
contract tests as plan tasks before their public content is marked verified.
This audit does not certify undocumented fields merely because a route exists.
