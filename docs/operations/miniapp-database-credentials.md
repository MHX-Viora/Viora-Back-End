# Mini App partner credentials in the database

## Behavior
- The public App ID is the existing Client ID. No app-specific ANKT env entry is
  needed for a new app. Opening its integration page provisions a credential for
  the selected environment; Sandbox and Production are independent.
- Secrets contain 32 cryptographically random bytes. Database rows store SHA-256
  hashes for authentication and versioned AES-GCM ciphertext for owner reveal.
  Keep `MiniApps__ActiveKeyVersion` and all referenced `MiniApps__EncryptionKeys__*`
  in the backend secret manager. Never discard an old key while rows still use it.
- Owners can copy, rotate or revoke. Rotation requires updating the partner BE;
  old secrets fail immediately on subsequent validation. Revocation clears hash
  and ciphertext. It persists until explicit rotation, including after env import.
- No plaintext secret appears in public app DTOs, audit metadata or frontend build
  settings. Owner responses are POST-only/no-store, session-bound and rate-limited.

## Rollout on the existing Viora Docker deployment
1. Retain existing `MiniAppAuth__Partners__*` settings temporarily. Build the new
   image without replacing the running service:
   `cd /var/www/viora && sudo docker compose build viora-api`.
2. Apply additive migration `20261028034422_AddMiniAppPartnerCredentials` using the
   normal verified database migration procedure. It only creates the credential
   table, app FK and unique app/environment index. No live migration was run here.
3. While legacy env is still available, explicitly import it into the database:

   ```bash
   cd /var/www/viora
   sudo docker compose run --rm --no-deps viora-api --import-miniapp-partner-credentials
   ```

   This uses the service's existing connection/key/env configuration and exits;
   it does not start another web server or apply migrations. Success prints only
   an imported count. Check the exit code before continuing. The command validates
   Client IDs, ambiguity and secret/hash consistency before one transactional write.
   Re-running skips existing rows, including rotated/revoked ones. Concurrent imports
   may conflict on uniqueness; rerun after the other operator finishes.
4. Start the new backend and deploy the FE together:
   `sudo docker compose up -d --no-build --force-recreate viora-api`.
   Verify existing partner login plus owner copy/rotation on a test app. Owner UI
   and the API response now include credential `version` and `revoked`.
5. Remove legacy `MiniAppAuth__Partners__*` entries from ANKT BE env after checking
   import. Remove `EXPO_PUBLIC_NTS_SANDBOX_CLIENT_SECRET` and
   `EXPO_PUBLIC_NTS_PRODUCTION_CLIENT_SECRET` from FE build env. Clean-build and
   redeploy FE so previous public values leave the current bundle. Recreate the BE
   after env removal. Runtime authentication has no env fallback.
6. Rotate previously exposed secrets through the owner's integration page, then
   update the corresponding partner backend. Import preserves old values for
   continuity; it does not undo earlier disclosure.

Hash-only registrations import with their existing hash so partner login continues.
Their original plaintext is not recoverable: the owner sees no secret and must
explicitly rotate to obtain a new value. Import never guesses or silently replaces
an established secret. Supplying plaintext in a later import does not overwrite
an existing row; use rotation instead.

## API
`POST /api/developer/mini-apps/{appId}/auth-bridge/credentials/reveal`:
`{environment:"Production"}` (or Sandbox).
`/rotate` and `/revoke` accept `{environment,version}` from the last response.
All return `{clientId,environment,clientSecret,version,revoked}`; revoked/hash-only
rows return an empty secret. Non-owner/deleted app: 404. Expired/locked/suspended
owner: 401. Invalid environment: 400. Stale update: 409 CREDENTIAL_CONFLICT.

## Troubleshooting / rollback
- Missing table: apply the migration to the same verified DB used by the service.
- CREDENTIALS_UNAVAILABLE: restore the appropriate encryption key/version; do not
  generate a replacement global encryption key to bypass missing key material.
- PARTNER_LOGIN_NOT_CONFIGURED: owner must open that environment's integration
  section; revoked credentials require explicit rotation.
- Import failure: verify Client IDs resolve to non-deleted apps, unique config labels,
  hashes and encryption keys. Logs never include plaintext or connection strings.
- Back up the database before rollout. Keep the additive table when reverting code;
  dropping it loses credentials. Old env-only code cannot authenticate credentials
  generated/rotated after migration. Reverting binaries alone is not a safe rollback.
