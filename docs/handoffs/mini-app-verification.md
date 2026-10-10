# Mini App verification — 2026-10-09

## Verdict

The backend supports Mini App registration and launch-code exchange. The user application cannot currently launch Mini Apps. Onboarding is available through APIs, with no creation forms in the admin UI.

## Findings

1. **Blocking: user runtime is missing.** `viora/features/utilities/utilities-screen.tsx:59` renders a plain `View` with “Sắp ra mắt”, without a press handler. There are no `app/mini-apps` routes, Mini App service/types/features, or `react-native-webview` dependency in the current `viora` tree. Users cannot discover apps, grant consent, launch a WebView, or use the SDK bridge.
2. **Important: onboarding requires API calls.** `MiniAppsPage.tsx` offers refresh/list/detail only; `DevelopersPage.tsx` offers list and status transitions only. There are no forms to create or link a Developer account, register an app, or submit it for review. Developer Portal UI is explicitly deferred by the spec; API onboarding is intentional, but the admin UI alone cannot complete registration.
3. **Important: handoff does not match current source.** `mini-app-platform.md:6` claims a Mini App center and secured WebView exist. Those components are absent from the current mobile project. The spec's `npm test` command also does not exist in the current mobile package scripts.

## Verified

- Admin: `npm run build` passed, including TypeScript compilation and Vite production build.
- Backend: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false -nodeReuse:false` passed: 56 passed, 1 PostgreSQL test skipped.
- Existing backend tests contain no Mini App-specific tests.
- A temporary console harness exercised the real `MiniAppManagementService`, `MiniAppService`, `ClientCredentialService`, and `MiniAppSecurityPolicy` against an isolated in-memory SQLite database. All 24 checks passed:
  - Pending Developer cannot register; registration creates Draft; Draft stays hidden and cannot launch or skip review; approved app becomes visible.
  - Missing consent blocks launch; unapproved permission is rejected; launch points to CallbackUrl with 60-second expiry; raw launch code is not stored.
  - Invalid credentials, reused code, and expired code are rejected; exchanged profile contains consented fields only.
  - Subject is stable within an app and distinct across apps; revocation and newly added permissions require consent.
  - Suspended app and Developer block launch; ownership is enforced; secret rotation invalidates the old credential and enables the new one.
  - HTTPS and allowed-domain boundaries are enforced.
- The harness reset EF tracking between simulated requests where bulk updates occurred, matching scoped API context behavior. It was removed after verification.

## Limits

- No connected browser was available; admin interactions were not tested live.
- No Android/iOS WebView or partner callback site was exercised.
- SQLite checks do not establish PostgreSQL concurrency behavior, deployed API availability, or migration state.
- No production data was accessed or changed; no application implementation was modified.

## Next implementation scope

Connect the Utilities tile to a Mini App center, add API/consent handling and a secured shared WebView with a validated SDK bridge, and provide an onboarding UI if registration must be completed without API tools. Add permanent Mini App regression tests and correct the platform handoff.
