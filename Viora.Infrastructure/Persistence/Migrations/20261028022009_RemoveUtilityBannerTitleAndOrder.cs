using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUtilityBannerTitleAndOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UtilityBanners_IsActive_SortOrder",
                table: "UtilityBanners");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "UtilityBanners");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "UtilityBanners");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "UtilityBanners",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBanners_IsActive_CreatedAt",
                table: "UtilityBanners",
                columns: new[] { "IsActive", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UtilityBanners_IsActive_CreatedAt",
                table: "UtilityBanners");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "UtilityBanners");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "UtilityBanners",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "UtilityBanners",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBanners_IsActive_SortOrder",
                table: "UtilityBanners",
                columns: new[] { "IsActive", "SortOrder" });
        }
    }
}
