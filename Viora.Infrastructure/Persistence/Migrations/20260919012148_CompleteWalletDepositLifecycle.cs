using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteWalletDepositLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"Payments\" SET \"ExpiresAt\" = \"CreatedAt\" + INTERVAL '15 minutes'");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LedgerTransactionId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_LedgerTransactionId",
                table: "Payments",
                column: "LedgerTransactionId",
                unique: true,
                filter: "\"LedgerTransactionId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_WalletTransactions_LedgerTransactionId",
                table: "Payments",
                column: "LedgerTransactionId",
                principalTable: "WalletTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_WalletTransactions_LedgerTransactionId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_LedgerTransactionId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "LedgerTransactionId",
                table: "Payments");
        }
    }
}
