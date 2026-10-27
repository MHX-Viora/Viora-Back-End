# Mini App review and publication with Auth Bridge

This change is local source only. It adds no migration and has not been deployed.

## Configuration for a later release

- Backend: existing `MiniAppAuth__Enabled=true`, `SandboxTest__Enabled=true`,
  correct `MiniAppAuth__AllowedHostOrigin`, and the existing Mini App/Bridge
  database migrations. Keep existing encryption keys and credentials.
- `MiniAppReview__Enabled` inherits `MiniAppAuth__Enabled` when absent. An explicit
  `false` keeps submission, moderation and publication routes disabled; an
  explicit `true` enables review. Confirm the effective host environment.
- Auth Bridge publications now append their reviewed catalog projection in the
  same transaction. `MiniAppRuntime__Enabled` and legacy executor/OIDC settings
  are not required for this Bridge path.
- Web build: `EXPO_PUBLIC_MINI_APP_AUTH_BRIDGE_ENABLED=true` and the actual backend
  in `EXPO_PUBLIC_API_URL`. Native builds additionally need
  `EXPO_PUBLIC_MINI_APP_NATIVE_RUNTIME_ENABLED=true`; rebuild the installed app.
  Build flags are not remotely enabled by publishing a Mini App.
- Deploy matching backend, Developer frontend and Admin parser support for
  evidence schema 2 together. Historical schema 1 remains readable.

## Developer and Admin sequence

1. Save name, category, logo, short/full descriptions, HTTPS website, support
   email, privacy and terms. Complete domain verification.
2. Create the draft/version in **Xét duyệt và phát hành**.
3. Open Sandbox in the same Developer login. Complete Auth Bridge login; use the
   SDK and check the actual Mini App interface and functions.
4. Return to review, refresh status and submit within five minutes. Editing the
   app or creating/renaming the draft invalidates old configuration proof: reopen
   Sandbox afterward. Expired/revoked sessions cannot supply evidence.
5. Admin opens the queue, starts review and approves, rejects or requests changes.
   Approval does not publish. Submitted snapshots remain immutable.
6. Developer chooses the approved version and confirms Production publication.
   It enters the common web/native catalog. Production login uses the real ANKT
   account, independent of the Sandbox identity.
7. Unpublishing removes catalog availability and revokes existing Production
   Bridge sessions. Re-publishing requires explicit approved-version checks.

Schema 2 seals server-verified exchange and current profile availability. It does
not assert that an external browser executor tested the website, or that the
partner retrieved UserInfo. The UI identifies this evidence as server protocol.
Do not manually backfill approvals/publications or reuse Sandbox tokens in
Production. Existing already-published rows without a catalog projection are not
backfilled by this change; handle any such historical data separately.
