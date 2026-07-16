using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameDeviceTokensToUserDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceTokens_Users_UserId",
                table: "DeviceTokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DeviceTokens",
                table: "DeviceTokens");

            migrationBuilder.RenameTable(
                name: "DeviceTokens",
                newName: "UserDevices");

            migrationBuilder.RenameColumn(
                name: "Token",
                table: "UserDevices",
                newName: "FcmToken");

            migrationBuilder.RenameIndex(
                name: "IX_DeviceTokens_UserId_IsActive",
                table: "UserDevices",
                newName: "IX_UserDevices_UserId_IsActive");

            migrationBuilder.RenameIndex(
                name: "IX_DeviceTokens_Token",
                table: "UserDevices",
                newName: "IX_UserDevices_FcmToken");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserDevices",
                table: "UserDevices",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_DeviceId",
                table: "UserDevices",
                column: "DeviceId",
                unique: true,
                filter: "\"DeviceId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_UserDevices_Users_UserId",
                table: "UserDevices",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserDevices_Users_UserId",
                table: "UserDevices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserDevices",
                table: "UserDevices");

            migrationBuilder.DropIndex(
                name: "IX_UserDevices_DeviceId",
                table: "UserDevices");

            migrationBuilder.RenameTable(
                name: "UserDevices",
                newName: "DeviceTokens");

            migrationBuilder.RenameColumn(
                name: "FcmToken",
                table: "DeviceTokens",
                newName: "Token");

            migrationBuilder.RenameIndex(
                name: "IX_UserDevices_UserId_IsActive",
                table: "DeviceTokens",
                newName: "IX_DeviceTokens_UserId_IsActive");

            migrationBuilder.RenameIndex(
                name: "IX_UserDevices_FcmToken",
                table: "DeviceTokens",
                newName: "IX_DeviceTokens_Token");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeviceTokens",
                table: "DeviceTokens",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceTokens_Users_UserId",
                table: "DeviceTokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
