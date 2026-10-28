using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppSandboxSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniAppSandboxSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OAuthClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<short>(type: "smallint", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ActorDeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorAdminAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDeveloperSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceSocialSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfigurationFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WebsiteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PartnerOrigin = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RuntimeCredentialHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppSandboxSessions", x => x.Id);
                    table.CheckConstraint("CK_MiniAppSandboxSessions_Actor", "(\"ActorType\" = 'Developer' AND \"ActorDeveloperAccountId\" IS NOT NULL AND \"ActorDeveloperAccountId\" = \"DeveloperAccountId\" AND \"SourceDeveloperSessionId\" IS NOT NULL AND \"ActorAdminAccountId\" IS NULL AND \"SourceSocialSessionId\" IS NULL) OR (\"ActorType\" = 'Admin' AND \"ActorAdminAccountId\" IS NOT NULL AND \"SourceSocialSessionId\" IS NOT NULL AND \"ActorDeveloperAccountId\" IS NULL AND \"SourceDeveloperSessionId\" IS NULL)");
                    table.CheckConstraint("CK_MiniAppSandboxSessions_Environment", "\"Environment\" = 0");
                    table.CheckConstraint("CK_MiniAppSandboxSessions_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxSessions_Accounts_ActorAdminAccountId",
                        column: x => x.ActorAdminAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxSessions_DeveloperAccounts_ActorDeveloperAcco~",
                        column: x => x.ActorDeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxSessions_DeveloperAccounts_DeveloperAccountId",
                        column: x => x.DeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxSessions_DeveloperSessions_SourceDeveloperSes~",
                        column: x => x.SourceDeveloperSessionId,
                        principalTable: "DeveloperSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxSessions_MiniAppOAuthClients_OAuthClientId",
                        column: x => x.OAuthClientId,
                        principalTable: "MiniAppOAuthClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxSessions_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppSandboxLaunchGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SandboxSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BrowserNonceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Audience = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppSandboxLaunchGrants", x => x.Id);
                    table.CheckConstraint("CK_MiniAppSandboxLaunchGrants_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxLaunchGrants_MiniAppSandboxSessions_SandboxSe~",
                        column: x => x.SandboxSessionId,
                        principalTable: "MiniAppSandboxSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxLaunchGrants_ExpiresAt",
                table: "MiniAppSandboxLaunchGrants",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxLaunchGrants_GrantHash",
                table: "MiniAppSandboxLaunchGrants",
                column: "GrantHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxLaunchGrants_SandboxSessionId",
                table: "MiniAppSandboxLaunchGrants",
                column: "SandboxSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxSessions_ActorAdminAccountId_RevokedAt_Expire~",
                table: "MiniAppSandboxSessions",
                columns: new[] { "ActorAdminAccountId", "RevokedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxSessions_ActorDeveloperAccountId_RevokedAt_Ex~",
                table: "MiniAppSandboxSessions",
                columns: new[] { "ActorDeveloperAccountId", "RevokedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxSessions_DeveloperAccountId",
                table: "MiniAppSandboxSessions",
                column: "DeveloperAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxSessions_ExpiresAt",
                table: "MiniAppSandboxSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxSessions_MiniAppId_CreatedAt",
                table: "MiniAppSandboxSessions",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxSessions_OAuthClientId",
                table: "MiniAppSandboxSessions",
                column: "OAuthClientId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxSessions_RuntimeCredentialHash",
                table: "MiniAppSandboxSessions",
                column: "RuntimeCredentialHash",
                unique: true,
                filter: "\"RuntimeCredentialHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxSessions_SourceDeveloperSessionId",
                table: "MiniAppSandboxSessions",
                column: "SourceDeveloperSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppSandboxLaunchGrants");

            migrationBuilder.DropTable(
                name: "MiniAppSandboxSessions");
        }
    }
}
