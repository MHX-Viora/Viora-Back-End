using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppReviewNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_MiniApps_Id_DeveloperAccountId",
                table: "MiniApps",
                columns: new[] { "Id", "DeveloperAccountId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MiniAppReviewActions_Id_MiniAppId",
                table: "MiniAppReviewActions",
                columns: new[] { "Id", "MiniAppId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MiniAppPublications_Id_MiniAppId",
                table: "MiniAppPublications",
                columns: new[] { "Id", "MiniAppId" });

            migrationBuilder.CreateTable(
                name: "MiniAppReviewNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientDeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<short>(type: "smallint", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    MiniAppVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppReviewNotifications", x => x.Id);
                    table.UniqueConstraint("AK_MiniAppReviewNotifications_Id_EventId_RecipientDeveloperId", x => new { x.Id, x.EventId, x.RecipientDeveloperId });
                    table.CheckConstraint("CK_MiniAppReviewNotifications_Event", "\"EventType\" BETWEEN 0 AND 8");
                    table.CheckConstraint("CK_MiniAppReviewNotifications_Read", "\"ReadAt\" IS NULL OR \"ReadAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_MiniAppReviewNotifications_DeveloperAccounts_RecipientDevel~",
                        column: x => x.RecipientDeveloperId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewNotifications_MiniAppPublications_PublicationI~",
                        columns: x => new { x.PublicationId, x.MiniAppId },
                        principalTable: "MiniAppPublications",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewNotifications_MiniAppReviewActions_EventId_Min~",
                        columns: x => new { x.EventId, x.MiniAppId },
                        principalTable: "MiniAppReviewActions",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewNotifications_MiniAppReviewRequests_ReviewRequ~",
                        columns: x => new { x.ReviewRequestId, x.MiniAppId },
                        principalTable: "MiniAppReviewRequests",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewNotifications_MiniAppVersions_MiniAppVersionId~",
                        columns: x => new { x.MiniAppVersionId, x.MiniAppId },
                        principalTable: "MiniAppVersions",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReviewNotifications_MiniApps_MiniAppId_RecipientDeve~",
                        columns: x => new { x.MiniAppId, x.RecipientDeveloperId },
                        principalTable: "MiniApps",
                        principalColumns: new[] { "Id", "DeveloperAccountId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppReviewNotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientDeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppReviewNotificationDeliveries", x => x.Id);
                    table.CheckConstraint("CK_MiniAppReviewDeliveries_Attempts", "\"AttemptCount\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_MiniAppReviewDeliveries_Channel", "\"Channel\" = 0");
                    table.CheckConstraint("CK_MiniAppReviewDeliveries_Error", "\"LastErrorCode\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_MiniAppReviewDeliveries_Lease", "(\"Status\" = 1 AND \"LeaseId\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"Status\" <> 1 AND \"LeaseId\" IS NULL AND \"LeaseUntil\" IS NULL)");
                    table.CheckConstraint("CK_MiniAppReviewDeliveries_Sent", "(\"Status\" = 2 AND \"SentAt\" IS NOT NULL) OR (\"Status\" <> 2 AND \"SentAt\" IS NULL)");
                    table.CheckConstraint("CK_MiniAppReviewDeliveries_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_MiniAppReviewNotificationDeliveries_MiniAppReviewNotificati~",
                        columns: x => new { x.NotificationId, x.EventId, x.RecipientDeveloperId },
                        principalTable: "MiniAppReviewNotifications",
                        principalColumns: new[] { "Id", "EventId", "RecipientDeveloperId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotificationDeliveries_EventId_RecipientDevelo~",
                table: "MiniAppReviewNotificationDeliveries",
                columns: new[] { "EventId", "RecipientDeveloperId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotificationDeliveries_NotificationId_EventId_~",
                table: "MiniAppReviewNotificationDeliveries",
                columns: new[] { "NotificationId", "EventId", "RecipientDeveloperId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotificationDeliveries_Status_LeaseUntil",
                table: "MiniAppReviewNotificationDeliveries",
                columns: new[] { "Status", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotificationDeliveries_Status_NextAttemptAt",
                table: "MiniAppReviewNotificationDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotifications_EventId_MiniAppId",
                table: "MiniAppReviewNotifications",
                columns: new[] { "EventId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotifications_EventId_RecipientDeveloperId",
                table: "MiniAppReviewNotifications",
                columns: new[] { "EventId", "RecipientDeveloperId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotifications_MiniAppId_RecipientDeveloperId",
                table: "MiniAppReviewNotifications",
                columns: new[] { "MiniAppId", "RecipientDeveloperId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotifications_MiniAppVersionId_MiniAppId",
                table: "MiniAppReviewNotifications",
                columns: new[] { "MiniAppVersionId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotifications_PublicationId_MiniAppId",
                table: "MiniAppReviewNotifications",
                columns: new[] { "PublicationId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotifications_RecipientDeveloperId_CreatedAt_Id",
                table: "MiniAppReviewNotifications",
                columns: new[] { "RecipientDeveloperId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotifications_RecipientDeveloperId_ReadAt",
                table: "MiniAppReviewNotifications",
                columns: new[] { "RecipientDeveloperId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReviewNotifications_ReviewRequestId_MiniAppId",
                table: "MiniAppReviewNotifications",
                columns: new[] { "ReviewRequestId", "MiniAppId" });
            migrationBuilder.Sql("""
                CREATE FUNCTION ankt_guard_mini_app_review_notification() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(NEW."Id", NEW."RecipientDeveloperId", NEW."EventId", NEW."EventType", NEW."MiniAppId",
                        NEW."ReviewRequestId", NEW."MiniAppVersionId", NEW."PublicationId", NEW."CreatedAt")
                        IS DISTINCT FROM ROW(OLD."Id", OLD."RecipientDeveloperId", OLD."EventId", OLD."EventType", OLD."MiniAppId",
                        OLD."ReviewRequestId", OLD."MiniAppVersionId", OLD."PublicationId", OLD."CreatedAt")
                        OR (OLD."ReadAt" IS NOT NULL AND NEW."ReadAt" IS DISTINCT FROM OLD."ReadAt") THEN
                        RAISE EXCEPTION 'Notification event and receipt are immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_MiniAppReviewNotifications_Receipt" BEFORE UPDATE ON "MiniAppReviewNotifications"
                    FOR EACH ROW EXECUTE FUNCTION ankt_guard_mini_app_review_notification();
                CREATE FUNCTION ankt_guard_mini_app_review_delivery() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(NEW."Id", NEW."NotificationId", NEW."EventId", NEW."RecipientDeveloperId", NEW."Channel", NEW."CreatedAt")
                        IS DISTINCT FROM ROW(OLD."Id", OLD."NotificationId", OLD."EventId", OLD."RecipientDeveloperId", OLD."Channel", OLD."CreatedAt")
                        OR (OLD."Status" = 2 AND NEW IS DISTINCT FROM OLD)
                        OR NEW."AttemptCount" < OLD."AttemptCount" OR NEW."AttemptCount" > OLD."AttemptCount" + 1
                        OR (NEW."Status" = 2 AND (OLD."Status" <> 1 OR NEW."AttemptCount" = 0)) THEN
                        RAISE EXCEPTION 'Delivery identity, attempts or terminal receipt conflict' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_MiniAppReviewNotificationDeliveries_Receipt" BEFORE UPDATE ON "MiniAppReviewNotificationDeliveries"
                    FOR EACH ROW EXECUTE FUNCTION ankt_guard_mini_app_review_delivery();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppReviewNotificationDeliveries");

            migrationBuilder.DropTable(
                name: "MiniAppReviewNotifications");
            migrationBuilder.Sql("DROP FUNCTION ankt_guard_mini_app_review_notification(); DROP FUNCTION ankt_guard_mini_app_review_delivery();");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MiniApps_Id_DeveloperAccountId",
                table: "MiniApps");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MiniAppReviewActions_Id_MiniAppId",
                table: "MiniAppReviewActions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MiniAppPublications_Id_MiniAppId",
                table: "MiniAppPublications");
        }
    }
}
