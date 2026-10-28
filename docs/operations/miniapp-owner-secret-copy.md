# Owner-only Client Secret management

All Mini Apps use the authenticated Developer API. No app has a direct FE env
exception. Client ID stays public; Client Secret is returned only to an active,
unlocked owner with a live Developer session, never in public/list DTOs.

Opening the integration section lazily provisions a random secret for the selected
Sandbox or Production environment. Repeat reveal returns the same encrypted-at-rest
value. Copy stays available until the app/environment changes or the page exits.
The page supports confirmed rotation and revocation; update the partner backend
with each newly rotated secret. Other apps and environments are unaffected.

POST `/api/developer/mini-apps/{appId}/auth-bridge/credentials/reveal` accepts
`{environment:"Production"}` or Sandbox. `/rotate` and `/revoke` under the same
credentials prefix accept `{environment,version}` from the last response.
Responses: `{clientId,environment,clientSecret,version,revoked}` with no-store.
Revoked and imported hash-only credentials return an empty clientSecret; rotate
explicitly to get a fresh value. A revoked row never silently regenerates.
Stale changes: 409 CREDENTIAL_CONFLICT; invalid environment: 400; invalid owner
session: 401; non-owner/deleted app: 404. All mutations are sensitive-rate-limited
same-origin POST routes and audit only actor/app/environment, never secret values.

See [database credential rollout](miniapp-database-credentials.md) for schema,
legacy import, encryption keys and deployment order.
