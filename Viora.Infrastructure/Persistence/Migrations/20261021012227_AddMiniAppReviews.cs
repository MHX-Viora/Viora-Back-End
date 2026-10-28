using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniAppReviewRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedAdminAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ChangeRequestsJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    EvidenceAttestationJson = table.Column<string>(type: "character varying(32768)", maxLength: 32768, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppReviewRequests", x => x.Id);
                    table.UniqueConstraint("AK_MiniAppReviewRequests_Id_MiniAppId", x => new { x.Id, x.MiniAppId });
                    table.UniqueConstraint("AK_MiniAppReviewRequests_Id_MiniAppId_MiniAppVersionId", x => new { x.Id, x.MiniAppId, x.MiniAppVersionId });
                    table.CheckConstraint("CK_MiniAppReviews_Assignment", "(\"Status\" = 0 AND \"AssignedAdminAccountId\" IS NULL AND \"ReviewStartedAt\" IS NULL) OR (\"Status\" <> 0 AND \"AssignedAdminAccountId\" IS NOT NULL AND \"ReviewStartedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_MiniAppReviews_Changes", "length(\"ChangeRequestsJson\") BETWEEN 2 AND 16384");
                    table.CheckConstraint("CK_MiniAppReviews_Decision", "(\"Status\" IN (0,1) AND \"DecidedAt\" IS NULL) OR (\"Status\" IN (2,3,4) AND \"DecidedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_MiniAppReviews_Evidence", "length(\"EvidenceAttestationJson\") BETWEEN 2 AND 32768");
                    table.CheckConstraint("CK_MiniAppReviews_Reason", "\"Status\" NOT IN (2,3) OR length(trim(\"DecisionReason\")) BETWEEN 10 AND 2000 AND \"DecisionReason\" IS NOT NULL");
                    table.CheckConstraint("CK_MiniAppReviews_Status", "\"Status\" IN (0,1,2,3,4)");
                    table.ForeignKey(
                        name: "FK_MiniAppReviewRequests_Accounts_AssignedAdminAccountId",
                        column: x => x.AssignedAdminAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewRequests_DeveloperAccounts_DeveloperAccountId",
                        column: x => x.DeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewRequests_MiniAppVersions_MiniAppVersionId_Mini~",
                        columns: x => new { x.MiniAppVersionId, x.MiniAppId },
                        principalTable: "MiniAppVersions",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppReviewActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    MiniAppVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<short>(type: "smallint", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorDeveloperId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorAdminAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<short>(type: "smallint", nullable: false),
                    BeforeState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AfterState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MetadataJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppReviewActions", x => x.Id);
                    table.CheckConstraint("CK_MiniAppReviewActions_Action", "\"Action\" BETWEEN 0 AND 13");
                    table.CheckConstraint("CK_MiniAppReviewActions_Actor", "(\"ActorType\" = 0 AND \"ActorDeveloperId\" IS NOT NULL AND \"ActorAdminAccountId\" IS NULL AND \"ActorId\" = \"ActorDeveloperId\") OR (\"ActorType\" = 1 AND \"ActorAdminAccountId\" IS NOT NULL AND \"ActorDeveloperId\" IS NULL AND \"ActorId\" = \"ActorAdminAccountId\")");
                    table.CheckConstraint("CK_MiniAppReviewActions_Hash", "length(\"PayloadHash\") = 64");
                    table.CheckConstraint("CK_MiniAppReviewActions_Payload", "length(\"MetadataJson\") BETWEEN 2 AND 2048 AND length(\"ResultJson\") BETWEEN 2 AND 4096");
                    table.ForeignKey(
                        name: "FK_MiniAppReviewActions_Accounts_ActorAdminAccountId",
                        column: x => x.ActorAdminAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewActions_DeveloperAccounts_ActorDeveloperId",
                        column: x => x.ActorDeveloperId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewActions_MiniAppReviewRequests_ReviewRequestId_~",
                        columns: x => new { x.ReviewRequestId, x.MiniAppId },
                        principalTable: "MiniAppReviewRequests",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewActions_MiniAppVersions_MiniAppVersionId_MiniA~",
                        columns: x => new { x.MiniAppVersionId, x.MiniAppId },
                        principalTable: "MiniAppVersions",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewActions_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewActions_ActorAdminAccountId",
                table: "MiniAppReviewActions",
                column: "ActorAdminAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewActions_ActorDeveloperId",
                table: "MiniAppReviewActions",
                column: "ActorDeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewActions_ActorType_ActorId_RequestId",
                table: "MiniAppReviewActions",
                columns: new[] { "ActorType", "ActorId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewActions_MiniAppId_CreatedAt",
                table: "MiniAppReviewActions",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewActions_MiniAppVersionId_MiniAppId",
                table: "MiniAppReviewActions",
                columns: new[] { "MiniAppVersionId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewActions_ReviewRequestId_MiniAppId",
                table: "MiniAppReviewActions",
                columns: new[] { "ReviewRequestId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewRequests_AssignedAdminAccountId_Status_Submitt~",
                table: "MiniAppReviewRequests",
                columns: new[] { "AssignedAdminAccountId", "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewRequests_DeveloperAccountId",
                table: "MiniAppReviewRequests",
                column: "DeveloperAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewRequests_MiniAppId",
                table: "MiniAppReviewRequests",
                column: "MiniAppId",
                unique: true,
                filter: "\"Status\" IN (0,1)");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewRequests_MiniAppVersionId",
                table: "MiniAppReviewRequests",
                column: "MiniAppVersionId",
                unique: true,
                filter: "\"Status\" IN (0,1)");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewRequests_MiniAppVersionId_MiniAppId",
                table: "MiniAppReviewRequests",
                columns: new[] { "MiniAppVersionId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewRequests_Status_SubmittedAt",
                table: "MiniAppReviewRequests",
                columns: new[] { "Status", "SubmittedAt" });
            migrationBuilder.Sql("""
                CREATE FUNCTION ankt_guard_mini_app_review() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Review history cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 0 OR NOT EXISTS (
                            SELECT 1 FROM "MiniAppVersions" v JOIN "MiniApps" a ON a."Id" = v."MiniAppId"
                            WHERE v."Id" = NEW."MiniAppVersionId" AND v."MiniAppId" = NEW."MiniAppId"
                                AND v."State" = 1 AND a."DeveloperAccountId" = NEW."DeveloperAccountId") THEN
                            RAISE EXCEPTION 'Review requires the owned sealed version' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF ROW(NEW."Id", NEW."MiniAppId", NEW."MiniAppVersionId", NEW."DeveloperAccountId",
                            NEW."SubmittedAt", NEW."EvidenceAttestationJson", NEW."CorrelationId", NEW."CreatedAt")
                            IS DISTINCT FROM ROW(OLD."Id", OLD."MiniAppId", OLD."MiniAppVersionId", OLD."DeveloperAccountId",
                            OLD."SubmittedAt", OLD."EvidenceAttestationJson", OLD."CorrelationId", OLD."CreatedAt") THEN
                            RAISE EXCEPTION 'Review target and evidence are immutable' USING ERRCODE = '23514';
                        END IF;
                        IF (OLD."Status" IN (2,3,4) AND NEW IS DISTINCT FROM OLD)
                            OR (NEW."Status" <> OLD."Status" AND NOT (
                                (OLD."Status" = 0 AND NEW."Status" = 1) OR (OLD."Status" = 1 AND NEW."Status" IN (2,3,4)))) THEN
                            RAISE EXCEPTION 'Invalid or terminal review transition' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    IF octet_length(NEW."EvidenceAttestationJson") > 32768
                        OR jsonb_typeof(NEW."EvidenceAttestationJson"::jsonb) <> 'object'
                        OR octet_length(NEW."ChangeRequestsJson") > 16384
                        OR jsonb_typeof(NEW."ChangeRequestsJson"::jsonb) <> 'array' THEN
                        RAISE EXCEPTION 'Invalid review evidence or change envelope' USING ERRCODE = '23514';
                    END IF;
                    IF jsonb_array_length(NEW."ChangeRequestsJson"::jsonb) > 20
                        OR (NEW."Status" = 2 AND jsonb_array_length(NEW."ChangeRequestsJson"::jsonb) = 0) THEN
                        RAISE EXCEPTION 'Change requests require bounded field items' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_MiniAppReviewRequests_Immutable" BEFORE INSERT OR UPDATE OR DELETE ON "MiniAppReviewRequests"
                    FOR EACH ROW EXECUTE FUNCTION ankt_guard_mini_app_review();
                CREATE FUNCTION ankt_guard_mini_app_review_action() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Review audit is append only' USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER "TR_MiniAppReviewActions_AppendOnly" BEFORE UPDATE OR DELETE ON "MiniAppReviewActions"
                    FOR EACH ROW EXECUTE FUNCTION ankt_guard_mini_app_review_action();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppReviewActions");

            migrationBuilder.DropTable(
                name: "MiniAppReviewRequests");
            migrationBuilder.Sql("DROP FUNCTION ankt_guard_mini_app_review(); DROP FUNCTION ankt_guard_mini_app_review_action();");
        }
    }
}
