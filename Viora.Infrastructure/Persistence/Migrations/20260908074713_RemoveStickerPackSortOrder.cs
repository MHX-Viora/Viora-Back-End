using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStickerPackSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StickerPacks_IsActive_IsFeatured_SortOrder",
                table: "StickerPacks");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "StickerPacks");

            migrationBuilder.CreateIndex(
                name: "IX_StickerPacks_IsActive_IsFeatured_CreatedAt",
                table: "StickerPacks",
                columns: new[] { "IsActive", "IsFeatured", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StickerPacks_IsActive_IsFeatured_CreatedAt",
                table: "StickerPacks");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "StickerPacks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_StickerPacks_IsActive_IsFeatured_SortOrder",
                table: "StickerPacks",
                columns: new[] { "IsActive", "IsFeatured", "SortOrder" });
        }
    }
}
