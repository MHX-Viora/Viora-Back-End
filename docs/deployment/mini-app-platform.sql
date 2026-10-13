-- Historical setup baseline. Follow with mini-app-schema-consolidation.sql
-- after the media/discovery migration; see ../handoffs/mini-app-schema-consolidation.md.
START TRANSACTION;

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

CREATE TABLE "Developers" (
    "Id" uuid NOT NULL,
    "PartnerType" character varying(32) NOT NULL,
    "LegalName" character varying(200),
    "TradingName" character varying(200),
    "BusinessRegistrationNumber" character varying(80),
    "Country" character varying(80),
    "Address" character varying(500),
    "RepresentativeName" character varying(120),
    "RegistrationVersion" integer NOT NULL,
    "DraftApplicationJson" text,
    "RegistrationStatus" character varying(32) NOT NULL,
    "ConcurrencyStamp" uuid NOT NULL,
    "AccountId" uuid,
    "Name" character varying(120) NOT NULL,
    "CompanyName" character varying(160),
    "Email" character varying(255) NOT NULL,
    "Phone" character varying(20),
    "Website" character varying(2048),
    "Status" smallint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Developers" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Developers_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "MiniAppAuditLogs" (
    "Id" uuid NOT NULL,
    "MiniAppId" uuid,
    "DeveloperId" uuid,
    "ActorAccountId" uuid,
    "Action" character varying(80) NOT NULL,
    "Detail" character varying(500),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppAuditLogs" PRIMARY KEY ("Id")
);

CREATE TABLE "MiniAppCategories" (
    "Id" uuid NOT NULL,
    "Icon" character varying(60) NOT NULL,
    "SortOrder" integer NOT NULL,
    "Name" character varying(120) NOT NULL,
    "Slug" character varying(120) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppCategories" PRIMARY KEY ("Id")
);

CREATE TABLE "MiniAppHistoryStates" (
    "Id" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "ClearedBefore" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppHistoryStates" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppHistoryStates_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppLaunchLogs" (
    "Id" uuid NOT NULL,
    "MiniAppId" uuid NOT NULL,
    "AccountId" uuid,
    "Status" smallint NOT NULL,
    "FailureReason" character varying(80),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppLaunchLogs" PRIMARY KEY ("Id")
);

CREATE TABLE "MiniAppPermissions" (
    "Id" uuid NOT NULL,
    "Code" character varying(100) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Description" character varying(500),
    "IsSensitive" boolean NOT NULL,
    "Status" smallint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppPermissions" PRIMARY KEY ("Id")
);

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

CREATE TABLE "DeveloperMemberships" (
    "Id" uuid NOT NULL,
    "DeveloperId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "Role" smallint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_DeveloperMemberships" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_DeveloperMemberships_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_DeveloperMemberships_Developers_DeveloperId" FOREIGN KEY ("DeveloperId") REFERENCES "Developers" ("Id") ON DELETE CASCADE
);

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

CREATE TABLE "MiniApps" (
    "Id" uuid NOT NULL,
    "CatalogName" character varying(160),
    "CatalogSearchText" character varying(6000) NOT NULL,
    "CatalogCategoryId" uuid,
    "PublishedAt" timestamp with time zone,
    "HiddenFromCatalog" boolean NOT NULL,
    "SupportEmail" character varying(255),
    "PrivacyPolicyUrl" character varying(2048),
    "TermsUrl" character varying(2048),
    "DevelopmentUrl" character varying(2048),
    "AgeRating" character varying(32) NOT NULL,
    "ReleaseRegions" text[] NOT NULL,
    "IntegrationType" character varying(32) NOT NULL,
    "SsoMode" character varying(32) NOT NULL,
    "AutoLogin" boolean NOT NULL,
    "Name" character varying(120) NOT NULL,
    "Slug" character varying(120) NOT NULL,
    "Description" character varying(1000),
    "IconUrl" character varying(2048),
    "CoverUrl" character varying(2048),
    "WebUrl" character varying(2048) NOT NULL,
    "CallbackUrl" character varying(2048) NOT NULL,
    "DeveloperId" uuid NOT NULL,
    "ClientId" character varying(80) NOT NULL,
    "ClientSecretHash" text NOT NULL,
    "SecretRotatedAt" timestamp with time zone,
    "Status" smallint NOT NULL,
    "IsFeatured" boolean NOT NULL,
    "CategoryId" uuid,
    "AuthenticationMode" smallint NOT NULL DEFAULT 1,
    "ClientAuthenticationMethod" smallint NOT NULL,
    "CallbackUrls" text[] NOT NULL,
    "AllowedOrigins" text[] NOT NULL,
    "PublishedVersion" integer NOT NULL,
    "PendingVersion" integer,
    "PublishedConfigurationJson" text,
    "AllowedDomains" text[] NOT NULL,
    "DeletedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniApps" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniApps_Developers_DeveloperId" FOREIGN KEY ("DeveloperId") REFERENCES "Developers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MiniApps_MiniAppCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "MiniAppCategories" ("Id") ON DELETE RESTRICT
);

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

CREATE TABLE "MiniAppBanners" (
    "Id" uuid NOT NULL,
    "Title" character varying(160) NOT NULL,
    "ImageUrl" character varying(2048) NOT NULL,
    "MiniAppId" uuid,
    "CategoryId" uuid,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppBanners" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_MiniAppBanner_Target" CHECK (("MiniAppId" IS NULL) <> ("CategoryId" IS NULL)),
    CONSTRAINT "FK_MiniAppBanners_MiniAppCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "MiniAppCategories" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MiniAppBanners_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppExternalIdentities" (
    "Id" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "MiniAppId" uuid NOT NULL,
    "Subject" character varying(64) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppExternalIdentities" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppExternalIdentities_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MiniAppExternalIdentities_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppFavorites" (
    "Id" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "MiniAppId" uuid NOT NULL,
    "SortOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppFavorites" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppFavorites_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MiniAppFavorites_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppPermissionMappings" (
    "MiniAppId" uuid NOT NULL,
    "PermissionId" uuid NOT NULL,
    CONSTRAINT "PK_MiniAppPermissionMappings" PRIMARY KEY ("MiniAppId", "PermissionId"),
    CONSTRAINT "FK_MiniAppPermissionMappings_MiniAppPermissions_PermissionId" FOREIGN KEY ("PermissionId") REFERENCES "MiniAppPermissions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MiniAppPermissionMappings_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppPlacements" (
    "Id" uuid NOT NULL,
    "MiniAppId" uuid NOT NULL,
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppPlacements" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppPlacements_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppReports" (
    "Id" uuid NOT NULL,
    "MiniAppId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "Resolution" character varying(500),
    "ResolvedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppReports" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppReports_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MiniAppReports_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppRuntimeSessions" (
    "Id" uuid NOT NULL,
    "PublishedVersion" integer NOT NULL,
    "MiniAppId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "TokenHash" character varying(64) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "ClosedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppRuntimeSessions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppRuntimeSessions_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MiniAppRuntimeSessions_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

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

CREATE TABLE "MiniAppUserConsents" (
    "Id" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "MiniAppId" uuid NOT NULL,
    "PermissionId" uuid NOT NULL,
    "Granted" boolean NOT NULL,
    "GrantedAt" timestamp with time zone,
    "RevokedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppUserConsents" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppUserConsents_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MiniAppUserConsents_MiniAppPermissions_PermissionId" FOREIGN KEY ("PermissionId") REFERENCES "MiniAppPermissions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MiniAppUserConsents_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppVerifiedDomains" (
    "Id" uuid NOT NULL,
    "MiniAppId" uuid NOT NULL,
    "Host" character varying(253) NOT NULL,
    "ChallengeToken" character varying(100) NOT NULL,
    "VerifiedAt" timestamp with time zone,
    "LastAttemptAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppVerifiedDomains" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppVerifiedDomains_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppVersions" (
    "Id" uuid NOT NULL,
    "ReviewState" character varying(32) NOT NULL,
    "TechnicalTestJson" text,
    "TestedAt" timestamp with time zone,
    "ConfigurationHash" character varying(64),
    "MiniAppId" uuid NOT NULL,
    "Version" integer NOT NULL,
    "Status" smallint NOT NULL,
    "ConfigurationJson" text NOT NULL,
    "Reason" character varying(500),
    "ReviewedByAccountId" uuid,
    "ReviewedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppVersions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppVersions_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

CREATE TABLE "MiniAppLaunchCodes" (
    "Id" uuid NOT NULL,
    "CodeHash" character varying(64) NOT NULL,
    "AccountId" uuid NOT NULL,
    "MiniAppId" uuid NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "UsedAt" timestamp with time zone,
    "RedirectUri" character varying(2048),
    "StateHash" character varying(64),
    "CodeChallenge" character varying(43),
    "RuntimeSessionId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MiniAppLaunchCodes" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MiniAppLaunchCodes_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MiniAppLaunchCodes_MiniAppRuntimeSessions_RuntimeSessionId" FOREIGN KEY ("RuntimeSessionId") REFERENCES "MiniAppRuntimeSessions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MiniAppLaunchCodes_MiniApps_MiniAppId" FOREIGN KEY ("MiniAppId") REFERENCES "MiniApps" ("Id") ON DELETE CASCADE
);

INSERT INTO "MiniAppPermissions" ("Id", "Code", "CreatedAt", "Description", "IsSensitive", "Name", "Status", "UpdatedAt")
VALUES ('11111111-1111-1111-1111-111111111101', 'identity.login', TIMESTAMPTZ '2026-08-20T00:00:00Z', NULL, FALSE, 'Đăng nhập ANKT', 1, TIMESTAMPTZ '2026-08-20T00:00:00Z');
INSERT INTO "MiniAppPermissions" ("Id", "Code", "CreatedAt", "Description", "IsSensitive", "Name", "Status", "UpdatedAt")
VALUES ('11111111-1111-1111-1111-111111111102', 'profile.basic', TIMESTAMPTZ '2026-08-20T00:00:00Z', NULL, FALSE, 'Hồ sơ cơ bản', 1, TIMESTAMPTZ '2026-08-20T00:00:00Z');
INSERT INTO "MiniAppPermissions" ("Id", "Code", "CreatedAt", "Description", "IsSensitive", "Name", "Status", "UpdatedAt")
VALUES ('11111111-1111-1111-1111-111111111103', 'profile.email', TIMESTAMPTZ '2026-08-20T00:00:00Z', NULL, TRUE, 'Địa chỉ email', 1, TIMESTAMPTZ '2026-08-20T00:00:00Z');
INSERT INTO "MiniAppPermissions" ("Id", "Code", "CreatedAt", "Description", "IsSensitive", "Name", "Status", "UpdatedAt")
VALUES ('11111111-1111-1111-1111-111111111104', 'profile.phone', TIMESTAMPTZ '2026-08-20T00:00:00Z', NULL, TRUE, 'Số điện thoại', 1, TIMESTAMPTZ '2026-08-20T00:00:00Z');
INSERT INTO "MiniAppPermissions" ("Id", "Code", "CreatedAt", "Description", "IsSensitive", "Name", "Status", "UpdatedAt")
VALUES ('11111111-1111-1111-1111-111111111105', 'app.close', TIMESTAMPTZ '2026-08-20T00:00:00Z', NULL, FALSE, 'Đóng Mini App', 1, TIMESTAMPTZ '2026-08-20T00:00:00Z');
INSERT INTO "MiniAppPermissions" ("Id", "Code", "CreatedAt", "Description", "IsSensitive", "Name", "Status", "UpdatedAt")
VALUES ('11111111-1111-1111-1111-111111111106', 'app.open_url', TIMESTAMPTZ '2026-08-20T00:00:00Z', NULL, FALSE, 'Mở liên kết ngoài', 1, TIMESTAMPTZ '2026-08-20T00:00:00Z');
INSERT INTO "MiniAppPermissions" ("Id", "Code", "CreatedAt", "Description", "IsSensitive", "Name", "Status", "UpdatedAt")
VALUES ('11111111-1111-1111-1111-111111111107', 'app.theme', TIMESTAMPTZ '2026-08-20T00:00:00Z', NULL, FALSE, 'Đọc giao diện', 1, TIMESTAMPTZ '2026-08-20T00:00:00Z');

CREATE UNIQUE INDEX "IX_DeveloperApplications_DeveloperId_Version" ON "DeveloperApplications" ("DeveloperId", "Version");

CREATE INDEX "IX_DeveloperApplications_SubmittedByAccountId" ON "DeveloperApplications" ("SubmittedByAccountId");

CREATE INDEX "IX_DeveloperEmailVerifications_AccountId_CreatedAt" ON "DeveloperEmailVerifications" ("AccountId", "CreatedAt");

CREATE UNIQUE INDEX "IX_DeveloperEmailVerifications_AccountId_RequestWindow" ON "DeveloperEmailVerifications" ("AccountId", "RequestWindow");

CREATE INDEX "IX_DeveloperEmailVerifications_ExpiresAt" ON "DeveloperEmailVerifications" ("ExpiresAt");

CREATE UNIQUE INDEX "IX_DeveloperMemberships_AccountId" ON "DeveloperMemberships" ("AccountId");

CREATE UNIQUE INDEX "IX_DeveloperMemberships_DeveloperId_AccountId" ON "DeveloperMemberships" ("DeveloperId", "AccountId");

CREATE INDEX "IX_DeveloperReauths_AccountId" ON "DeveloperReauths" ("AccountId");

CREATE INDEX "IX_DeveloperReauths_ExpiresAt" ON "DeveloperReauths" ("ExpiresAt");

CREATE INDEX "IX_DeveloperReauths_MiniAppId" ON "DeveloperReauths" ("MiniAppId");

CREATE UNIQUE INDEX "IX_DeveloperReauths_ProofHash" ON "DeveloperReauths" ("ProofHash");

CREATE INDEX "IX_DeveloperReviewHistory_DeveloperId_CreatedAt" ON "DeveloperReviewHistory" ("DeveloperId", "CreatedAt");

CREATE INDEX "IX_DeveloperReviewHistory_ReviewerAccountId" ON "DeveloperReviewHistory" ("ReviewerAccountId");

CREATE UNIQUE INDEX "IX_Developers_AccountId" ON "Developers" ("AccountId");

CREATE UNIQUE INDEX "IX_Developers_Email" ON "Developers" ("Email");

CREATE INDEX "IX_Developers_Status" ON "Developers" ("Status");

CREATE INDEX "IX_MiniAppAuditLogs_MiniAppId_CreatedAt" ON "MiniAppAuditLogs" ("MiniAppId", "CreatedAt");

CREATE INDEX "IX_MiniAppBanners_CategoryId" ON "MiniAppBanners" ("CategoryId");

CREATE INDEX "IX_MiniAppBanners_IsActive_SortOrder" ON "MiniAppBanners" ("IsActive", "SortOrder");

CREATE INDEX "IX_MiniAppBanners_MiniAppId" ON "MiniAppBanners" ("MiniAppId");

CREATE INDEX "IX_MiniAppCategories_IsActive_SortOrder" ON "MiniAppCategories" ("IsActive", "SortOrder");

CREATE UNIQUE INDEX "IX_MiniAppCategories_Slug" ON "MiniAppCategories" ("Slug");

CREATE UNIQUE INDEX "IX_MiniAppExternalIdentities_AccountId_MiniAppId" ON "MiniAppExternalIdentities" ("AccountId", "MiniAppId");

CREATE UNIQUE INDEX "IX_MiniAppExternalIdentities_MiniAppId_Subject" ON "MiniAppExternalIdentities" ("MiniAppId", "Subject");

CREATE UNIQUE INDEX "IX_MiniAppFavorites_AccountId_MiniAppId" ON "MiniAppFavorites" ("AccountId", "MiniAppId");

CREATE INDEX "IX_MiniAppFavorites_AccountId_SortOrder" ON "MiniAppFavorites" ("AccountId", "SortOrder");

CREATE INDEX "IX_MiniAppFavorites_MiniAppId" ON "MiniAppFavorites" ("MiniAppId");

CREATE UNIQUE INDEX "IX_MiniAppHistoryStates_AccountId" ON "MiniAppHistoryStates" ("AccountId");

CREATE INDEX "IX_MiniAppLaunchCodes_AccountId" ON "MiniAppLaunchCodes" ("AccountId");

CREATE UNIQUE INDEX "IX_MiniAppLaunchCodes_CodeHash" ON "MiniAppLaunchCodes" ("CodeHash");

CREATE INDEX "IX_MiniAppLaunchCodes_ExpiresAt" ON "MiniAppLaunchCodes" ("ExpiresAt");

CREATE INDEX "IX_MiniAppLaunchCodes_MiniAppId" ON "MiniAppLaunchCodes" ("MiniAppId");

CREATE INDEX "IX_MiniAppLaunchCodes_RuntimeSessionId" ON "MiniAppLaunchCodes" ("RuntimeSessionId");

CREATE INDEX "IX_MiniAppLaunchLogs_MiniAppId_CreatedAt" ON "MiniAppLaunchLogs" ("MiniAppId", "CreatedAt");

CREATE INDEX "IX_MiniAppPermissionMappings_PermissionId" ON "MiniAppPermissionMappings" ("PermissionId");

CREATE UNIQUE INDEX "IX_MiniAppPermissions_Code" ON "MiniAppPermissions" ("Code");

CREATE INDEX "IX_MiniAppPlacements_IsActive_SortOrder" ON "MiniAppPlacements" ("IsActive", "SortOrder");

CREATE UNIQUE INDEX "IX_MiniAppPlacements_MiniAppId" ON "MiniAppPlacements" ("MiniAppId");

CREATE INDEX "IX_MiniAppReports_AccountId" ON "MiniAppReports" ("AccountId");

CREATE INDEX "IX_MiniAppReports_MiniAppId_CreatedAt" ON "MiniAppReports" ("MiniAppId", "CreatedAt");

CREATE INDEX "IX_MiniAppRuntimeSessions_AccountId" ON "MiniAppRuntimeSessions" ("AccountId");

CREATE INDEX "IX_MiniAppRuntimeSessions_ExpiresAt" ON "MiniAppRuntimeSessions" ("ExpiresAt");

CREATE INDEX "IX_MiniAppRuntimeSessions_MiniAppId" ON "MiniAppRuntimeSessions" ("MiniAppId");

CREATE UNIQUE INDEX "IX_MiniAppRuntimeSessions_TokenHash" ON "MiniAppRuntimeSessions" ("TokenHash");

CREATE INDEX "IX_MiniApps_CatalogCategoryId_Status" ON "MiniApps" ("CatalogCategoryId", "Status");

CREATE INDEX "IX_MiniApps_CatalogName" ON "MiniApps" ("CatalogName");

CREATE INDEX "IX_MiniApps_CategoryId" ON "MiniApps" ("CategoryId");

CREATE UNIQUE INDEX "IX_MiniApps_ClientId" ON "MiniApps" ("ClientId");

CREATE INDEX "IX_MiniApps_DeveloperId" ON "MiniApps" ("DeveloperId");

CREATE UNIQUE INDEX "IX_MiniApps_Slug" ON "MiniApps" ("Slug");

CREATE INDEX "IX_MiniApps_Status" ON "MiniApps" ("Status");

CREATE INDEX "IX_MiniApps_Status_HiddenFromCatalog_PublishedAt" ON "MiniApps" ("Status", "HiddenFromCatalog", "PublishedAt");

CREATE INDEX "IX_MiniAppTechnicalTests_MiniAppId_CreatedAt" ON "MiniAppTechnicalTests" ("MiniAppId", "CreatedAt");

CREATE INDEX "IX_MiniAppTechnicalTests_TestedByAccountId" ON "MiniAppTechnicalTests" ("TestedByAccountId");

CREATE INDEX "IX_MiniAppUserConsents_AccountId_MiniAppId" ON "MiniAppUserConsents" ("AccountId", "MiniAppId");

CREATE UNIQUE INDEX "IX_MiniAppUserConsents_AccountId_MiniAppId_PermissionId" ON "MiniAppUserConsents" ("AccountId", "MiniAppId", "PermissionId");

CREATE INDEX "IX_MiniAppUserConsents_MiniAppId" ON "MiniAppUserConsents" ("MiniAppId");

CREATE INDEX "IX_MiniAppUserConsents_PermissionId" ON "MiniAppUserConsents" ("PermissionId");

CREATE UNIQUE INDEX "IX_MiniAppVerifiedDomains_MiniAppId_Host" ON "MiniAppVerifiedDomains" ("MiniAppId", "Host");

CREATE UNIQUE INDEX "IX_MiniAppVersions_MiniAppId_Version" ON "MiniAppVersions" ("MiniAppId", "Version");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012072618_ReintroduceMiniAppPlatform', '8.0.30');

COMMIT;

START TRANSACTION;

ALTER TABLE "MiniApps" ADD "Screenshots" text[] NOT NULL DEFAULT ARRAY[]::text[];

CREATE INDEX "IX_MiniAppLaunchLogs_AccountId_Status_CreatedAt" ON "MiniAppLaunchLogs" ("AccountId", "Status", "CreatedAt");

CREATE INDEX "IX_MiniAppLaunchLogs_Status_CreatedAt_MiniAppId_AccountId" ON "MiniAppLaunchLogs" ("Status", "CreatedAt", "MiniAppId", "AccountId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012072934_AddMiniAppMediaAndDiscoveryIndexes', '8.0.30');

COMMIT;
