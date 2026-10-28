using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSsoClientEnvironments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MiniAppRedirectUris_MiniAppId_RedirectUri",
                table: "MiniAppRedirectUris");

            migrationBuilder.DropIndex(
                name: "IX_MiniAppOAuthClients_MiniAppId",
                table: "MiniAppOAuthClients");

            migrationBuilder.AddColumn<short>(
                name: "Environment",
                table: "MiniAppRedirectUris",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "AllowedScopes",
                table: "MiniAppOAuthClients",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<short>(
                name: "Environment",
                table: "MiniAppOAuthClients",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "SandboxEnabled",
                table: "MiniAppOAuthClients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "MiniAppOAuthClients",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRedirectUris_MiniAppId_Environment_RedirectUri",
                table: "MiniAppRedirectUris",
                columns: new[] { "MiniAppId", "Environment", "RedirectUri" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppOAuthClients_MiniAppId_Environment",
                table: "MiniAppOAuthClients",
                columns: new[] { "MiniAppId", "Environment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MiniAppRedirectUris_MiniAppId_Environment_RedirectUri",
                table: "MiniAppRedirectUris");

            migrationBuilder.DropIndex(
                name: "IX_MiniAppOAuthClients_MiniAppId_Environment",
                table: "MiniAppOAuthClients");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "MiniAppRedirectUris");

            migrationBuilder.DropColumn(
                name: "AllowedScopes",
                table: "MiniAppOAuthClients");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "MiniAppOAuthClients");

            migrationBuilder.DropColumn(
                name: "SandboxEnabled",
                table: "MiniAppOAuthClients");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MiniAppOAuthClients");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRedirectUris_MiniAppId_RedirectUri",
                table: "MiniAppRedirectUris",
                columns: new[] { "MiniAppId", "RedirectUri" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppOAuthClients_MiniAppId",
                table: "MiniAppOAuthClients",
                column: "MiniAppId",
                unique: true);
        }
    }
}
