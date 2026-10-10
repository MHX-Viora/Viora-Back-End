# Mini App SSO deployment diagnosis

Checked on 2026-10-10 for Nghệ Thuật Số opening inside ANKT.

## Finding

Public read-only checks of `https://api.mxh.ankt.vn` returned:

- `/mini-app-sdk/ankt-mini-app.js`: HTTP 200, JavaScript, version `1.0`, no `configure` or `requestLogin`.
- `/swagger/v1/swagger.json`: the request schema for `/api/mini-app-auth/exchange` only contains `clientId`, `clientSecret`, and `code`.

This deployed contract is older than the checked-in SSO implementation. A partner using the new SDK login flow cannot complete it against that SDK/API pair. This establishes a deployment mismatch; it does not prove the partner has no additional integration or cookie issues.

## Repository verification

- The checked-in SDK is version `2.0`, with trusted-host configuration and `requestLogin`.
- `ExchangeLaunchCodeRequest` includes exact callback, state and PKCE verifier binding.
- Mini App hybrid backend tests: 45 passed, 1 PostgreSQL concurrency test skipped because `VIORA_MINI_APP_TEST_DB` is not configured.
- SDK and Node partner configuration tests: 17 passed.
- Release publish succeeded. The published `wwwroot/mini-app-sdk/ankt-mini-app.js` matches the source file byte for byte and includes version `2.0`, `configure` and `requestLogin`.
- SSO implementation was already committed; no backend behavior change was needed for this diagnosis.

## Required follow-up

Deploy the current backend and its full static assets with the reviewed hybrid migration, following [the deployment guide](../MINI_APP_DEPLOYMENT.md). Confirm the public SDK and Swagger contract after restarting the service and invalidating stale caches.

Then verify the actual partner integration: an app configured for `AnktSso`, verified callback domain, exact registered callback, trusted ANKT host origin, server-created state/S256 PKCE, SDK login, server-side exchange, and a partner-owned session usable in the embedded browser. Opening the runtime alone does not authenticate the partner user. Existing consent may be reused; missing data-sharing consent still requires confirmation.

The runnable Node example intentionally uses login/continue buttons. Automatic sign-in must be initiated by the partner when embedded and without a partner session, with an explicit retry after errors or consent denial.

No production deployment, migration application or end-to-end login against the live partner was performed. The actual Nghệ Thuật Số source is outside this workspace.
