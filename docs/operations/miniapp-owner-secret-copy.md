# Owner-only Client Secret copy

The integration Console places an environment selector and masked Client Secret
beside App ID. Only an authenticated, active/unlocked app owner can request reveal.
The BFF requires same-origin POST/CSRF headers; the backend uses the existing
sensitive-operation rate limit and audits only app/actor/environment. Responses are
no-store. UI clears the value after 60 seconds and on app/environment change or exit.

## Operator provisioning

Existing partner authentication continues using the registered SHA-256 hash. Hashes
cannot be reversed. To enable owner copy, supply the corresponding original value
from the backend secret manager under the SAME existing label/App ID:

```dotenv
MiniAppAuth__Partners__YourLabel__ClientId=YOUR_REGISTERED_APP_ID
MiniAppAuth__Partners__YourLabel__SandboxSecretHash=SHA256_HEX_OF_SANDBOX_SECRET
MiniAppAuth__Partners__YourLabel__SandboxSecret=ORIGINAL_REGISTERED_SANDBOX_SECRET
MiniAppAuth__Partners__YourLabel__ProductionSecretHash=SHA256_HEX_OF_PRODUCTION_SECRET
MiniAppAuth__Partners__YourLabel__ProductionSecret=ORIGINAL_REGISTERED_PRODUCTION_SECRET
```

These are backend-only secret-manager settings, never frontend, SDK, docs or Git
values. Use independent high-entropy secrets per app and environment. Do not reuse
one app's secret for other developers. Preserve the existing operator label; do not
register a duplicate Client ID under another label. Restart/redeploy the backend.
Reveal refuses any value that does not match its registered hash. Hash-only
configurations remain valid for partner login but reveal returns
PARTNER_SECRET_UNAVAILABLE. No database migration is needed.

When rotating, update plaintext and hash together, plus the partner backend's
Client Secret. A secret disclosed outside the intended private channel should be
replaced, not copied into source code as a shared default.

## Private Portal contract

POST `/api/developer/mini-apps/{appId}/auth-bridge/credentials/reveal` with a valid
owner DeveloperSession and JSON `{ "environment": "Production" }` or `Sandbox`.
The browser uses the same-origin `/developers/api/account/mini-apps/...` BFF route.
Response: `{clientId, environment, clientSecret}`. No GET/prefetch or general app-list
DTO contains this value. No public SDK method exposes the server credential.

Missing/mismatched provisioning: 503 PARTNER_SECRET_UNAVAILABLE. Wrong environment:
400 INVALID_ENVIRONMENT. Invalid session: 401. Non-owner: 404 without disclosure.
The feature reveals an existing registered credential; it does not generate or
rotate credentials and does not alter the partner login protocol.
