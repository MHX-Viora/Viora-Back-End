using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixLiveGiftCoinLedgerConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WalletTransactions_Amount",
                table: "WalletTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalletTransactions_Amount",
                table: "WalletTransactions",
                sql: "(\"Amount\" <> 0 AND \"CoinAmount\" IS NULL) OR (\"Amount\" = 0 AND \"CoinAmount\" IS NOT NULL AND \"CoinAmount\" <> 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalletTransactions_CoinBalance",
                table: "WalletTransactions",
                sql: "\"CoinAmount\" IS NULL OR (\"CoinBalanceBefore\" IS NOT NULL AND \"CoinBalanceAfter\" IS NOT NULL AND \"CoinBalanceBefore\" >= 0 AND \"CoinBalanceAfter\" >= 0 AND \"CoinBalanceAfter\" = \"CoinBalanceBefore\" + \"CoinAmount\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WalletTransactions_Amount",
                table: "WalletTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WalletTransactions_CoinBalance",
                table: "WalletTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalletTransactions_Amount",
                table: "WalletTransactions",
                sql: "\"Amount\" <> 0");
        }
    }
}
