using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalPercentageFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FeePercent",
                table: "Withdrawals",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WithdrawalFeeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    FeePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawalFeeSettings", x => x.Id);
                    table.CheckConstraint("CK_WithdrawalFeeSettings_Rate", "\"FeePercent\" >= 0 AND \"FeePercent\" <= 99.99");
                    table.CheckConstraint("CK_WithdrawalFeeSettings_Singleton", "\"Id\" = 1");
                    table.CheckConstraint("CK_WithdrawalFeeSettings_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_WithdrawalFeeSettings_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "WithdrawalFeeSettings",
                columns: new[] { "Id", "FeePercent", "UpdatedAt", "UpdatedBy", "Version" },
                values: new object[] { 1, 10m, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, 1L });

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalFeeSettings_UpdatedBy",
                table: "WithdrawalFeeSettings",
                column: "UpdatedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WithdrawalFeeSettings");

            migrationBuilder.DropColumn(
                name: "FeePercent",
                table: "Withdrawals");
        }
    }
}
