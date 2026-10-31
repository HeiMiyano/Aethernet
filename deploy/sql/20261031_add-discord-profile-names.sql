START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031211238_AddDiscordProfileNames') THEN
    ALTER TABLE "Users" ADD "DiscordDisplayName" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031211238_AddDiscordProfileNames') THEN
    ALTER TABLE "Users" ADD "DiscordProfileUpdatedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031211238_AddDiscordProfileNames') THEN
    ALTER TABLE "Users" ADD "DiscordUsername" character varying(32);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261031211238_AddDiscordProfileNames') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261031211238_AddDiscordProfileNames', '8.0.10');
    END IF;
END $EF$;
COMMIT;

