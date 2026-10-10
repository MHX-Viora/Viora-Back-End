# Hybrid Mini App verification

Verified in this workspace on 2026-10-09. Automated source/build checks and live environment acceptance are separate.

## Automated checks

| Area | Result | Scope |
|---|---|---|
| Backend | Build clean; 101 passed, 2 skipped | Existing application regressions plus 45 hybrid checks; Mini App and money-audit PostgreSQL fixtures conditional |
| Hosted SDK | 13 passing checks across two suites | Actual hosted script in VM: parent/source/origin binding, native handshake, app/session/nonce, schema, replay/rate/timeout/navigation |
| Expo client | 18 passing checks | Registered launch/fallback and URL/navigation policy, actual host handler with consent/suspension/revoke, native patch guards and queued injection destination checks |
| Expo static checks | TypeScript and lint pass; web export succeeds | Does not compile Android/iOS native source |
| Admin/Developer Portal | Build/lint pass; 35 tests pass | 14 Mini App configuration/auth/UI checks and 21 existing regressions |
| Node partner sample | 11 tests pass | Local account/identity/link conflicts/unlink and HTTP login/CSRF/browser transaction binding |
| ASP.NET partner sample | Build and 7 tests pass | SQLite identity creation/link conflicts/unlink/account preservation |

## Commands

Backend, from `viora-BE`:

```powershell
dotnet build viora-BE.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
node tests/mini-app-sdk.test.cjs
node --test --experimental-test-isolation=none examples/mini-app-sdk.test.mjs examples/mini-app-node/identity-store.test.mjs examples/mini-app-node/server.test.mjs
dotnet test examples/mini-app-dotnet-tests/PartnerMiniApp.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
```

Expo, from `viora`:

```powershell
node node_modules/typescript/bin/tsc --noEmit
npm run lint
node --test --experimental-test-isolation=none features/mini-apps/runtime-policy.test.mjs features/mini-apps/runtime-bridge.test.mjs features/mini-apps/runtime-injection.test.mjs plugins/with-mini-app-isolation.test.cjs
npx expo export --platform web --max-workers 0 --output-dir dist-mini-app-verification
```

Admin, from `viora-admin`:

```powershell
npm run build
npm run lint
node --test --experimental-test-isolation=none tests/mini-app-auth.test.mjs tests/mini-app-config.test.mjs tests/mini-app-ui.test.mjs src/pages/admin-articles.test.mjs src/pages/withdrawal-detail.test.mjs src/components/withdrawal-fee-settings.test.mjs
```

Node's no-isolation option avoids this sandbox's child-process restriction; it does not replace real browser testing. Some build tools also required permission to launch their ordinary subprocesses.

## PostgreSQL and migrations

`MiniAppHybridPostgresTests` requires `VIORA_MINI_APP_TEST_DB` naming a dedicated `test`/`audit` database. It creates a unique temporary schema, checks one winner for simultaneous redemption and one pairwise identity for simultaneous first login with distinct codes, then drops only that schema. Never use an application/production database.

That environment variable is absent here; the fixture is skipped. SQLite tests verify behavior but do not establish PostgreSQL lock/concurrency semantics. Migration generation/model comparison/SQL review do not apply the migration to a database. See [deployment](MINI_APP_DEPLOYMENT.md) and the [handoff](handoffs/mini-app-hybrid.md) for the migration artifact.

## Browser/device acceptance still required

The configured in-app browser runtime returned no connected browsers. No authenticated visual/end-to-end walkthrough was possible. `adb devices` returned no attached device. Android/iOS native compile and hardware permission behavior were not verified.

Before release, use real verified HTTPS partner hosts and a staging ANKT account:

1. Approve a Developer, create Independent/SSO apps, prove all domains, review/publish/update/reject/suspend/archive.
2. Exercise marketplace search/categories/recent/favorites, detail/consent/revoke/report and developer/admin ownership boundaries.
3. Test independent partner login and explicitly requested SSO, fresh/existing account, confirmed link/unlink, no email matching and identity conflicts.
4. Attempt wrong state/verifier/redirect/client/session, expired/reused code, duplicate concurrent redemption, revoked consent and suspension while open.
5. Test permitted/blocked iframe headers, third-party-cookie restrictions and external-tab/provider login without assuming generic cookie/session transfer.
6. Rebuild native with the pinned WebView patches, then verify child-frame/redirect bridge rejection and denied camera/microphone/file/download paths on Android and iOS.

Native media/file capabilities are intentionally unavailable in the embedded runtime. Their scope-driven session consent and safe transfer design remain future work. Opening the system browser does not grant SDK/SSO bridge access there.
