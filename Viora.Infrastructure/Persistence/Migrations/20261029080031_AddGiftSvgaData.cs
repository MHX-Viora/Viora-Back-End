using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftSvgaData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "AnimationData",
                table: "LiveGifts",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnimationDurationMs",
                table: "LiveGifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnimationData",
                table: "LiveGifts");

            migrationBuilder.DropColumn(
                name: "AnimationDurationMs",
                table: "LiveGifts");
        }
    }
}
