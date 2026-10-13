# Mini App schema consolidation proposal

Status: recommended 28-to-25 consolidation implemented in the working tree;
local migration/regression verification recorded in
`docs/handoffs/mini-app-schema-consolidation.md`. Production is not migrated.

## Evidence and scope

The current deployment scripts create 24 platform tables and four OpenIddict
tables (28 total, excluding EF migration history). This inventory describes
repository schema, not an inspection of the user's live database or row counts.

Public and internal legacy authorization/exchange paths are retired with HTTP 410.
Runtime sessions and OpenIddict remain active. Most other tables support distinct
active features, rather than redundant storage.

## Recommended target: 25 tables

| Existing table | Proposed replacement | Conditions |
| --- | --- | --- |
| MiniAppLaunchCodes | Remove legacy custom-code protocol; use existing OpenIddict flow | Trace every caller, including runtime launch responses; migrate remaining consumers and tests before dropping storage. Preserve runtime-session binding and PKCE/replay protections. |
| DeveloperReviewHistory | Typed events in MiniAppAuditLogs | Preserve developer/version/reviewer, previous/new status, reason, private internal notes, original ID and timestamps. Add indexed event type/version fields and expose existing history DTOs. Preserve all decisions, including suspension/revocation outside application review. |
| MiniAppHistoryStates | Nullable MiniAppHistoryClearedBefore on Accounts | Copy each account's cutoff exactly; null retains current default. Clear history must not delete launch or audit logs. |

The Accounts column trades feature isolation for one less table. If that tradeoff
is undesirable, retain MiniAppHistoryStates and target 26 tables instead.

## Optional target: 24 tables

Merge MiniAppBanners and MiniAppPlacements into MiniAppEditorialItems with an
explicit Kind (Banner or Featured), common scheduling/order fields and conditional
constraints. Banner requires title/image and exactly one app/category target;
Featured requires an app and no category. Keep existing API contracts and all
visibility, publication, scheduling and admin authorization checks.

This is a moderate refactor for one fewer table; defer unless unified editorial
management is useful independently of table count.

## Keep separate

- The four OpenIddict tables: retain the supported protocol store unchanged.
- MiniAppVersions and DeveloperApplications: immutable submitted snapshots must
  remain separate from mutable drafts and published projections.
- MiniAppTechnicalTests: drafts need hash-bound test attempts before a submitted
  version exists; version snapshots alone cannot replace this store.
- MiniAppVerifiedDomains, DeveloperEmailVerifications, DeveloperReauths and
  MiniAppRuntimeSessions: distinct proof, expiry, attempt and consumption rules.
- Permissions, app-permission mappings and user consents: separate app approval
  from each user's grant/revocation; retain relational integrity.
- External identities: stable partner subject mapping; retain unique constraints.
- Favorites, reports, categories, memberships and launch logs: active functions
  with distinct query, authorization and retention requirements.

Do not replace unrelated entities with a generic JSON table solely to reduce
table count. Fewer tables do not by themselves imply better performance.

## Ordered implementation

1. Inventory actual database schema/rows and all readers/writers. Generate a
   reviewed backup and rehearse restoration. Confirm retired protocol usage is
   zero. Do not rewrite migrations already applied to any database.
2. Remove the legacy service path and migrate its consumers to OIDC. Verify
   independent apps, runtime SSO, PKCE, code replay, close and consent revoke.
3. Add account cutoff column; backfill exactly; switch catalog read/clear code.
   Verify cleared recents, subsequent launches and unchanged analytics/audit.
4. Extend audit event representation; migrate review records with source IDs;
   switch registration/admin history reads and writes. Verify resubmission,
   suspension/revocation, ordering and private-note visibility.
5. Optional editorial merge: backfill by kind; preserve API DTOs; verify schedule
   boundaries, category/app targeting and published-only discovery.
6. Validate source/target row counts and field equality on a restored test DB.
   Review indexes/foreign keys and run BE/FE/Admin regression checks. Provide a
   migration SQL artifact and update setup/handoff documentation.
7. Deploy replacements before deleting old tables. Drop via new explicit migration
   only after all consumers are switched and verification passes. Production
   application remains a separate, explicitly authorized operation.

For concurrent traffic, use a maintenance window or transactional dual writes
and a final delta backfill; do not rely on an uncoordinated one-time copy.
Keep old tables through a verification window so rollback can switch code back.
After drops, rollback requires reverse data migration or verified backup restore.

## Acceptance criteria

- No removal of active user-facing features or relaxation of security controls.
- Existing API response shapes and partner OIDC integration remain compatible.
- Complete review history, private-note boundaries and history cutoffs preserved.
- No missing/duplicate migrated records; database-backed regression checks pass.
- Recommended count is 25; optional editorial consolidation reaches 24, excluding
  EF history tables and unrelated existing ANKT tables.
