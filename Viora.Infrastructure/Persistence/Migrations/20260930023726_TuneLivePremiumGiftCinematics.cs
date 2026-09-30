using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TuneLivePremiumGiftCinematics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "LiveGifts" SET "EffectDurationMs" = 5600
                WHERE "Id" = 'c474b413-f287-4f13-84f5-b9a578041004' AND "EffectType" = 1 AND "EffectDurationMs" = 4400;
                UPDATE "LiveGifts" SET "EffectDurationMs" = 6000
                WHERE "Id" = 'c474b413-f287-4f13-84f5-b9a578041005' AND "EffectType" = 3 AND "EffectDurationMs" = 4600;
                UPDATE "LiveGifts" SET "EffectDurationMs" = 4700
                WHERE "Id" = 'c474b413-f287-4f13-84f5-b9a578041006' AND "EffectType" = 2 AND "EffectDurationMs" = 3600;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "LiveGifts" SET "EffectDurationMs" = 4400
                WHERE "Id" = 'c474b413-f287-4f13-84f5-b9a578041004' AND "EffectType" = 1 AND "EffectDurationMs" = 5600;
                UPDATE "LiveGifts" SET "EffectDurationMs" = 4600
                WHERE "Id" = 'c474b413-f287-4f13-84f5-b9a578041005' AND "EffectType" = 3 AND "EffectDurationMs" = 6000;
                UPDATE "LiveGifts" SET "EffectDurationMs" = 3600
                WHERE "Id" = 'c474b413-f287-4f13-84f5-b9a578041006' AND "EffectType" = 2 AND "EffectDurationMs" = 4700;
                """);
        }
    }
}
