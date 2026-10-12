START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012020223_AddMiniAppAutoLogin') THEN
    ALTER TABLE "MiniApps" ADD "AutoLogin" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012020223_AddMiniAppAutoLogin') THEN
    ALTER TABLE "MiniAppRuntimeSessions" ADD "PublishedVersion" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261012020223_AddMiniAppAutoLogin') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261012020223_AddMiniAppAutoLogin', '8.0.30');
    END IF;
END $EF$;
COMMIT;
