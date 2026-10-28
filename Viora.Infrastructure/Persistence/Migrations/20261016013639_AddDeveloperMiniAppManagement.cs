using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeveloperMiniAppManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniApps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    DeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ShortDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    WebsiteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PrivacyPolicyUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TermsOfServiceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SupportEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreateRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreateFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniApps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniApps_DeveloperAccounts_DeveloperAccountId",
                        column: x => x.DeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorDeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Metadata = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppAuditLogs_DeveloperAccounts_ActorDeveloperId",
                        column: x => x.ActorDeveloperId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppAuditLogs_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppLogoUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppLogoUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppLogoUploads_DeveloperAccounts_DeveloperAccountId",
                        column: x => x.DeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppLogoUploads_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppOAuthClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppOAuthClients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppOAuthClients_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppRedirectUris",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    RedirectUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppRedirectUris", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppRedirectUris_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppClientSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OAuthClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretEncrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SecretKeyVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppClientSecrets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppClientSecrets_MiniAppOAuthClients_OAuthClientId",
                        column: x => x.OAuthClientId,
                        principalTable: "MiniAppOAuthClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppAuditLogs_ActorDeveloperId",
                table: "MiniAppAuditLogs",
                column: "ActorDeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppAuditLogs_MiniAppId_CreatedAt",
                table: "MiniAppAuditLogs",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppClientSecrets_OAuthClientId",
                table: "MiniAppClientSecrets",
                column: "OAuthClientId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLogoUploads_DeveloperAccountId",
                table: "MiniAppLogoUploads",
                column: "DeveloperAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLogoUploads_ExpiresAt",
                table: "MiniAppLogoUploads",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLogoUploads_MiniAppId",
                table: "MiniAppLogoUploads",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppOAuthClients_ClientKey",
                table: "MiniAppOAuthClients",
                column: "ClientKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppOAuthClients_MiniAppId",
                table: "MiniAppOAuthClients",
                column: "MiniAppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRedirectUris_MiniAppId_RedirectUri",
                table: "MiniAppRedirectUris",
                columns: new[] { "MiniAppId", "RedirectUri" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_AppId",
                table: "MiniApps",
                column: "AppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_DeveloperAccountId_CreateRequestId",
                table: "MiniApps",
                columns: new[] { "DeveloperAccountId", "CreateRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_DeveloperAccountId_UpdatedAt",
                table: "MiniApps",
                columns: new[] { "DeveloperAccountId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_Status",
                table: "MiniApps",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppAuditLogs");

            migrationBuilder.DropTable(
                name: "MiniAppClientSecrets");

            migrationBuilder.DropTable(
                name: "MiniAppLogoUploads");

            migrationBuilder.DropTable(
                name: "MiniAppRedirectUris");

            migrationBuilder.DropTable(
                name: "MiniAppOAuthClients");

            migrationBuilder.DropTable(
                name: "MiniApps");
        }
    }
}
