# Hybrid Mini App implementation plan

Follow-up clarified: (1) connect the existing profile settings entry to an internal `/developer` route; (2) use existing authenticated developer APIs for ordinary-user registration and owned-app lifecycle; (3) verify configuration/status/API boundaries and profile regressions, TypeScript/lint/web export. Remove the obsolete external opener/URL setting. No deployment or admin access.

Dependency: audited source → shared DTO/API contract → independent implementation slices → integration → verification/review → documentation.

## Backend slices

1. Extend registry/category/auth modes and version snapshots; tests for standard-mode registration and review.
2. Developer registration/profile, memberships/team, verified-domain challenge, scoped analytics/audit.
3. Runtime sessions, state/redirect/PKCE-bound codes, revocation/suspension; focused auth regression tests.
4. Admin review/history/categories/permissions/reports; additive EF migration and SQL validation.

Each slice: at most five related source groups, compile and focused tests before extending. Preserve existing platform APIs wherever compatible with required security.

## Client slices

1. Typed API + safe URL and bridge validation helpers with failing behavioral tests.
2. Marketplace/search/category/favorites/recent/consent/revocation with API data.
3. Native WebView runtime, toolbar/progress/error/navigation handling.
4. Web iframe runtime, source/origin verified bridge, fallback and SDK integration.

Verify TypeScript/lint/helper tests after each related slice. Preserve app theme and session coordinator.

## Portal/admin slices

1. Developer-only login/layout/profile onboarding; existing admin login remains strict.
2. Five-step registration wizard and owned app lifecycle.
3. SSO/domain verification/permissions/team/analytics/audit screens with real APIs.
4. Admin creation/review/security/timeline/report/category/permission controls.

Verify TypeScript/build/lint; every mutation maps to an implemented API.

## Integration and completion

- Reconcile all DTOs and routes, test source ownership and auth isolation, adversarial security review.
- Add partner Node and ASP.NET examples; document account linking/unlinking and SSO transaction binding.
- Run complete checks once after final changes; record failures/skips accurately.
- Update architecture/developer/SDK/security/admin/deployment/testing/handoff documents.
- No commit, deploy or production migration unless requested. Final report distinguishes implementation, build verification and production/device limits.
