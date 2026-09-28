using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveGiftCoinLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CoinAmount",
                table: "WalletTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CoinBalanceAfter",
                table: "WalletTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CoinBalanceBefore",
                table: "WalletTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO "LiveGifts" ("Id", "Name", "ImageUrl", "PriceCoin", "AnimationType", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt") VALUES
                ('c474b413-f287-4f13-84f5-b9a578041001', 'Hoa hồng', 'https://raw.githubusercontent.com/jdecked/twemoji/main/assets/svg/1f339.svg', 1, 0, 1, TRUE, NOW(), NOW()),
                ('c474b413-f287-4f13-84f5-b9a578041002', 'Trái tim', 'https://raw.githubusercontent.com/jdecked/twemoji/main/assets/svg/1f496.svg', 5, 0, 2, TRUE, NOW(), NOW()),
                ('c474b413-f287-4f13-84f5-b9a578041003', 'Cà phê', 'https://raw.githubusercontent.com/jdecked/twemoji/main/assets/svg/2615.svg', 10, 0, 3, TRUE, NOW(), NOW()),
                ('c474b413-f287-4f13-84f5-b9a578041004', 'Pháo hoa', 'https://raw.githubusercontent.com/jdecked/twemoji/main/assets/svg/1f386.svg', 20, 1, 4, TRUE, NOW(), NOW()),
                ('c474b413-f287-4f13-84f5-b9a578041005', 'Vương miện', 'https://raw.githubusercontent.com/jdecked/twemoji/main/assets/svg/1f451.svg', 100, 1, 5, TRUE, NOW(), NOW()),
                ('c474b413-f287-4f13-84f5-b9a578041006', 'Tên lửa', 'https://raw.githubusercontent.com/jdecked/twemoji/main/assets/svg/1f680.svg', 500, 2, 6, TRUE, NOW(), NOW())
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoinAmount",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "CoinBalanceAfter",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "CoinBalanceBefore",
                table: "WalletTransactions");
        }
    }
}
