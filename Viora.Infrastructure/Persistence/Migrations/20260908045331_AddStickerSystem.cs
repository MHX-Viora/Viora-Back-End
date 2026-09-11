using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStickerSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StickerId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StickerPacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    AvailableFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AvailableUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StickerPacks", x => x.Id);
                    table.CheckConstraint("CK_StickerPacks_PriceAndAvailability", "\"Price\" >= 0 AND (\"AvailableUntil\" IS NULL OR \"AvailableFrom\" IS NULL OR \"AvailableUntil\" > \"AvailableFrom\")");
                });

            migrationBuilder.CreateTable(
                name: "StickerPackPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StickerPackId = table.Column<Guid>(type: "uuid", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StickerPackPurchases", x => x.Id);
                    table.CheckConstraint("CK_StickerPackPurchases_Price", "\"Price\" >= 0");
                    table.ForeignKey(
                        name: "FK_StickerPackPurchases_StickerPacks_StickerPackId",
                        column: x => x.StickerPackId,
                        principalTable: "StickerPacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StickerPackPurchases_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Stickers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StickerPackId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Format = table.Column<short>(type: "smallint", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stickers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stickers_StickerPacks_StickerPackId",
                        column: x => x.StickerPackId,
                        principalTable: "StickerPacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserStickerPacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StickerPackId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStickerPacks", x => x.Id);
                    table.CheckConstraint("CK_UserStickerPacks_PurchasePrice", "\"PurchasePrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_UserStickerPacks_StickerPacks_StickerPackId",
                        column: x => x.StickerPackId,
                        principalTable: "StickerPacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserStickerPacks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_StickerId",
                table: "Messages",
                column: "StickerId");

            migrationBuilder.CreateIndex(
                name: "IX_StickerPackPurchases_StickerPackId",
                table: "StickerPackPurchases",
                column: "StickerPackId");

            migrationBuilder.CreateIndex(
                name: "IX_StickerPackPurchases_UserId_StickerPackId_CreatedAt",
                table: "StickerPackPurchases",
                columns: new[] { "UserId", "StickerPackId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StickerPacks_IsActive_IsFeatured_SortOrder",
                table: "StickerPacks",
                columns: new[] { "IsActive", "IsFeatured", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StickerPacks_Price",
                table: "StickerPacks",
                column: "Price");

            migrationBuilder.CreateIndex(
                name: "IX_Stickers_StickerPackId_IsActive_SortOrder",
                table: "Stickers",
                columns: new[] { "StickerPackId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_UserStickerPacks_StickerPackId",
                table: "UserStickerPacks",
                column: "StickerPackId");

            migrationBuilder.CreateIndex(
                name: "IX_UserStickerPacks_UserId_StickerPackId",
                table: "UserStickerPacks",
                columns: new[] { "UserId", "StickerPackId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Stickers_StickerId",
                table: "Messages",
                column: "StickerId",
                principalTable: "Stickers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Stickers_StickerId",
                table: "Messages");

            migrationBuilder.DropTable(
                name: "StickerPackPurchases");

            migrationBuilder.DropTable(
                name: "Stickers");

            migrationBuilder.DropTable(
                name: "UserStickerPacks");

            migrationBuilder.DropTable(
                name: "StickerPacks");

            migrationBuilder.DropIndex(
                name: "IX_Messages_StickerId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "StickerId",
                table: "Messages");
        }
    }
}
