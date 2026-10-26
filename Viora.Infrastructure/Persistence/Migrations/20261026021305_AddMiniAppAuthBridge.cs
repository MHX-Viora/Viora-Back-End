using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppAuthBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniAppBridgeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Origin = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Policy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeChallenge = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    CodeExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AccessHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccessExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppBridgeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppBridgeSessions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppBridgeSessions_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppBridgeSessions_AccessHash",
                table: "MiniAppBridgeSessions",
                column: "AccessHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppBridgeSessions_AccountId",
                table: "MiniAppBridgeSessions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppBridgeSessions_ActorId_SourceSessionId_ExpiresAt",
                table: "MiniAppBridgeSessions",
                columns: new[] { "ActorId", "SourceSessionId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppBridgeSessions_CodeHash",
                table: "MiniAppBridgeSessions",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppBridgeSessions_MiniAppId",
                table: "MiniAppBridgeSessions",
                column: "MiniAppId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppBridgeSessions");
        }
    }
}
