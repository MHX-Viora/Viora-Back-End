using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppProductionHost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniAppProductionHostSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedReviewRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicationRevision = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationalRevision = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceSocialSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrowserBindingHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BrowserNonceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Platform = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuntimeCredentialHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppProductionHostSessions", x => x.Id);
                    table.UniqueConstraint("AK_MiniAppProductionHostSessions_Id_BindingId", x => new { x.Id, x.BindingId });
                    table.CheckConstraint("CK_ProductionHost_Actor", "(\"AccountId\" IS NULL AND \"SourceSocialSessionId\" IS NULL) OR (\"AccountId\" IS NOT NULL AND \"SourceSocialSessionId\" IS NOT NULL)");
                    table.CheckConstraint("CK_ProductionHost_Expiry", "\"ExpiresAt\" > \"CreatedAt\" AND (\"ClosedAt\" IS NULL OR \"ClosedAt\" >= \"CreatedAt\")");
                    table.CheckConstraint("CK_ProductionHost_Hashes", "length(\"SnapshotHash\") = 64 AND \"SnapshotHash\" = lower(\"SnapshotHash\") AND length(\"PolicyFingerprint\") = 64 AND \"PolicyFingerprint\" = lower(\"PolicyFingerprint\") AND length(\"BrowserBindingHash\") = 64 AND \"BrowserBindingHash\" = lower(\"BrowserBindingHash\") AND length(\"BrowserNonceHash\") = 64 AND \"BrowserNonceHash\" = lower(\"BrowserNonceHash\") AND length(\"RequestHash\") = 64 AND \"RequestHash\" = lower(\"RequestHash\")");
                    table.CheckConstraint("CK_ProductionHost_Platform", "\"Platform\" IN ('Web','Android','iOS')");
                    table.CheckConstraint("CK_ProductionHost_RuntimeHash", "\"RuntimeCredentialHash\" IS NULL OR (length(\"RuntimeCredentialHash\") = 64 AND \"RuntimeCredentialHash\" = lower(\"RuntimeCredentialHash\"))");
                    table.ForeignKey(
                        name: "FK_MiniAppProductionHostSessions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppProductionHostSessions_MiniAppPublications_Publicati~",
                        columns: x => new { x.PublicationId, x.MiniAppId, x.MiniAppVersionId, x.ApprovedReviewRequestId },
                        principalTable: "MiniAppPublications",
                        principalColumns: new[] { "Id", "MiniAppId", "MiniAppVersionId", "ApprovedReviewRequestId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppProductionLaunchGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BrowserNonceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Audience = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ReceiptProtected = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppProductionLaunchGrants", x => x.Id);
                    table.CheckConstraint("CK_ProductionGrant_Audience", "length(\"Audience\") BETWEEN 1 AND 512");
                    table.CheckConstraint("CK_ProductionGrant_Expiry", "\"ExpiresAt\" > \"CreatedAt\" AND (\"ConsumedAt\" IS NULL OR \"ConsumedAt\" >= \"CreatedAt\")");
                    table.CheckConstraint("CK_ProductionGrant_Hashes", "length(\"GrantHash\") = 64 AND \"GrantHash\" = lower(\"GrantHash\") AND length(\"BrowserNonceHash\") = 64 AND \"BrowserNonceHash\" = lower(\"BrowserNonceHash\")");
                    table.CheckConstraint("CK_ProductionGrant_Receipt", "(\"ConsumedAt\" IS NOT NULL OR \"ReceiptProtected\" IS NOT NULL) AND (\"ReceiptProtected\" IS NULL OR length(\"ReceiptProtected\") BETWEEN 1 AND 16384)");
                    table.ForeignKey(
                        name: "FK_MiniAppProductionLaunchGrants_MiniAppProductionHostSessions~",
                        columns: x => new { x.HostSessionId, x.BindingId },
                        principalTable: "MiniAppProductionHostSessions",
                        principalColumns: new[] { "Id", "BindingId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionHostSessions_AccountId",
                table: "MiniAppProductionHostSessions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionHostSessions_BindingId_ClosedAt_ExpiresAt",
                table: "MiniAppProductionHostSessions",
                columns: new[] { "BindingId", "ClosedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionHostSessions_BindingId_RequestId",
                table: "MiniAppProductionHostSessions",
                columns: new[] { "BindingId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionHostSessions_ExpiresAt",
                table: "MiniAppProductionHostSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionHostSessions_MiniAppId_ExpiresAt",
                table: "MiniAppProductionHostSessions",
                columns: new[] { "MiniAppId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionHostSessions_PublicationId_MiniAppId_MiniA~",
                table: "MiniAppProductionHostSessions",
                columns: new[] { "PublicationId", "MiniAppId", "MiniAppVersionId", "ApprovedReviewRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionHostSessions_RuntimeCredentialHash",
                table: "MiniAppProductionHostSessions",
                column: "RuntimeCredentialHash",
                unique: true,
                filter: "\"RuntimeCredentialHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionLaunchGrants_ExpiresAt",
                table: "MiniAppProductionLaunchGrants",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionLaunchGrants_GrantHash",
                table: "MiniAppProductionLaunchGrants",
                column: "GrantHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionLaunchGrants_HostSessionId",
                table: "MiniAppProductionLaunchGrants",
                column: "HostSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionLaunchGrants_HostSessionId_BindingId",
                table: "MiniAppProductionLaunchGrants",
                columns: new[] { "HostSessionId", "BindingId" });

            migrationBuilder.Sql("""
                CREATE FUNCTION ankt_production_host_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        PERFORM 1 FROM "MiniAppPublications" p
                        JOIN "MiniAppVersions" v ON v."Id" = p."MiniAppVersionId" AND v."MiniAppId" = p."MiniAppId"
                        JOIN "MiniAppReviewRequests" r ON r."Id" = p."ApprovedReviewRequestId" AND r."MiniAppVersionId" = v."Id"
                        JOIN "MiniAppOperationalStates" o ON o."MiniAppId" = p."MiniAppId"
                        JOIN "MiniApps" a ON a."Id" = p."MiniAppId"
                        JOIN "DeveloperAccounts" d ON d."Id" = a."DeveloperAccountId"
                        WHERE p."Id" = NEW."PublicationId" AND p."MiniAppId" = NEW."MiniAppId"
                            AND p."MiniAppVersionId" = NEW."MiniAppVersionId" AND r."Id" = NEW."ApprovedReviewRequestId"
                            AND p."Environment" = 1 AND p."Status" = 0 AND p."UnpublishedAt" IS NULL AND p."PublishedAt" <= clock_timestamp()
                            AND p."ConcurrencyToken" = NEW."PublicationRevision" AND o."ConcurrencyToken" = NEW."OperationalRevision"
                            AND v."SnapshotHash" = NEW."SnapshotHash" AND v."State" <> 0 AND v."SubmittedAt" IS NOT NULL
                            AND r."Status" = 4 AND r."DecidedAt" IS NOT NULL AND o."Status" = 0 AND a."DeletedAt" IS NULL
                            AND p."PublishedByDeveloperId" = a."DeveloperAccountId" AND r."DeveloperAccountId" = a."DeveloperAccountId"
                            AND d."Status" = 1 AND (d."LockedUntil" IS NULL OR d."LockedUntil" <= clock_timestamp())
                        FOR SHARE OF p,v,r,o,a,d;
                        IF NOT FOUND OR NEW."ExpiresAt" > NEW."CreatedAt" + interval '30 minutes' OR NEW."RuntimeCredentialHash" IS NOT NULL
                            OR NEW."ClosedAt" IS NOT NULL THEN
                            RAISE EXCEPTION 'invalid production host target' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF (to_jsonb(NEW) - ARRAY['Version','LastActivityAt','ClosedAt','RuntimeCredentialHash'])
                            IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['Version','LastActivityAt','ClosedAt','RuntimeCredentialHash'])
                            OR (OLD."ClosedAt" IS NOT NULL AND NEW."ClosedAt" IS DISTINCT FROM OLD."ClosedAt")
                            OR NEW."LastActivityAt" < OLD."LastActivityAt"
                            OR (OLD."RuntimeCredentialHash" IS NOT NULL AND NEW."RuntimeCredentialHash" IS NOT NULL
                                AND NEW."RuntimeCredentialHash" IS DISTINCT FROM OLD."RuntimeCredentialHash")
                            OR (OLD."ClosedAt" IS NOT NULL AND NEW."RuntimeCredentialHash" IS NOT NULL) THEN
                            RAISE EXCEPTION 'immutable production host binding' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER ankt_production_host_guard BEFORE INSERT OR UPDATE ON "MiniAppProductionHostSessions"
                    FOR EACH ROW EXECUTE FUNCTION ankt_production_host_guard();

                CREATE FUNCTION ankt_production_grant_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        PERFORM 1 FROM "MiniAppProductionHostSessions" s WHERE s."Id" = NEW."HostSessionId"
                            AND s."BindingId" = NEW."BindingId" AND s."BrowserNonceHash" = NEW."BrowserNonceHash"
                            AND s."ClosedAt" IS NULL AND s."ExpiresAt" >= NEW."ExpiresAt" FOR SHARE OF s;
                        IF NOT FOUND OR NEW."ExpiresAt" > NEW."CreatedAt" + interval '30 seconds' OR NEW."ConsumedAt" IS NOT NULL THEN
                            RAISE EXCEPTION 'invalid production grant binding' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF (to_jsonb(NEW) - ARRAY['Version','ConsumedAt','ReceiptProtected'])
                            IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['Version','ConsumedAt','ReceiptProtected'])
                            OR (OLD."ConsumedAt" IS NOT NULL AND NEW."ConsumedAt" IS DISTINCT FROM OLD."ConsumedAt")
                            OR (NEW."ConsumedAt" IS NOT NULL AND NEW."ReceiptProtected" IS NOT NULL)
                            OR (OLD."ReceiptProtected" IS NULL AND NEW."ReceiptProtected" IS NOT NULL) THEN
                            RAISE EXCEPTION 'immutable production grant binding' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER ankt_production_grant_guard BEFORE INSERT OR UPDATE ON "MiniAppProductionLaunchGrants"
                    FOR EACH ROW EXECUTE FUNCTION ankt_production_grant_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppProductionLaunchGrants");

            migrationBuilder.DropTable(
                name: "MiniAppProductionHostSessions");
            migrationBuilder.Sql("DROP FUNCTION ankt_production_grant_guard(); DROP FUNCTION ankt_production_host_guard();");
        }
    }
}
