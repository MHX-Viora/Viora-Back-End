using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSandboxTestIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSandbox",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SandboxIdentityTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Nickname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Birthdate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxIdentityTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SandboxIdentityTemplates_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SandboxTestIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxTestIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SandboxTestIdentities_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SandboxTestIdentities_DeveloperAccounts_DeveloperAccountId",
                        column: x => x.DeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SandboxTestIdentities_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SandboxIdentitySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OAuthClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    LaunchHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LaunchExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LaunchConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BrowserHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AuthorizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokenIssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserInfoAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxIdentitySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SandboxIdentitySessions_DeveloperSessions_DeveloperSessionId",
                        column: x => x.DeveloperSessionId,
                        principalTable: "DeveloperSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SandboxIdentitySessions_MiniAppOAuthClients_OAuthClientId",
                        column: x => x.OAuthClientId,
                        principalTable: "MiniAppOAuthClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SandboxIdentitySessions_SandboxTestIdentities_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "SandboxTestIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxIdentitySessions_BrowserHash",
                table: "SandboxIdentitySessions",
                column: "BrowserHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxIdentitySessions_DeveloperSessionId_ExpiresAt",
                table: "SandboxIdentitySessions",
                columns: new[] { "DeveloperSessionId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxIdentitySessions_IdentityId",
                table: "SandboxIdentitySessions",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxIdentitySessions_LaunchHash",
                table: "SandboxIdentitySessions",
                column: "LaunchHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxIdentitySessions_OAuthClientId",
                table: "SandboxIdentitySessions",
                column: "OAuthClientId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxIdentityTemplates_AccountId",
                table: "SandboxIdentityTemplates",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxTestIdentities_AccountId",
                table: "SandboxTestIdentities",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxTestIdentities_DeveloperAccountId_MiniAppId",
                table: "SandboxTestIdentities",
                columns: new[] { "DeveloperAccountId", "MiniAppId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxTestIdentities_MiniAppId",
                table: "SandboxTestIdentities",
                column: "MiniAppId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Accounts" WHERE "IsSandbox") THEN
                    RAISE EXCEPTION 'Cannot remove Sandbox isolation while Sandbox accounts exist. Keep the marker and review account/history cleanup before downgrade.';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "SandboxIdentitySessions");

            migrationBuilder.DropTable(
                name: "SandboxIdentityTemplates");

            migrationBuilder.DropTable(
                name: "SandboxTestIdentities");

            migrationBuilder.DropColumn(
                name: "IsSandbox",
                table: "Accounts");
        }
    }
}
