# ANKT Mini App Platform reintroduction

## Objective and accepted scope
On 2026-10-12 the user explicitly selected implementation of the full FE/BE/Admin/SSO/SDK platform, superseding documentation-only scope. Source requirements: `viora/docs/prompts/ankt-mini-app-platform-utilities.md`, retaining all 13 master sections. The user-facing catalog belongs immediately after Wallet in the existing Utilities route; no independent catalog page or navigation tab. Developer registration/Console use the existing ANKT website/auth; Admin uses the existing Admin app.

## Baseline audit
- Backend HEAD ab0393b5 and Admin HEAD aab4485 retain the previous platform in Git. Their removal is currently uncommitted. Reuse reviewed source selectively, preserving unrelated WIP and the explicit removal of Swagger's custom filter.
- Prior BE includes Developer lifecycle/membership, private verification, immutable configuration snapshots, review/technical tests, domain verification, permissions/consent, pairwise identity, runtime sessions, analytics/reports, credential rotation and an OpenIddict 7.7.1 protocol store with separate issuer/audience boundary.
- Current runtime model excludes MiniApp/Developer. Retirement history contains metadata-only retirement and explicit table removal. Last verified production inventory removed 20 feature tables while keeping 58 social/wallet tables. Never undo or edit applied migrations; scaffold an additive reintroduction migration from the current retired snapshot.
- OpenApiDbContext and AppDbContext share the existing connection/database; there is no second database. Protocol tables/history remain separate because the maintained authorization server owns them.
- Previous Expo host source was not committed and is absent. Build a host integrated into Utilities, reuse contracts/SDK rather than invent a token protocol. Public /developers docs already exist and must remain anonymous/SEO-capable.
- Missing: scalable catalog pagination/search/category, categories with icon/order, favorites/reorder, recents, banners/scheduling, featured placements and usage deduplication/retention; native/web host; first-party Console; Admin editorial controls.

## Architecture and contracts
- Preserve existing DTO/exception conventions: typed DTOs, 400 validation, 401 unauthenticated, 403 ownership, 404 unavailable resource and 409 stale version. Extend existing MiniApps services/controllers instead of duplicating auth/review stores.
- Map requested DeveloperAccounts/Members, MiniAppScopes/ClientScopes/UserIdentities/Audit/Reviews to existing equivalent entities. Add only genuinely missing entities (favorites, recents, usage events, banners/placements/media if not represented). No duplicate OAuth store or credentials.
- Catalog returns only active published snapshots belonging to approved Developers; exclude hidden/suspended applications from every rail, history and favorite response. Paginate and bound all user-supplied filters. Popularity comes from deduplicated authenticated launches, never synthetic figures.
- Only exact HTTPS verified URLs/origins may launch. Protocol is OpenIddict authorization code + PKCE; host/runtime handoff is not a partner access token. Never expose ANKT JWT, host runtime secret, private verification or signing secrets to partner JavaScript. Client secret shown once and stored hashed by credential manager.
- Native WebView must isolate cookies/credentials and allow only verified origins; web uses a partner window when embedding is unsupported. Close returns to the existing Utilities state. SDK bridge validates exact origin/source/runtime/nonce and bounds requests/timeouts/capabilities.
- Independent and SSO apps are explicit modes. Auto login requires partner integration, current host identity, approved callback/scope and consent; partner owns its session. No automatic email-based account merge.

## Commands and structure
- BE: `dotnet build viora-BE/viora-BE.csproj -c Release`; `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj -c Release`; EF: `dotnet ef migrations add ReintroduceMiniAppPlatform --project Viora.Infrastructure --startup-project viora-BE --context AppDbContext` with a design-only fixture connection; generate SQL before any application.
- FE: `npx tsc --noEmit`; focused `node --test` tests; scoped ESLint; `npm run build:web`.
- Admin: `npm run build`; existing Node feature tests. SDK/examples: existing Node test suites and .NET partner tests.
- New BE tests under Viora.Application.Tests; platform services under Infrastructure/MiniApps; catalog contracts under Application/MiniApps; FE host under features/mini-apps and Console under features/developer; Admin under existing features/pages/services. Handoff under docs/handoffs.

## Code style
Use the existing record DTOs and injected service interfaces, e.g. `Task<MiniAppCatalogPage> GetCatalogAsync(Guid accountId, MiniAppCatalogQuery query, CancellationToken cancellationToken);`. EF configurations define keys, ownership, indexes and concurrency. Prefer small focused components and feature modules over new abstractions.

## Test strategy and acceptance
Restore existing security/review/OIDC tests and verify them before extending contracts. Add failing tests for catalog visibility/pagination, favorite ownership/order, recent/popularity dedupe, banner safety/schedule and Utilities placement. Exercise replay/wrong PKCE/redirect/client secret/audience, cross-tenant access, consent/runtime/account switch and review/publication races. Use isolated SQLite/PostgreSQL fixtures and local HTTP/OIDC hosts; do not create production accounts or rows for testing. Web browser checks follow each UI integration; actual Android/iOS device verification remains distinct from TypeScript/build checks.

## Boundaries
- Authorized: source restoration, dependency additions needed by specified WebView/OIDC/SDK, additive migration generation, FE/Admin/BE implementation, local fixtures/tests/builds and deployment documentation.
- Preserve unrelated changes and applied migration history. Do not restore the removed Swagger authorization filter.
- Production secrets, production migration application, deploy/push and external partner changes require separate concrete operator approval; never infer a hostname/credential or fake successful readiness.
