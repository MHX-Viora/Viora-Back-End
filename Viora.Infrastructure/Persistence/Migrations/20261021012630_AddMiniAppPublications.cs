using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_MiniAppOAuthClients_Id_MiniAppId",
                table: "MiniAppOAuthClients",
                columns: new[] { "Id", "MiniAppId" });

            migrationBuilder.CreateTable(
                name: "MiniAppOperationalStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ChangedByAdminAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SuspendedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppOperationalStates", x => x.Id);
                    table.CheckConstraint("CK_MiniAppOperationalStates_Status", "\"Status\" IN (0,1)");
                    table.CheckConstraint("CK_MiniAppOperationalStates_Suspension", "\"Status\" = 0 OR (\"ChangedByAdminAccountId\" IS NOT NULL AND \"SuspendedAt\" IS NOT NULL AND \"Reason\" IS NOT NULL AND length(trim(\"Reason\")) BETWEEN 10 AND 2000)");
                    table.ForeignKey(
                        name: "FK_MiniAppOperationalStates_Accounts_ChangedByAdminAccountId",
                        column: x => x.ChangedByAdminAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppOperationalStates_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedReviewRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOAuthClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Environment = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnpublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedByDeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuspendedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspensionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppPublications", x => x.Id);
                    table.CheckConstraint("CK_MiniAppPublications_Closure", "(\"Status\" = 0 AND \"UnpublishedAt\" IS NULL) OR (\"Status\" <> 0 AND \"UnpublishedAt\" IS NOT NULL AND \"UnpublishedAt\" >= \"PublishedAt\")");
                    table.CheckConstraint("CK_MiniAppPublications_Environment", "\"Environment\" = 1");
                    table.CheckConstraint("CK_MiniAppPublications_Status", "\"Status\" IN (0,1,2,3)");
                    table.CheckConstraint("CK_MiniAppPublications_Suspension", "\"Status\" <> 3 OR (\"SuspendedAt\" IS NOT NULL AND \"SuspensionReason\" IS NOT NULL AND length(trim(\"SuspensionReason\")) BETWEEN 10 AND 2000)");
                    table.ForeignKey(
                        name: "FK_MiniAppPublications_DeveloperAccounts_PublishedByDeveloperId",
                        column: x => x.PublishedByDeveloperId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppPublications_MiniAppOAuthClients_ProductionOAuthClie~",
                        columns: x => new { x.ProductionOAuthClientId, x.MiniAppId },
                        principalTable: "MiniAppOAuthClients",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppPublications_MiniAppReviewRequests_ApprovedReviewReq~",
                        columns: x => new { x.ApprovedReviewRequestId, x.MiniAppId, x.MiniAppVersionId },
                        principalTable: "MiniAppReviewRequests",
                        principalColumns: new[] { "Id", "MiniAppId", "MiniAppVersionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppPublications_MiniAppVersions_MiniAppVersionId_MiniAp~",
                        columns: x => new { x.MiniAppVersionId, x.MiniAppId },
                        principalTable: "MiniAppVersions",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppOperationalStates_ChangedByAdminAccountId",
                table: "MiniAppOperationalStates",
                column: "ChangedByAdminAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppOperationalStates_MiniAppId",
                table: "MiniAppOperationalStates",
                column: "MiniAppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPublications_ApprovedReviewRequestId_MiniAppId_MiniA~",
                table: "MiniAppPublications",
                columns: new[] { "ApprovedReviewRequestId", "MiniAppId", "MiniAppVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPublications_MiniAppId",
                table: "MiniAppPublications",
                column: "MiniAppId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPublications_MiniAppId_PublishedAt",
                table: "MiniAppPublications",
                columns: new[] { "MiniAppId", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPublications_MiniAppVersionId_MiniAppId",
                table: "MiniAppPublications",
                columns: new[] { "MiniAppVersionId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPublications_ProductionOAuthClientId_MiniAppId",
                table: "MiniAppPublications",
                columns: new[] { "ProductionOAuthClientId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPublications_PublishedByDeveloperId",
                table: "MiniAppPublications",
                column: "PublishedByDeveloperId");
            migrationBuilder.Sql("""
                CREATE FUNCTION ankt_guard_mini_app_publication() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Publication history cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 0 OR NOT EXISTS (
                            SELECT 1 FROM "MiniAppReviewRequests" r
                            JOIN "MiniApps" a ON a."Id" = r."MiniAppId"
                            JOIN "DeveloperAccounts" d ON d."Id" = a."DeveloperAccountId"
                            WHERE r."Id" = NEW."ApprovedReviewRequestId" AND r."Status" = 4
                                AND r."MiniAppId" = NEW."MiniAppId" AND r."MiniAppVersionId" = NEW."MiniAppVersionId"
                                AND a."DeveloperAccountId" = NEW."PublishedByDeveloperId" AND a."DeletedAt" IS NULL
                                AND a."Status" <> 7 AND d."Status" = 1
                                AND (d."LockedUntil" IS NULL OR d."LockedUntil" <= now()))
                            OR EXISTS (SELECT 1 FROM "MiniAppOperationalStates" WHERE "MiniAppId" = NEW."MiniAppId" AND "Status" = 1)
                            OR (NEW."ProductionOAuthClientId" IS NOT NULL AND NOT EXISTS (
                                SELECT 1 FROM "MiniAppOAuthClients" c WHERE c."Id" = NEW."ProductionOAuthClientId"
                                    AND c."MiniAppId" = NEW."MiniAppId" AND c."Environment" = 1 AND c."RevokedAt" IS NULL)) THEN
                            RAISE EXCEPTION 'Publication requires an owned approved target and active source' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF ROW(NEW."Id", NEW."MiniAppId", NEW."MiniAppVersionId", NEW."ApprovedReviewRequestId",
                            NEW."ProductionOAuthClientId", NEW."Environment", NEW."PublishedAt", NEW."PublishedByDeveloperId", NEW."CreatedAt")
                            IS DISTINCT FROM ROW(OLD."Id", OLD."MiniAppId", OLD."MiniAppVersionId", OLD."ApprovedReviewRequestId",
                            OLD."ProductionOAuthClientId", OLD."Environment", OLD."PublishedAt", OLD."PublishedByDeveloperId", OLD."CreatedAt")
                            OR (OLD."Status" <> 0 AND NEW IS DISTINCT FROM OLD) THEN
                            RAISE EXCEPTION 'Publication target and closed history are immutable' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_MiniAppPublications_Immutable" BEFORE INSERT OR UPDATE OR DELETE ON "MiniAppPublications"
                    FOR EACH ROW EXECUTE FUNCTION ankt_guard_mini_app_publication();
                CREATE FUNCTION ankt_guard_mini_app_operational_state() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Operational state cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'UPDATE' AND ROW(NEW."Id", NEW."MiniAppId", NEW."CreatedAt")
                        IS DISTINCT FROM ROW(OLD."Id", OLD."MiniAppId", OLD."CreatedAt") THEN
                        RAISE EXCEPTION 'Operational identity is immutable' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."Status" = 0 AND EXISTS (SELECT 1 FROM "MiniAppPublications" WHERE "MiniAppId" = NEW."MiniAppId" AND "Status" = 0)
                        AND TG_OP = 'UPDATE' AND OLD."Status" = 1 THEN
                        RAISE EXCEPTION 'Restore must leave the app unpublished' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_MiniAppOperationalStates_Identity" BEFORE INSERT OR UPDATE OR DELETE ON "MiniAppOperationalStates"
                    FOR EACH ROW EXECUTE FUNCTION ankt_guard_mini_app_operational_state();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Downgrade loses Stage 7 history. Never let an older binary reactivate a live/legacy publication.
            migrationBuilder.Sql("""
                LOCK TABLE "MiniApps", "MiniAppPublications" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "MiniAppPublications" WHERE "Status" = 0)
                        OR EXISTS (SELECT 1 FROM "MiniApps" WHERE "Status" = 6) THEN
                        RAISE EXCEPTION 'Unpublish or explicitly quarantine apps before removing Stage 7 publication authority' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "MiniAppOperationalStates");

            migrationBuilder.DropTable(
                name: "MiniAppPublications");
            migrationBuilder.Sql("DROP FUNCTION ankt_guard_mini_app_publication(); DROP FUNCTION ankt_guard_mini_app_operational_state();");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MiniAppOAuthClients_Id_MiniAppId",
                table: "MiniAppOAuthClients");
        }
    }
}
