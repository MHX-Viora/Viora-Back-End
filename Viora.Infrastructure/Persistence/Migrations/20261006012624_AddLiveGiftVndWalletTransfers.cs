using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveGiftVndWalletTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "WalletTransactions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "LiveGiftTransactions",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "COIN");

            migrationBuilder.AddColumn<long>(
                name: "FeeAmount",
                table: "LiveGiftTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GiftName",
                table: "LiveGiftTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GrossAmount",
                table: "LiveGiftTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NetAmount",
                table: "LiveGiftTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceiverWalletTransactionId",
                table: "LiveGiftTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "Status",
                table: "LiveGiftTransactions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<long>(
                name: "UnitPrice",
                table: "LiveGiftTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Price",
                table: "LiveGifts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Only the current catalog receives VND prices. Historical coin ledger rows stay untouched.
            // Disable oversized legacy catalog entries instead of overflowing bigint/JavaScript amounts.
            migrationBuilder.Sql("""
                UPDATE "LiveGifts"
                SET "Price" = CASE "EffectType"
                    WHEN 1 THEN 50000
                    WHEN 2 THEN 200000
                    WHEN 3 THEN 500000
                    ELSE LEAST(CAST("PriceCoin" AS numeric) * 1000, 9007199254740991)::bigint
                    END,
                    "IsActive" = CASE
                        WHEN "EffectType" NOT IN (1, 2, 3) AND CAST("PriceCoin" AS numeric) * 1000 > 9007199254740991
                        THEN FALSE ELSE "IsActive" END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_LiveGiftTransactions_ReceiverWalletTransactionId",
                table: "LiveGiftTransactions",
                column: "ReceiverWalletTransactionId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LiveGiftTransactions_Currency",
                table: "LiveGiftTransactions",
                sql: "\"Currency\" IN ('COIN', 'VND')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LiveGiftTransactions_VndTransfer",
                table: "LiveGiftTransactions",
                sql: "\"Currency\" <> 'VND' OR (\n    \"UnitPrice\" IS NOT NULL AND \"UnitPrice\" > 0\n    AND \"GrossAmount\" IS NOT NULL AND \"GrossAmount\" > 0 AND \"GrossAmount\" <= 9007199254740991\n    AND \"NetAmount\" IS NOT NULL AND \"NetAmount\" > 0\n    AND \"FeeAmount\" IS NOT NULL AND \"FeeAmount\" >= 0 AND \"FeeAmount\" < \"GrossAmount\"\n    AND \"Quantity\" > 0 AND \"Quantity\" <= 99\n    AND CAST(\"GrossAmount\" AS numeric) = CAST(\"UnitPrice\" AS numeric) * \"Quantity\"\n    AND \"NetAmount\" = \"GrossAmount\" - \"FeeAmount\"\n    AND \"ReceiverWalletTransactionId\" IS NOT NULL\n    AND \"ReceiverWalletTransactionId\" <> \"WalletTransactionId\"\n    AND \"WalletTransactionId\" <> '00000000-0000-0000-0000-000000000000'\n    AND \"ReceiverWalletTransactionId\" <> '00000000-0000-0000-0000-000000000000'\n    AND \"SenderUserId\" <> \"HostUserId\"\n    AND \"GiftName\" IS NOT NULL AND length(trim(\"GiftName\")) > 0\n    AND \"Status\" = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LiveGifts_Price",
                table: "LiveGifts",
                sql: "\"Price\" > 0 AND \"Price\" <= 9007199254740991");

            migrationBuilder.AddForeignKey(
                name: "FK_LiveGiftTransactions_WalletTransactions_ReceiverWalletTrans~",
                table: "LiveGiftTransactions",
                column: "ReceiverWalletTransactionId",
                principalTable: "WalletTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back after VND gifts would discard financial snapshots and ledger metadata.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "LiveGiftTransactions" WHERE "Currency" = 'VND')
                        OR EXISTS (SELECT 1 FROM "WalletTransactions" WHERE "Metadata" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Cannot roll back VND gift schema while VND transactions or wallet metadata exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_LiveGiftTransactions_WalletTransactions_ReceiverWalletTrans~",
                table: "LiveGiftTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LiveGiftTransactions_ReceiverWalletTransactionId",
                table: "LiveGiftTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LiveGiftTransactions_Currency",
                table: "LiveGiftTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LiveGiftTransactions_VndTransfer",
                table: "LiveGiftTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LiveGifts_Price",
                table: "LiveGifts");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "LiveGiftTransactions");

            migrationBuilder.DropColumn(
                name: "FeeAmount",
                table: "LiveGiftTransactions");

            migrationBuilder.DropColumn(
                name: "GiftName",
                table: "LiveGiftTransactions");

            migrationBuilder.DropColumn(
                name: "GrossAmount",
                table: "LiveGiftTransactions");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "LiveGiftTransactions");

            migrationBuilder.DropColumn(
                name: "ReceiverWalletTransactionId",
                table: "LiveGiftTransactions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "LiveGiftTransactions");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "LiveGiftTransactions");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "LiveGifts");
        }
    }
}
