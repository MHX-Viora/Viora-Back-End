# Database-backed Mini App credentials

- `MiniAppPartnerCredentialStore` provisions independent app/environment records;
  unique index converges concurrent reveals, Guid concurrency token protects changes.
- Existing AES-GCM protector encrypts owner-recoverable values with row-ID-bound AAD;
  SHA-256 hashes authenticate random secrets. Revocation erases both hash/ciphertext.
- `MiniAppPartnerLoginService` reads DB only, rechecks live Developer session and app
  ownership for management; audit writes share the rotation/revocation SaveChanges.
- Additive migration `20261028034422_AddMiniAppPartnerCredentials` generated offline.
- Explicit `--import-miniapp-partner-credentials` command preserves old env credentials;
  import is transactional/idempotent and never overwrites or revives existing rows.
- FE removes the Nts/public-env branch; all apps use owner API and versioned confirmed
  rotate/revoke actions. BFF extends same-origin POST/no-store protections.
- Preserve unrelated proxy diagnostics and Mini App status UI edits in the worktree.
- See `docs/operations/miniapp-database-credentials.md` for deployment order. No live
  database, deployment, import, production secret or remote service was changed.

## Verification
- 48 focused BE tests pass, including isolated PostgreSQL concurrent provision and
  rotation, one-use partner exchange, owner/session boundaries, environment/app
  isolation, revoked-state persistence and atomic/idempotent legacy import.
- CLI dispatch/error handling checked against an unreachable test-only database.
- FE TypeScript, changed-file ESLint, five BFF policy tests and actual browser flow
  pass (copy, change app/environment, stale replies, rotate/revoke confirmation).
- Clean Expo export passes. Scan of 8 exported JS files found zero configured legacy
  partner secret values. Production API origin guard passes after distinguishing
  older immutable SDK archives from the current manifest SDK; regression test covers
  incorrect API origins in the current app, current SDK and unversioned SDK.
- Independent review and the requested Codex CLI review completed. CLI's hash-only
  import concern is an intentional documented compatibility case: hashes remain
  valid; owner explicitly rotates to obtain recoverable plaintext. Tests cover this.
