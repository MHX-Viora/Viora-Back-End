START TRANSACTION;

ALTER TABLE "DeveloperApplications" ALTER COLUMN "EmailVerifiedAt" DROP NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261013015728_MakeDeveloperEmailVerificationOptional', '8.0.30');

COMMIT;
