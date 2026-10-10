# Developer registration completion — 2026-10-10

## User flow
ANKT Profile -> Settings and activity -> Developer Portal (`/developer`). Existing ordinary ANKT session is used; registration never accepts a selected owner. Web portal uses the same ANKT account via `/developer/login`.

Submit name/contact email and optional company/phone/public HTTPS website -> Pending -> admin approves -> Active -> create mini-app draft -> prove domains -> submit immutable version -> separate app moderation/publication. Developer approval never grants admin privileges.

## Changes
- Existing profile PUT now changes Rejected -> Pending after successful owner correction; same profile/membership preserved, `DeveloperResubmitted` audit. Pending/Active edits preserve status; suspended profiles and team members remain unable to edit.
- Admin decisions enforce Pending -> Active/Rejected, Active -> Suspended, Suspended -> Active. Rejected must be resubmitted first. Accurate DeveloperRejected/DeveloperRestored audit; admin action buttons match transitions.
- App and portal provide explicit resubmission labels/pending confirmations. Portal blocks app/team routes until Active, offers status refresh and displays a disabled profile for Suspended. Phone limit is 20 across app/portal/database. Backend rejects display-name email syntax.

## Verification
- 57 .NET tests: DeveloperRegistrationTests, MiniAppHybridIntegrationTests, MiniAppHybridSecurityTests; no skips. SQLite service integration covers ownership, Pending creation gates, correction/reapproval/app creation, duplicate normalized emails, invalid transitions, invalid correction and suspended edit restrictions.
- 16 app Node tests and 17 portal Node tests. App TypeScript/lint and portal TypeScript/build/lint pass. Portal build has existing >500 kB chunk advisory.
- Chrome against local production preview with `../viora-admin/tests/developer-browser-fixture.js` as initScript: real form registration POST -> Pending; rejected correction PUT -> Pending; direct app-create route blocked for Suspended; Active opens mini-app wizard. No console warnings/errors observed. Fixture intercepts all API requests and uses a synthetic, invalid bearer; no live server data changed.
- Browser resize checked actual widths ~502/770/1026/1442 without horizontal overflow. Tool enforces a minimum browser width, so true 320px/device verification remains untested.

## Limits and commands
No deployment, schema migration or live PostgreSQL verification in this change. Existing extensive uncommitted mini-app work preserved; no commits made.

`dotnet test Viora.Application.Tests --no-restore --filter 'FullyQualifiedName~DeveloperRegistrationTests|FullyQualifiedName~MiniAppHybridIntegrationTests|FullyQualifiedName~MiniAppHybridSecurityTests' --nologo --verbosity quiet -m:1 /nodeReuse:false`

App: `node --test --experimental-test-isolation=none features/developer/config.test.mjs features/developer/api.test.mjs features/developer/ui.test.mjs`; `node node_modules/typescript/bin/tsc --noEmit`; `npm run lint`.

Portal: `node --test --experimental-test-isolation=none tests/developer-registration.test.mjs tests/mini-app-ui.test.mjs tests/mini-app-config.test.mjs tests/mini-app-auth.test.mjs`; `npm run build`; `npm run lint`.
