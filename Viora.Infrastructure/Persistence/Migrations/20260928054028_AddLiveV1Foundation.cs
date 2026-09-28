using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveV1Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiveCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiveGifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AnimationUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PriceCoin = table.Column<long>(type: "bigint", nullable: false),
                    AnimationType = table.Column<short>(type: "smallint", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveGifts", x => x.Id);
                    table.CheckConstraint("CK_LiveGifts_PriceCoin", "\"PriceCoin\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "Lives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CoverUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Privacy = table.Column<short>(type: "smallint", nullable: false),
                    AllowComments = table.Column<bool>(type: "boolean", nullable: false),
                    AllowGifts = table.Column<bool>(type: "boolean", nullable: false),
                    AgoraChannelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentViewerCount = table.Column<int>(type: "integer", nullable: false),
                    PeakViewerCount = table.Column<int>(type: "integer", nullable: false),
                    TotalViews = table.Column<long>(type: "bigint", nullable: false),
                    UniqueViewers = table.Column<int>(type: "integer", nullable: false),
                    TotalComments = table.Column<long>(type: "bigint", nullable: false),
                    TotalReactions = table.Column<long>(type: "bigint", nullable: false),
                    TotalGiftCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalGiftValue = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lives_LiveCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "LiveCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lives_Users_HostUserId",
                        column: x => x.HostUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LiveComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveComments_Lives_LiveId",
                        column: x => x.LiveId,
                        principalTable: "Lives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveComments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LiveGiftTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    LiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    GiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceCoin = table.Column<long>(type: "bigint", nullable: false),
                    TotalCoin = table.Column<long>(type: "bigint", nullable: false),
                    FeePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    PlatformFee = table.Column<long>(type: "bigint", nullable: false),
                    HostEarning = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveGiftTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveGiftTransactions_LiveGifts_GiftId",
                        column: x => x.GiftId,
                        principalTable: "LiveGifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveGiftTransactions_Lives_LiveId",
                        column: x => x.LiveId,
                        principalTable: "Lives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveGiftTransactions_Users_HostUserId",
                        column: x => x.HostUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveGiftTransactions_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveGiftTransactions_WalletTransactions_WalletTransactionId",
                        column: x => x.WalletTransactionId,
                        principalTable: "WalletTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LiveModerators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveModerators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveModerators_Lives_LiveId",
                        column: x => x.LiveId,
                        principalTable: "Lives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveModerators_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveModerators_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LiveUserRestrictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsMuted = table.Column<bool>(type: "boolean", nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    MutedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveUserRestrictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveUserRestrictions_Lives_LiveId",
                        column: x => x.LiveId,
                        principalTable: "Lives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveUserRestrictions_Users_AppliedByUserId",
                        column: x => x.AppliedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveUserRestrictions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LiveViewerSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveViewerSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveViewerSessions_Lives_LiveId",
                        column: x => x.LiveId,
                        principalTable: "Lives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveViewerSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveCategories_IsActive_SortOrder",
                table: "LiveCategories",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveCategories_Slug",
                table: "LiveCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveComments_LiveId_CreatedAt",
                table: "LiveComments",
                columns: new[] { "LiveId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveComments_UserId",
                table: "LiveComments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveGifts_IsActive_SortOrder",
                table: "LiveGifts",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveGiftTransactions_GiftId",
                table: "LiveGiftTransactions",
                column: "GiftId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveGiftTransactions_HostUserId_CreatedAt",
                table: "LiveGiftTransactions",
                columns: new[] { "HostUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveGiftTransactions_LiveId_CreatedAt",
                table: "LiveGiftTransactions",
                columns: new[] { "LiveId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveGiftTransactions_RequestId",
                table: "LiveGiftTransactions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveGiftTransactions_SenderUserId_CreatedAt",
                table: "LiveGiftTransactions",
                columns: new[] { "SenderUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveGiftTransactions_WalletTransactionId",
                table: "LiveGiftTransactions",
                column: "WalletTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveModerators_AssignedByUserId",
                table: "LiveModerators",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveModerators_LiveId_UserId",
                table: "LiveModerators",
                columns: new[] { "LiveId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveModerators_UserId",
                table: "LiveModerators",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Lives_AgoraChannelName",
                table: "Lives",
                column: "AgoraChannelName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lives_CategoryId",
                table: "Lives",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Lives_HostUserId_CreatedAt",
                table: "Lives",
                columns: new[] { "HostUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Lives_Status_StartedAt",
                table: "Lives",
                columns: new[] { "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveUserRestrictions_AppliedByUserId",
                table: "LiveUserRestrictions",
                column: "AppliedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveUserRestrictions_LiveId_UserId",
                table: "LiveUserRestrictions",
                columns: new[] { "LiveId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveUserRestrictions_UserId",
                table: "LiveUserRestrictions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveViewerSessions_LiveId_ConnectionId",
                table: "LiveViewerSessions",
                columns: new[] { "LiveId", "ConnectionId" },
                unique: true,
                filter: "\"LeftAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LiveViewerSessions_LiveId_UserId_JoinedAt",
                table: "LiveViewerSessions",
                columns: new[] { "LiveId", "UserId", "JoinedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveViewerSessions_UserId",
                table: "LiveViewerSessions",
                column: "UserId");

            migrationBuilder.Sql("""
                INSERT INTO "LiveCategories" ("Id", "Name", "Slug", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt") VALUES
                ('41cc3bf5-81e9-46c0-a7de-f857b8c51901', 'Trò chuyện', 'tro-chuyen', 10, TRUE, NOW(), NOW()),
                ('41cc3bf5-81e9-46c0-a7de-f857b8c51902', 'Âm nhạc', 'am-nhac', 20, TRUE, NOW(), NOW()),
                ('41cc3bf5-81e9-46c0-a7de-f857b8c51903', 'Game', 'game', 30, TRUE, NOW(), NOW()),
                ('41cc3bf5-81e9-46c0-a7de-f857b8c51904', 'Đời sống', 'doi-song', 40, TRUE, NOW(), NOW()),
                ('41cc3bf5-81e9-46c0-a7de-f857b8c51905', 'Ẩm thực', 'am-thuc', 50, TRUE, NOW(), NOW());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveComments");

            migrationBuilder.DropTable(
                name: "LiveGiftTransactions");

            migrationBuilder.DropTable(
                name: "LiveModerators");

            migrationBuilder.DropTable(
                name: "LiveUserRestrictions");

            migrationBuilder.DropTable(
                name: "LiveViewerSessions");

            migrationBuilder.DropTable(
                name: "LiveGifts");

            migrationBuilder.DropTable(
                name: "Lives");

            migrationBuilder.DropTable(
                name: "LiveCategories");
        }
    }
}
