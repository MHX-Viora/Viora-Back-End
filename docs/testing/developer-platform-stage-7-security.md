# Stage 7 security verification

2026-10-21 continuation. This is a traceability map, not E2E acceptance. Actual
browser workflow results are recorded separately in the Stage 7 handoff.

Final backend regression after Admin list/capability integration: 695 passed, one existing
unrelated MoneyAudit skip, zero failures. Artifact:
`Viora.Application.Tests/TestResults/stage7-final.trx`.

| # | Requirement | Executed assertions (test class / method) |
| --- | --- | --- |
| 1 | Submit exact owned draft | MiniAppReviewSubmissionTests.SubmissionSealsExactSnapshotAndAttestationAndReplayNeedsNoNewRun |
| 2 | Block incomplete/stale evidence | MiniAppReviewReadinessTests.MissingOrStaleAuthorityNeverProducesReadyForReview; AnotherLiveSessionOfTheSameOwnerCannotSubstituteForTheEvidenceSource |
| 3 | Double/concurrent submit | MiniAppReviewSubmissionPostgresTests.ConcurrentSubmissionsSealOneVersionAndOneReviewEventWithDatabaseImmutability |
| 4 | Foreign owner submit / IDOR | MiniAppReviewSubmissionTests.InvalidSubmissionNeverWritesReviewOrNotifications (foreign); MiniAppReviewAccessTests.OwnerAccessIsAppBoundAndDoesNotGrantSandboxTesterAuthority |
| 5 | Immutable snapshot/evidence | MiniAppReviewSubmissionPostgresTests; MiniAppVersionPostgresTests; MiniAppReviewMigrationTests |
| 6 | Live Admin starts / assignment | MiniAppReviewAssignmentTests; MiniAppReviewAssignmentPostgresTests.TwoAdminsCannotClaimTheSamePendingReview |
| 7 | Approve exact sealed target | MiniAppReviewApprovalTests.ApprovalTargetsTheSealedVersionAfterDraftChangesAndOriginalSourceExpiryWithoutPublishing |
| 8 | Reject with reason | MiniAppReviewDecisionTests.AssigneeDecidesOnceWithReasonAndStructuredChangesAndOwnerMustCreateANewVersion (false) |
| 9 | Request structured changes | Same test (true); NonAssigneeAndUnstructuredOrUnknownChangesCannotDecide |
| 10 | Missing/short reason rejected | MiniAppReviewDecisionTests.NonAssigneeAndUnstructuredOrUnknownChangesCannotDecide; MiniAppModerationTests.OwnerOrRevokedAdminOrStaleRevisionsCannotSuspend |
| 11 | Insufficient role denied | MiniAppReviewAuthEndpointTests.ModulePoliciesRequireBearerAdminAndRecheckLiveRoleAndSession |
| 12 | Developer cannot self-approve | Same signed HTTP test: Developer authentication scheme, Moderator, missing/wrong sid, revoked session and stale role |
| 13 | Dual Admin decisions | MiniAppReviewDecisionPostgresTests.CompetingTerminalDecisionsHaveOneWinnerAndImmutableHistory |
| 14 | Approved-only publication | MiniAppPublishingTests.PublishRejectsStaleRevisionAndUnapprovedTargetWithoutSideEffects |
| 15 | Draft cannot replace approved version | MiniAppReviewApprovalTests; MiniAppPublishingProjectionTests; MiniAppPublicationPolicyTests.OnlyTheApprovedLivePublicationAuthorizesProduction |
| 16 | Suspend authority atomically | MiniAppModerationTests.LiveAdminSuspendsPublicationAndSandboxOnceWithoutSandboxTesterRole; MiniAppModerationPostgresTests |
| 17 | Restore stays unpublished | MiniAppRestoreTests.RestoreRequiresAdminAndLeavesApprovedTargetUnpublishedUntilExplicitRepublish |
| 18 | Issuance/runtime denied after suspension | MiniAppModerationProjectionTests.SuspendRevokesBothEnvironmentsAndRestoreDoesNotResurrectConsentOrOldSandboxSession; MiniAppPublicationPolicyTests |
| 19 | Exact scoped revocation | MiniAppUnpublishingProjectionTests; MiniAppModerationProjectionTests; existing SSO protocol suites |
| 20 | Inbox ownership / idempotency | MiniAppReviewNotificationEndpointTests.InboxIsBoundedRecipientScopedAndMarkReadIsMonotonic; MiniAppReviewNotificationTests.InboxAndOutboxFollowTheAuditTransactionAndDeduplicateTheSameEvent |
| 21 | Atomic audit/outbox, forced rollback | MiniAppReviewSubmissionTests.AuditFailureOrEvidenceExpiryAtCommitRollsBackTheEntireSubmission; MiniAppReviewDecisionTests.AdminExpiryDuringAuditRollsBackTheDecisionAndNotification; MiniAppPublishingTests.ProofExpiryDuringAuditRollsBackPublicationStateAndOutbox |
| 22 | Bounded delivery / no-secret responses | MiniAppReviewOutboxTests; MiniAppReviewOutboxPostgresTests; MiniAppVersionCredentialTests; MiniAppReviewAdminEndpointTests; strict Admin client tests |

Additional checks: Admin app filters execute before count/pagination; signed live
Admin policies protect all module routes. Developer BFF tests retain exact path,
method, Origin/CSRF, no-store and request-size enforcement. Browser tests must
verify escaped reasons, stale/busy controls and native dialog focus separately.

Unit SQLite fixtures isolate business assertions; PostgreSQL tests exercise real
migrations, triggers, serializable races and transaction rollback. Seeded frozen
SSO targets in projection tests never substitute for actual Production OIDC E2E.
No application database, real email, deployment or Stage 8 is part of these checks.
