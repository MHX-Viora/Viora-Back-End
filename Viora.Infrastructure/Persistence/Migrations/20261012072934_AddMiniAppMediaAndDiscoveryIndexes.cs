using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppMediaAndDiscoveryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "Screenshots",
                table: "MiniApps",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchLogs_AccountId_Status_CreatedAt",
                table: "MiniAppLaunchLogs",
                columns: new[] { "AccountId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchLogs_Status_CreatedAt_MiniAppId_AccountId",
                table: "MiniAppLaunchLogs",
                columns: new[] { "Status", "CreatedAt", "MiniAppId", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MiniAppLaunchLogs_AccountId_Status_CreatedAt",
                table: "MiniAppLaunchLogs");

            migrationBuilder.DropIndex(
                name: "IX_MiniAppLaunchLogs_Status_CreatedAt_MiniAppId_AccountId",
                table: "MiniAppLaunchLogs");

            migrationBuilder.DropColumn(
                name: "Screenshots",
                table: "MiniApps");
        }
    }
}
