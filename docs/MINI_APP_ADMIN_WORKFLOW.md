# Admin review workflow

Admin API routes require the existing ANKT admin role. Developer access cannot approve apps, change administrative permission policy or activate profiles.

## Developer onboarding

Review self-submitted profiles or create a Developer and associate the owner's ANKT AccountId. Check contact/company details, approve or reject. Suspend a compromised Developer to block all its apps without deleting application data. Team management remains owner-only.

## App review

Use Pending Apps, app details and version history. The identity header, Developer profile, external website preview, domain security configuration, requested permission cards and review timeline should be reviewed together.

Check:

1. Accurate app identity/category and appropriate website destination.
2. Every exact public HTTPS domain has completed verification.
3. Callback URI and bridge origins match the submitted integration; auth mode/client method are appropriate.
4. Requested permissions are minimal; standard independent apps do not request identity scopes.
5. Published version updates do not silently broaden callback, domain or permission access.

Approve the pending immutable configuration snapshot, or reject with a specific reason. Previously published configuration remains active while an update is under review. A rejection must not silently unpublish a previously approved version.

The detail page shows the exact owning Developer and required domain verification timestamps before the read-only submitted configuration. Admin may recheck each existing HTTPS challenge; failure clears the previous proof and blocks approval, but does not prevent rejection. Complete the website/auth/permission reviewer checks before confirming publication. Draft editing is a separate collapsed section; saving does not verify, submit or publish.

`GET /api/admin/mini-apps/{id}/review` supplies the owning Developer and domain proof status without challenge tokens. `POST /api/admin/mini-apps/{id}/domains/{domainId}/verify` reuses the existing verifier, 30-second per-domain rate limit and actor audit. Approval/rejection requests require the pending snapshot number, for example `{ "version": 2, "reason": "Reviewed" }`. A changed/missing version returns 409; approval rechecks the active Developer, domain proof, category and permissions. Deploy the updated admin client with this backend contract.

Suspended apps must be restored separately before approving an update; restoration keeps the previously published snapshot and does not approve a waiting version. Legacy `PendingReview` rows without a submitted snapshot cannot be approved. Saving their configuration returns them to a draft (or preserves an existing published version), records `LegacyReviewReturnedToDraft`, and lets the Developer complete proof and submit a real version.

## Incident handling

Review user reports and audit records. Suspend an app immediately when needed; restore only after investigation. Runtime session validation observes suspension, and launch/exchange fail. Document report resolution and reason in the action panel. Archive retired apps rather than deleting historical records.

Manage categories and the permissions developers may request. Deactivating a permission removes it from current grant projection; it must not create a bypass through a cached bridge response.

## Separation of authority

Admin changes are recorded with actor and resource IDs. No frontend button visibility is treated as authorization. One-time client secrets are not exposed in app detail/audit/history. ANKT does not reset partner passwords, delete partner accounts or pretend to revoke a partner's independently created sessions.
