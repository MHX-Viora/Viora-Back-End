START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniAppVersions" ADD "ConfigurationHash" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniAppVersions" ADD "ReviewState" character varying(32) NOT NULL DEFAULT 'Draft';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniAppVersions" ADD "TechnicalTestJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniAppVersions" ADD "TestedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniApps" ADD "AgeRating" character varying(32) NOT NULL DEFAULT 'Everyone';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniApps" ADD "DevelopmentUrl" character varying(2048);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniApps" ADD "IntegrationType" character varying(32) NOT NULL DEFAULT 'Website';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniApps" ADD "PrivacyPolicyUrl" character varying(2048);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniApps" ADD "ReleaseRegions" text[] NOT NULL DEFAULT ARRAY[]::text[];
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniApps" ADD "SsoMode" character varying(32) NOT NULL DEFAULT 'Disabled';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniApps" ADD "SupportEmail" character varying(255);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "MiniApps" ADD "TermsUrl" character varying(2048);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "Address" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "BusinessRegistrationNumber" character varying(80);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "ConcurrencyStamp" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "Country" character varying(80);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "DraftApplicationJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "LegalName" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "PartnerType" character varying(32) NOT NULL DEFAULT 'Individual';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "RegistrationStatus" character varying(32) NOT NULL DEFAULT 'Legacy';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "RegistrationVersion" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "RepresentativeName" character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    ALTER TABLE "Developers" ADD "TradingName" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    UPDATE "MiniApps"
    SET "IntegrationType" = CASE WHEN "AuthenticationMode" = 1 THEN 'Sso' ELSE 'Website' END,
        "SsoMode" = CASE WHEN "AuthenticationMode" = 1 THEN 'Optional' ELSE 'Disabled' END;
    UPDATE "MiniAppVersions"
    SET "ReviewState" = CASE "Status"
        WHEN 1 THEN 'Submitted' WHEN 2 THEN 'Approved'
        WHEN 4 THEN 'Rejected' ELSE 'Draft' END;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE TABLE "DeveloperApplications" (
        "Id" uuid NOT NULL,
        "DeveloperId" uuid NOT NULL,
        "Version" integer NOT NULL,
        "ConfigurationJson" text NOT NULL,
        "Status" character varying(32) NOT NULL,
        "SubmittedAt" timestamp with time zone NOT NULL,
        "TermsVersion" character varying(80) NOT NULL,
        "TermsAcceptedAt" timestamp with time zone NOT NULL,
        "SubmittedByAccountId" uuid NOT NULL,
        "EmailVerifiedAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_DeveloperApplications" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DeveloperApplications_Accounts_SubmittedByAccountId" FOREIGN KEY ("SubmittedByAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_DeveloperApplications_Developers_DeveloperId" FOREIGN KEY ("DeveloperId") REFERENCES "Developers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE TABLE "DeveloperEmailVerifications" (
        "Id" uuid NOT NULL,
        "AccountId" uuid NOT NULL,
        "Email" character varying(255) NOT NULL,
        "CodeHash" character varying(64) NOT NULL,
        "RequestWindow" bigint NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "VerifiedAt" timestamp with time zone,
        "Attempts" integer NOT NULL,
        "DeliverySucceeded" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_DeveloperEmailVerifications" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DeveloperEmailVerifications_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE TABLE "DeveloperReauths" (
        "Id" uuid NOT NULL,
        "AccountId" uuid NOT NULL,
        "MiniAppId" uuid,
        "Operation" character varying(40) NOT NULL,
        "ProofHash" character varying(64) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "ConsumedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_DeveloperReauths" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DeveloperReauths_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_DeveloperReauths_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE TABLE "DeveloperReviewHistory" (
        "Id" uuid NOT NULL,
        "DeveloperId" uuid NOT NULL,
        "Version" integer NOT NULL,
        "ReviewerAccountId" uuid NOT NULL,
        "PreviousStatus" character varying(32) NOT NULL,
        "Status" character varying(32) NOT NULL,
        "Reason" character varying(1000),
        "InternalNotes" character varying(2000),
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_DeveloperReviewHistory" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DeveloperReviewHistory_Accounts_ReviewerAccountId" FOREIGN KEY ("ReviewerAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_DeveloperReviewHistory_Developers_DeveloperId" FOREIGN KEY ("DeveloperId") REFERENCES "Developers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE TABLE "MiniAppTechnicalTests" (
        "Id" uuid NOT NULL,
        "MiniAppId" uuid NOT NULL,
        "TestedByAccountId" uuid NOT NULL,
        "ConfigurationHash" character varying(64) NOT NULL,
        "Passed" boolean NOT NULL,
        "ResultJson" text NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MiniAppTechnicalTests" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MiniAppTechnicalTests_Accounts_TestedByAccountId" FOREIGN KEY ("TestedByAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_MiniAppTechnicalTests_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE UNIQUE INDEX "IX_DeveloperApplications_DeveloperId_Version" ON "DeveloperApplications" ("DeveloperId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_DeveloperApplications_SubmittedByAccountId" ON "DeveloperApplications" ("SubmittedByAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_DeveloperEmailVerifications_AccountId_CreatedAt" ON "DeveloperEmailVerifications" ("AccountId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE UNIQUE INDEX "IX_DeveloperEmailVerifications_AccountId_RequestWindow" ON "DeveloperEmailVerifications" ("AccountId", "RequestWindow");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_DeveloperEmailVerifications_ExpiresAt" ON "DeveloperEmailVerifications" ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_DeveloperReauths_AccountId" ON "DeveloperReauths" ("AccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_DeveloperReauths_ExpiresAt" ON "DeveloperReauths" ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_DeveloperReauths_MiniAppId" ON "DeveloperReauths" ("MiniAppId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE UNIQUE INDEX "IX_DeveloperReauths_ProofHash" ON "DeveloperReauths" ("ProofHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_DeveloperReviewHistory_DeveloperId_CreatedAt" ON "DeveloperReviewHistory" ("DeveloperId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_DeveloperReviewHistory_ReviewerAccountId" ON "DeveloperReviewHistory" ("ReviewerAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_MiniAppTechnicalTests_MiniAppId_CreatedAt" ON "MiniAppTechnicalTests" ("MiniAppId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    CREATE INDEX "IX_MiniAppTechnicalTests_TestedByAccountId" ON "MiniAppTechnicalTests" ("TestedByAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012012833_AddDeveloperPlatformLifecycle') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261012012833_AddDeveloperPlatformLifecycle', '8.0.30');
    END IF;
END $EF$;
COMMIT;
