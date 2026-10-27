# Retired Mini App SSO store cleanup

`20261026040000_RetireMiniAppLegacySsoStores` removes legacy stores from the
runtime model by moving their existing tables from `public` to `legacy_sso`.
It preserves rows, indexes, foreign keys and legacy account-link audit records.
It never drops tables or deletes records. No application/Production database
was changed during implementation.

## Inventory

The archive migration includes these 13 tables:

| Tables | Former consumer |
| --- | --- |
| OpenIddictApplications, OpenIddictAuthorizations, OpenIddictScopes, OpenIddictTokens | OIDC server |
| AnktSsoSessions, SsoLoginTransactions | OIDC browser login |
| MiniAppPairwiseSubjects, MiniAppUserConsents | OIDC subject/consent |
| MiniAppAccountLinks, SsoAccountLinkChallenges, SsoAccountLinkAudits | OIDC partner account linking; audit data retained |
| SandboxTestIdentities, SandboxIdentitySessions | Legacy Sandbox OIDC cookie login |

Keep `Accounts`, `Users`, `SandboxIdentityTemplates`, `MiniAppBridgeSessions`,
all Developer, Registry, Domain, review/publication, permission and audit tables.
`MiniAppOAuthClients`, `MiniAppClientSecrets` and `MiniAppRedirectUris` remain
because current publication/version/readiness and managed Sandbox services still
consume their persisted registry data. Their names alone do not make them safe
to delete. They are not OpenIddict token stores. They require a separate consumer
migration before any future schema removal.

Historical SSO migrations remain unchanged so an existing database can upgrade
and a fresh installation can replay the chain without OpenIddict packages.

## Review and apply

1. Confirm the target connection privately; never paste it into logs/chat. Take
   a restorable database backup and record the deployed revision/migration ID.
2. Inspect exact row counts for the archived tables using a read-only database
   session and `SELECT COUNT(*) FROM public."<table>"` for each inventory entry.
   Record retention requirements, especially account-link audits. The repository
   does not assert these tables are empty in a live deployment.
3. Confirm all deployed clients use Auth Bridge and legacy routes have zero
   consumers. Deploy the replacement code before archiving the tables.
4. Review the migration SQL offline using a synthetic design connection:

   ```powershell
   $env:VIORA_EF_DESIGN_CONNECTION='Host=127.0.0.1;Database=offline_model;Username=offline'
   dotnet ef migrations script 20261026021305_AddMiniAppAuthBridge 20261026040000_RetireMiniAppLegacySsoStores --project Viora.Infrastructure --startup-project viora-BE --output legacy-sso-archive.review.sql
   ```

   This command generates SQL; it does not apply it. Apply reviewed SQL only
   through the deployment's approved database change process. `SET SCHEMA`
   acquires locks; schedule an appropriate deployment window.
5. Verify the 13 tables exist in `legacy_sso`, row counts match, active tables
   stay in `public`, and Sandbox/Production launch, exchange and UserInfo work.

## Rollback

Before redeploying a legacy binary, generate/review the reverse script from
`20261026040000_RetireMiniAppLegacySsoStores` to
`20261026021305_AddMiniAppAuthBridge`. Its `Down` moves the same tables back to
`public` with all rows intact. Restore matching prior application configuration
from protected deployment backups. Do not invoke `Down` on the additive Bridge
migration: that would delete Bridge sessions.

The empty archive schema remains after rollback to protect unrelated operator
data. If names already exist in the destination schema, the migration fails
transactionally; resolve the collision through the database change process.

## Destructive purge

Physical deletion is deferred. Obtain separate approval after retention and
rollback windows expire; backup and review every foreign key before preparing
any purge. No automatic DROP migration, startup purge or deletion SQL is shipped.
Archiving these tables does not authorize deleting Production data or secrets.

## Verification

`LegacySsoArchiveMigrationTests` verifies the runtime model and generated SQL,
then upgrades an independent local PostgreSQL cluster through the old chain,
inserts a sentinel legacy scope, archives, rolls back and archives again.
It checks all retired/active table locations and sentinel preservation. It never
accepts a live connection string; test results must be reported from an actual run.
