using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSsoLoginTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SsoLoginTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrowserHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReturnUrl = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SocialSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SsoLoginTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SsoLoginTransactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SsoLoginTransactions_AccountId",
                table: "SsoLoginTransactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SsoLoginTransactions_ExpiresAt",
                table: "SsoLoginTransactions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SsoLoginTransactions");
        }
    }
}
