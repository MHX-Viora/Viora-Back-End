using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateMiniAppSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF runs this migration in a transaction. Block writers while copying
            // so a late review or history clear cannot disappear with the old tables.
            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "MiniAppAuditLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Activity");

            migrationBuilder.AddColumn<string>(
                name: "ReviewInternalNotes",
                table: "MiniAppAuditLogs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewPreviousStatus",
                table: "MiniAppAuditLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "MiniAppAuditLogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "MiniAppAuditLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewVersion",
                table: "MiniAppAuditLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MiniAppHistoryClearedBefore",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppAuditLogs_DeveloperId_EventType_CreatedAt_Id",
                table: "MiniAppAuditLogs",
                columns: new[] { "DeveloperId", "EventType", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppAuditLogs_DeveloperId_EventType_ReviewVersion",
                table: "MiniAppAuditLogs",
                columns: new[] { "DeveloperId", "EventType", "ReviewVersion" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MiniAppAuditLogs_DeveloperReview",
                table: "MiniAppAuditLogs",
                sql: "\"EventType\" <> 'DeveloperReview' OR (\"DeveloperId\" IS NOT NULL AND \"ActorAccountId\" IS NOT NULL AND \"ReviewVersion\" IS NOT NULL AND \"ReviewPreviousStatus\" IS NOT NULL AND \"ReviewStatus\" IS NOT NULL)");

            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.DropTable(name: "DeveloperReviewHistory");
            migrationBuilder.DropTable(name: "MiniAppHistoryStates");
            migrationBuilder.DropTable(name: "MiniAppLaunchCodes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE \"MiniAppAuditLogs\", \"Accounts\" IN SHARE ROW EXCLUSIVE MODE;");

            migrationBuilder.CreateTable(
                name: "DeveloperReviewHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InternalNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PreviousStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperReviewHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperReviewHistory_Accounts_ReviewerAccountId",
                        column: x => x.ReviewerAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeveloperReviewHistory_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppHistoryStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClearedBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppHistoryStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppHistoryStates_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppLaunchCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeChallenge = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: true),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RedirectUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RuntimeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    StateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppLaunchCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppLaunchCodes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppLaunchCodes_MiniAppRuntimeSessions_RuntimeSessionId",
                        column: x => x.RuntimeSessionId,
                        principalTable: "MiniAppRuntimeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppLaunchCodes_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReviewHistory_DeveloperId_CreatedAt",
                table: "DeveloperReviewHistory",
                columns: new[] { "DeveloperId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReviewHistory_ReviewerAccountId",
                table: "DeveloperReviewHistory",
                column: "ReviewerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppHistoryStates_AccountId",
                table: "MiniAppHistoryStates",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_AccountId",
                table: "MiniAppLaunchCodes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_CodeHash",
                table: "MiniAppLaunchCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_ExpiresAt",
                table: "MiniAppLaunchCodes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_MiniAppId",
                table: "MiniAppLaunchCodes",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_RuntimeSessionId",
                table: "MiniAppLaunchCodes",
                column: "RuntimeSessionId");

            // Restore the supported data before removing its consolidated columns.
            // Retired launch codes cannot be recovered; use the backup if required.
            migrationBuilder.Sql("""
                INSERT INTO "DeveloperReviewHistory" ("Id", "CreatedAt", "DeveloperId",
                    "ReviewerAccountId", "Version", "PreviousStatus", "Status", "Reason", "InternalNotes")
                SELECT "Id", "CreatedAt", "DeveloperId", "ActorAccountId", "ReviewVersion",
                    "ReviewPreviousStatus", "ReviewStatus", "ReviewReason", "ReviewInternalNotes"
                FROM "MiniAppAuditLogs" WHERE "EventType" = 'DeveloperReview';

                INSERT INTO "MiniAppHistoryStates" ("Id", "AccountId", "CreatedAt", "ClearedBefore")
                SELECT "Id", "Id", CURRENT_TIMESTAMP, "MiniAppHistoryClearedBefore"
                FROM "Accounts" WHERE "MiniAppHistoryClearedBefore" IS NOT NULL;

                DELETE FROM "MiniAppAuditLogs" WHERE "EventType" = 'DeveloperReview';
                """);

            migrationBuilder.DropIndex(name: "IX_MiniAppAuditLogs_DeveloperId_EventType_CreatedAt_Id", table: "MiniAppAuditLogs");
            migrationBuilder.DropIndex(name: "IX_MiniAppAuditLogs_DeveloperId_EventType_ReviewVersion", table: "MiniAppAuditLogs");
            migrationBuilder.DropCheckConstraint(name: "CK_MiniAppAuditLogs_DeveloperReview", table: "MiniAppAuditLogs");
            migrationBuilder.DropColumn(name: "EventType", table: "MiniAppAuditLogs");
            migrationBuilder.DropColumn(name: "ReviewInternalNotes", table: "MiniAppAuditLogs");
            migrationBuilder.DropColumn(name: "ReviewPreviousStatus", table: "MiniAppAuditLogs");
            migrationBuilder.DropColumn(name: "ReviewReason", table: "MiniAppAuditLogs");
            migrationBuilder.DropColumn(name: "ReviewStatus", table: "MiniAppAuditLogs");
            migrationBuilder.DropColumn(name: "ReviewVersion", table: "MiniAppAuditLogs");
            migrationBuilder.DropColumn(name: "MiniAppHistoryClearedBefore", table: "Accounts");
        }
    }
}
