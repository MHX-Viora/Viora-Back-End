# Ordered implementation — Mini App Platform in Utilities

Requirements approved by the user's explicit full-platform selection. Each slice is checked before the next; do not request repeated scope approval. Existing uncommitted social/env/doc changes remain outside feature restoration.

1. Baseline recovery: restore only removed BE/Admin feature source plus related DI/configuration; preserve retirement migrations/snapshot and removed Swagger filter. Update obsolete runtime-retirement assertions to reintroduction acceptance. Verify BE/Admin builds and inherited tests.
2. Catalog contract: define bounded page/category/search/rail DTOs and add failing visibility/pagination tests. Implement published-snapshot catalog query; use existing review/publish ownership rules. Verify focused tests.
3. Favorites: add favorite entity/configuration and authenticated add/remove/reorder DTOs/services/controllers. Test ownership, duplicates and stale order; verify before UI work.
4. Recent/popular: add recents/deduplicated usage, record only validated launches, retention/aggregation and personalized rails. Test blocked apps and manipulation controls.
5. Editorial content: add banner/placement/media gaps and category icon/order; scheduled-safe targeting; Admin CRUD/permissions and UI; test SSRF/open redirect/status/date boundaries.
6. Database checkpoint: scaffold new additive AppDbContext migration after retirement; generate bounded reviewed SQL plus protocol SQL, exercise on an isolated local PostgreSQL database and verify pending model consistency. Never apply production automatically.
7. Utilities host: add API client/contracts, catalog component directly below Wallet, mobile/desktop icon grids, search/category/history/favorites/detail overlays, independent loading/error states. Test no separate catalog route and unaffected Wallet.
8. Runtime: reviewed native isolation and web partner window with source/origin/nonce-bound bridge; SDK callback/state/PKCE/consent/account-switch and close/retry flows. Verify Node bridge tests and browser fixtures.
9. Developer Console: replace unavailable screens with existing-auth registration/app management/team/credentials/analytics workflows on ANKT itself; server owns authorization. Use existing Admin review UI and add editorial controls.
10. OIDC/SDK checkpoint: reuse maintained authorization server/SDK and partner examples; complete capability API and transport guards; re-run inherited protocol/partner tests including replay and internal API boundary. Ensure no main JWT or secret reaches partner JS.
11. Documentation: update public docs to describe delivered contracts without claiming production deployment; SDK/framework examples, environment/migrations/Nginx/operator steps and concise technical handoff.
12. Verify/review: BE/Admin/FE tests/builds, generated HTML, localhost browser registration/review/publication/catalog/runtime/SSO flows, security review and production configuration checklist. Report actual native/staging/deployment limits explicitly.

Dependencies: schema/DTO services precede FE/Admin integration; publication/security visibility precedes every discovery rail; maintained OIDC baseline precedes host bridge. No new separate frontend or production DB. Large slices are split into small entity/service/controller/UI increments as implementation proceeds.

## Completion evidence

All twelve source/schema/UI/documentation slices implemented. BE 193 tests passed with local PostgreSQL/no skips; SDK/BFF 49; FE docs/OAuth 22 and bridge 2; Admin 22. FE TypeScript/scoped lint, FE/Admin export/build, React/ASP.NET examples and TypeScript SDK build/pack succeeded. Local PostgreSQL exercised the migration histories; bounded app/protocol SQL generated and reviewed. Recent discovery is 90 days; security audit logs remain intact. Browser verified login/Console, Admin editorial save and Wallet/catalog layout/paging/favorite/detail at mobile and desktop sizes.

Production migration/deployment, real SMTP/domain proof, full staging HTTPS partner flow and native devices are rollout verification, not claimed complete. See `docs/handoffs/mini-app-platform.md` and `docs/mini-app-partner/PLATFORM-SETUP.md`.
