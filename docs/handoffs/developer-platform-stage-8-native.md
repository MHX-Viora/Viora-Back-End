# Stage 8 native continuation checkpoint

2026-10-23. Web acceptance remains unchanged. Native is **IMPLEMENTING, NOT
ACCEPTED**. Runtime remains opt-in; the server native gate stays closed.

## Implementation and fixes

The existing working tree contains the local Expo Android/iOS host modules,
trusted-shell navigation/message validation and a native adapter/private channel.
This continuation verified that foundation and repaired two lifecycle defects:

- Shell disposal removed the native listener before the final close/error reached
  the parent. The port now permits exactly one bounded terminal receipt after
  disposal, with the same exact parent origin. Wrong origins, oversized receipts,
  new launch/status messages and duplicate terminal receipts are denied.
- Native platform errors bypassed adapter terminal cleanup. They now use the
  same idempotent finish path: clear deadline, dispose/abort channel, stop view,
  clear configuration and ignore late status/error callbacks.

Both defects have RED/GREEN regression evidence. The original terminal-receipt
test failed before the port fix. The new adapter-error test failed with zero
native stops before the adapter fix. A shell integration test additionally checks
resume/close against fake HTTP/native boundaries: scoped close request, confirmed
receipt, removed frame/listeners and repeated-close idempotency. This Node
integration test is not device E2E.

Backend regression also exposed a quota-test timing defect: SQL-translated
DateTime.UtcNow loses precision in SQLite and can precede a just-saved CreatedAt.
The test now captures a timestamp parameter after save; production constraints
and quota assertions remain unchanged. The failure is in
`.codex-tmp/stage8-continuation-backend-unrestricted.log`.

## Verification

- Android module compile and app debug assembly: **success**, offline, Java 17,
  x86_64, 536 actionable tasks. APK:
  `../viora/android/app/build/outputs/apk/debug/app-debug.apk`. Log:
  `../viora/.codex-tmp/stage8-continuation-android-build.log`.
- Frontend Mini App/Wallet/root SSO: **50 passed**, zero failures. Sandbox initially
  blocked esbuild; unrestricted rerun passed. Log:
  `../viora/.codex-tmp/stage8-continuation-feature-tests.log`.
- Production host Node: **16 passed**, zero failures. Log:
  `.codex-tmp/stage8-continuation-host-tests.log`.
- Root TypeScript and focused native-adapter/test lint passed. Logs:
  `../viora/.codex-tmp/stage8-continuation-{types,lint}.log`.
- Backend Production regression: **146 passed**, zero failures/skips, including
  disposable PostgreSQL cases. Log:
  `.codex-tmp/stage8-continuation-backend-final.log`. Five focused quota cases
  also pass in `.codex-tmp/stage8-continuation-quota-green.log`. Initial sandbox
  PostgreSQL startup failures were environmental; unrestricted execution exposed
  the separate SQLite fixture defect documented above, now fixed.

Reproduce from the respective repository roots:

```powershell
# viora (Java 17 and installed Android SDK)
$env:JAVA_HOME = 'C:/Program Files/Java/jdk-17'
$env:EXPO_NO_DOTENV = '1'
Push-Location android
.\gradlew.bat :ankt-mini-app-host:compileDebugKotlin :app:assembleDebug --offline --no-daemon --max-workers=2 -PreactNativeArchitectures=x86_64
Pop-Location
node --import tsx --test --experimental-test-isolation=none features/sso/bootstrap.test.ts features/mini-apps/tests/*.test.ts features/mini-apps/tests/*.test.mjs components/wallet/wallet-summary-card.test.mjs
node node_modules/typescript/bin/tsc --noEmit
node node_modules/eslint/bin/eslint.js features/mini-apps/adapters/host.native.tsx features/mini-apps/tests/native-host.test.mjs

# viora-BE
node --test --experimental-test-isolation=none tools/ProductionHostTests/*.test.mjs
$env:DOTNET_ROOT = (Resolve-Path .codex-tmp/sdk8-check/sdk).Path
$env:MSBUILDDISABLENODEREUSE = '1'
& "$env:DOTNET_ROOT/dotnet.exe" test Viora.Application.Tests/Viora.Application.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~MiniAppProduction'
```

## Environment and remaining gates

`adb devices -l` now reports **emulator-5580**, Android **15**, x86_64. Its selected
WebView is `com.google.android.webview` **124.0.6367.219**. This supersedes historical
no-device observations. Availability is not device permission/SSO proof. No app
install, emulator permission change or TLS trust change was made here. Debug
assembly does not prove flags/backend availability or successful device launch.

Required next work, in dependency order:

1. Run the trusted native shell against an isolated trusted-TLS Android fixture.
   Prove main-frame/source spoof rejection, denied media/location/file chooser/
   download and lifecycle recovery with OS permissions already granted to ANKT.
2. Resolve [native SSO P0](../decisions/stage-8-native-sso.md) with actual issuer,
   browser and Public/Confidential partner sessions: source/browser user mismatch,
   cancel/replay, reload/reopen and cookie isolation. No broker, new credential,
   callback change or Public partner backend is approved by this checkpoint.
   Any necessary trust change needs a concrete reviewed contract first.
3. Compile/run Swift on Mac/Xcode and an appropriate simulator/device. Verify
   actual supported OS/delegate availability and permission denial. Windows
   TypeScript/Android results supply no iOS build evidence.
4. After these gates, run native business flows A-D and update acceptance.

Review covered terminal bounds/origin, inbound disposal, idempotency, late async
callbacks and Web compatibility. No open finding in the two repaired paths;
native platform/security/SSO acceptance remains outstanding. No deployment,
live DB migration, commit or Stage 9 work.
