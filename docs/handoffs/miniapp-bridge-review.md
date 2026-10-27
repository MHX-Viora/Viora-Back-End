# Mini App Auth Bridge review completion — 2026-10-27

Scope: source and local verification only, per user request. No deployment,
production data access, live migrations, commits or partner source edits.

## Changes

- `MiniAppReviewRegistration`: review inherits Bridge enablement unless explicitly
  overridden; registers the Bridge evidence reader when Bridge is enabled.
- `MiniAppBridgeReviewEvidence` and `MiniAppReadinessService`: use fresh successful
  Sandbox S256 exchanges bound to owned app, current Developer source, app policy,
  verified domain and available profile. Revalidate the exact source/session and
  snapshot in the serializable submission transaction. Existing executor path
  remains available when Bridge is disabled.
- Evidence schema 2 distinguishes server protocol proof from legacy browser
  executor proof. Submission seals the matching schema; approval/publishing keep
  historical evidence, snapshot, ownership and concurrency checks.
- `MiniAppCatalogProjection`: Bridge publication writes the public catalog without
  requiring the disabled legacy runtime. Projection shares the publication
  transaction; unpublish/revocation remains enforced.
- Developer/Admin parse schemas 1/2. Review shows actionable Sandbox guidance,
  server evidence sources and token/profile checks. Sandbox links back to review.
- Updated outdated regression fixtures for Bridge catalog enablement, retained
  client-secret storage and native session-invalidation subscriptions. Added a
  native logout regression.

## Verification executed

- Backend focused regression: **185 passed**, including new Bridge readiness and
  full draft/submit/replay/admin approval/publish/catalog/real-user login/unpublish/
  token-revocation path; missing/stale/future/foreign/revoked/unexchanged/wrong-
  environment/configuration/domain proof is rejected. PostgreSQL/migration cases
  were excluded from this run.
- Developer contract/publishing tests: **8 passed**.
- Admin review/service/parser tests: **19 passed**.
- Web/native host, catalog and review/publishing proxy tests: **55 passed**.
- Frontend `tsc --noEmit`, Admin `tsc -b`, targeted frontend ESLint: passed.
- Expo web export and Admin Vite production build: passed. Admin retains a bundle
  size warning (>500 kB); no bundling changes made in this task.
- Chrome UI fixture: submit, same-request retry, inbox/deep link, publish/unpublish,
  changes/new draft, widths 320/768/1024/1440; no runtime warnings/errors.
  DevTools also confirmed enabled submission and visible server proof labels.

## Limits and next release work

- Chrome UI uses deterministic HTTP fixtures; backend lifecycle uses isolated
  SQLite. Neither proves the deployed server or an installed phone build.
- Attempted real AuthBridgeE2EHost build, but its existing ProjectReference points
  to missing `../../../Digital-Creative/Digital_Crative_BE/...csproj`. Restore the
  partner fixture dependency/path before running the real partner browser E2E.
  No workaround or partner source change was made.
- No Android/iOS device or emulator verification. Native adapters were tested in
  component/transport fixtures only.
- See [operator guide](../operations/miniapp-bridge-review.md) and
  [spec/task order](../specs/miniapp-bridge-review.md). Existing dirty changes across
  all three repos were preserved. Local logs/artifacts use `.codex-tmp/bridge-review-*`.
