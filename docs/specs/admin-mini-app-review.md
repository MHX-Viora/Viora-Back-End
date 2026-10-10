# Admin Mini App review correction

## Problem and intended workflow
Admin detail conflates draft editing and review, lacks domain evidence, and resolves owner by ambiguous name. Approval of legacy PendingReview without a submitted snapshot can activate unverified configuration. Correct workflow: developer approval → draft save → developer domain proof → immutable version submission → admin examines submitted snapshot/owner/domain/permissions → explicit approve/reject → publication. Saving configuration never verifies or publishes.

## Scoped plan
1. Reproduce legacy approval bypass and add tests for exact version decisions, owner lookup and administrator domain recheck.
2. Backend: authenticated admin review context with exact Developer and domain verification timestamps; admin recheck uses existing safe verifier/rate limit/audit without exposing challenge token. Approve/reject require submitted version number and forbid approval while suspended/archived. Restoration is distinct from version approval. No migration.
3. Admin: review-first submitted snapshot, exact owner, domain evidence/recheck, required reviewer confirmations and blocked-state explanations. Draft editor is collapsed and clearly separate. Queue displays pending updates on Active apps.
4. Focused BE/admin tests, build/lint and browser fixture verify; handoff.

## Acceptance and boundaries
Missing snapshot, wrong version, inactive owner, missing domain proof and disabled permission/category cannot publish. Admin recheck failure clears proof; rate-limited attempts preserve existing proof. Failed requests are visible and approval stays blocked. Reject does not require valid domain proof. Do not approve/verify any production application during tests, change business ownership, or auto-submit a developer's draft.
