using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeAdvertisements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Advertisements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvertiserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    Placement = table.Column<short>(type: "smallint", nullable: false),
                    Objective = table.Column<short>(type: "smallint", nullable: false),
                    DestinationType = table.Column<short>(type: "smallint", nullable: false),
                    DestinationUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CtaType = table.Column<short>(type: "smallint", nullable: false),
                    TargetingMode = table.Column<short>(type: "smallint", nullable: false),
                    MinimumAge = table.Column<short>(type: "smallint", nullable: true),
                    MaximumAge = table.Column<short>(type: "smallint", nullable: true),
                    TargetLocation = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DailyBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SpentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advertisements", x => x.Id);
                    table.CheckConstraint("CK_Advertisements_Ages", "(\"MinimumAge\" IS NULL OR \"MinimumAge\" >= 13) AND (\"MaximumAge\" IS NULL OR \"MaximumAge\" <= 100) AND (\"MinimumAge\" IS NULL OR \"MaximumAge\" IS NULL OR \"MaximumAge\" >= \"MinimumAge\")");
                    table.CheckConstraint("CK_Advertisements_Budget", "\"TotalBudget\" >= 50000 AND \"SpentAmount\" >= 0 AND \"ReservedAmount\" >= 0 AND \"SpentAmount\" + \"ReservedAmount\" <= \"TotalBudget\"");
                    table.CheckConstraint("CK_Advertisements_Schedule", "\"EndAt\" > \"StartAt\"");
                    table.ForeignKey(
                        name: "FK_Advertisements_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Advertisements_Users_AdvertiserId",
                        column: x => x.AdvertiserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Advertisements_Users_ReviewedBy",
                        column: x => x.ReviewedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisementEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvertisementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    ClientEventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ChargeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisementEvents", x => x.Id);
                    table.CheckConstraint("CK_AdvertisementEvents_Charge", "\"ChargeAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_AdvertisementEvents_Advertisements_AdvertisementId",
                        column: x => x.AdvertisementId,
                        principalTable: "Advertisements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdvertisementEvents_Users_ViewerId",
                        column: x => x.ViewerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisementFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvertisementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisementFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvertisementFeedback_Advertisements_AdvertisementId",
                        column: x => x.AdvertisementId,
                        principalTable: "Advertisements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdvertisementFeedback_Users_ViewerId",
                        column: x => x.ViewerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementEvents_AdvertisementId_Type_CreatedAt",
                table: "AdvertisementEvents",
                columns: new[] { "AdvertisementId", "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementEvents_AdvertisementId_ViewerId_Type_ClientEve~",
                table: "AdvertisementEvents",
                columns: new[] { "AdvertisementId", "ViewerId", "Type", "ClientEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementEvents_ViewerId",
                table: "AdvertisementEvents",
                column: "ViewerId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementFeedback_AdvertisementId_ViewerId_Type",
                table: "AdvertisementFeedback",
                columns: new[] { "AdvertisementId", "ViewerId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementFeedback_ViewerId",
                table: "AdvertisementFeedback",
                column: "ViewerId");

            migrationBuilder.CreateIndex(
                name: "IX_Advertisements_AdvertiserId_CreatedAt",
                table: "Advertisements",
                columns: new[] { "AdvertiserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Advertisements_PostId",
                table: "Advertisements",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_Advertisements_ReviewedBy",
                table: "Advertisements",
                column: "ReviewedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Advertisements_Status_Placement_StartAt_EndAt",
                table: "Advertisements",
                columns: new[] { "Status", "Placement", "StartAt", "EndAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvertisementEvents");

            migrationBuilder.DropTable(
                name: "AdvertisementFeedback");

            migrationBuilder.DropTable(
                name: "Advertisements");
        }
    }
}
