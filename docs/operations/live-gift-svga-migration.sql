START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029080031_AddGiftSvgaData') THEN
    ALTER TABLE "LiveGifts" ADD "AnimationData" bytea;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029080031_AddGiftSvgaData') THEN
    ALTER TABLE "LiveGifts" ADD "AnimationDurationMs" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261029080031_AddGiftSvgaData') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261029080031_AddGiftSvgaData', '8.0.11');
    END IF;
END $EF$;
COMMIT;

