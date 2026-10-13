START TRANSACTION;

LOCK TABLE "DeveloperReviewHistory", "MiniAppHistoryStates",
    "MiniAppLaunchCodes", "MiniAppAuditLogs", "Accounts"
    IN SHARE ROW EXCLUSIVE MODE;
DO $guard$
BEGIN
    IF EXISTS (SELECT 1 FROM "MiniAppLaunchCodes"
        WHERE "UsedAt" IS NULL AND "ExpiresAt" > CURRENT_TIMESTAMP) THEN
        RAISE EXCEPTION 'Active legacy Mini App codes remain; retire consumers and wait for expiry before consolidation';
    END IF;
    IF EXISTS (SELECT 1 FROM "DeveloperReviewHistory" r
        JOIN "MiniAppAuditLogs" a ON a."Id" = r."Id") THEN
        RAISE EXCEPTION 'Developer review IDs collide with audit IDs; consolidation aborted';
    END IF;
END $guard$;

ALTER TABLE "MiniAppAuditLogs" ADD "EventType" character varying(32) NOT NULL DEFAULT 'Activity';

ALTER TABLE "MiniAppAuditLogs" ADD "ReviewInternalNotes" character varying(2000);

ALTER TABLE "MiniAppAuditLogs" ADD "ReviewPreviousStatus" character varying(32);

ALTER TABLE "MiniAppAuditLogs" ADD "ReviewReason" character varying(1000);

ALTER TABLE "MiniAppAuditLogs" ADD "ReviewStatus" character varying(32);

ALTER TABLE "MiniAppAuditLogs" ADD "ReviewVersion" integer;

ALTER TABLE "Accounts" ADD "MiniAppHistoryClearedBefore" timestamp with time zone;

CREATE INDEX "IX_MiniAppAuditLogs_DeveloperId_EventType_CreatedAt_Id" ON "MiniAppAuditLogs" ("DeveloperId", "EventType", "CreatedAt", "Id");

CREATE INDEX "IX_MiniAppAuditLogs_DeveloperId_EventType_ReviewVersion" ON "MiniAppAuditLogs" ("DeveloperId", "EventType", "ReviewVersion");

ALTER TABLE "MiniAppAuditLogs" ADD CONSTRAINT "CK_MiniAppAuditLogs_DeveloperReview" CHECK ("EventType" <> 'DeveloperReview' OR ("DeveloperId" IS NOT NULL AND "ActorAccountId" IS NOT NULL AND "ReviewVersion" IS NOT NULL AND "ReviewPreviousStatus" IS NOT NULL AND "ReviewStatus" IS NOT NULL));

INSERT INTO "MiniAppAuditLogs" ("Id", "CreatedAt", "DeveloperId",
    "ActorAccountId", "Action", "EventType", "ReviewVersion",
    "ReviewPreviousStatus", "ReviewStatus", "ReviewReason", "ReviewInternalNotes")
SELECT "Id", "CreatedAt", "DeveloperId", "ReviewerAccountId",
    'Developer' || "Status", 'DeveloperReview', "Version",
    "PreviousStatus", "Status", "Reason", "InternalNotes"
FROM "DeveloperReviewHistory";

UPDATE "Accounts" a SET "MiniAppHistoryClearedBefore" = h."ClearedBefore"
FROM "MiniAppHistoryStates" h WHERE a."Id" = h."AccountId";

DO $verify$
BEGIN
    IF EXISTS (
        SELECT "Id", "CreatedAt", "DeveloperId", "ReviewerAccountId",
            "Version", "PreviousStatus", "Status", "Reason", "InternalNotes"
        FROM "DeveloperReviewHistory"
        EXCEPT
        SELECT "Id", "CreatedAt", "DeveloperId", "ActorAccountId",
            "ReviewVersion", "ReviewPreviousStatus", "ReviewStatus",
            "ReviewReason", "ReviewInternalNotes"
        FROM "MiniAppAuditLogs" WHERE "EventType" = 'DeveloperReview'
    ) THEN
        RAISE EXCEPTION 'Developer review data verification failed';
    END IF;
    IF EXISTS (SELECT 1 FROM "MiniAppHistoryStates" h
        LEFT JOIN "Accounts" a ON a."Id" = h."AccountId"
        WHERE a."Id" IS NULL OR a."MiniAppHistoryClearedBefore" IS DISTINCT FROM h."ClearedBefore") THEN
        RAISE EXCEPTION 'Mini App history cutoff verification failed';
    END IF;
END $verify$;

DROP TABLE "DeveloperReviewHistory";

DROP TABLE "MiniAppHistoryStates";

DROP TABLE "MiniAppLaunchCodes";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012094401_ConsolidateMiniAppSchema', '8.0.30');

COMMIT;
