using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLivePremiumGiftEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EffectDurationMs",
                table: "LiveGifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<short>(
                name: "EffectTier",
                table: "LiveGifts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "EffectType",
                table: "LiveGifts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.Sql("""
                UPDATE "LiveGifts" SET "EffectType" = 1, "EffectTier" = 1, "EffectDurationMs" = 4400
                WHERE "Id" = 'c474b413-f287-4f13-84f5-b9a578041004';
                UPDATE "LiveGifts" SET "EffectType" = 3, "EffectTier" = 3, "EffectDurationMs" = 4600
                WHERE "Id" = 'c474b413-f287-4f13-84f5-b9a578041005';
                UPDATE "LiveGifts" SET "EffectType" = 2, "EffectTier" = 2, "EffectDurationMs" = 3600
                WHERE "Id" = 'c474b413-f287-4f13-84f5-b9a578041006';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectDurationMs",
                table: "LiveGifts");

            migrationBuilder.DropColumn(
                name: "EffectTier",
                table: "LiveGifts");

            migrationBuilder.DropColumn(
                name: "EffectType",
                table: "LiveGifts");
        }
    }
}
