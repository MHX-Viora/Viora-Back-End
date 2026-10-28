using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHybridMiniAppPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "AllowedOrigins",
                table: "MiniApps",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<short>(
                name: "AuthenticationMode",
                table: "MiniApps",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<string[]>(
                name: "CallbackUrls",
                table: "MiniApps",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "MiniApps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "ClientAuthenticationMethod",
                table: "MiniApps",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<int>(
                name: "PendingVersion",
                table: "MiniApps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishedConfigurationJson",
                table: "MiniApps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishedVersion",
                table: "MiniApps",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CodeChallenge",
                table: "MiniAppLaunchCodes",
                type: "character varying(43)",
                maxLength: 43,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedirectUri",
                table: "MiniAppLaunchCodes",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RuntimeSessionId",
                table: "MiniAppLaunchCodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateHash",
                table: "MiniAppLaunchCodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeveloperMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperMemberships_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeveloperMemberships_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppReports_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReports_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppRuntimeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppRuntimeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppRuntimeSessions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppRuntimeSessions_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppVerifiedDomains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    ChallengeToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppVerifiedDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppVerifiedDomains_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppVersions_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_CategoryId",
                table: "MiniApps",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_RuntimeSessionId",
                table: "MiniAppLaunchCodes",
                column: "RuntimeSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperMemberships_AccountId",
                table: "DeveloperMemberships",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperMemberships_DeveloperId_AccountId",
                table: "DeveloperMemberships",
                columns: new[] { "DeveloperId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppCategories_Slug",
                table: "MiniAppCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReports_AccountId",
                table: "MiniAppReports",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReports_MiniAppId_CreatedAt",
                table: "MiniAppReports",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRuntimeSessions_AccountId",
                table: "MiniAppRuntimeSessions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRuntimeSessions_ExpiresAt",
                table: "MiniAppRuntimeSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRuntimeSessions_MiniAppId",
                table: "MiniAppRuntimeSessions",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRuntimeSessions_TokenHash",
                table: "MiniAppRuntimeSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppVerifiedDomains_MiniAppId_Host",
                table: "MiniAppVerifiedDomains",
                columns: new[] { "MiniAppId", "Host" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppVersions_MiniAppId_Version",
                table: "MiniAppVersions",
                columns: new[] { "MiniAppId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MiniAppLaunchCodes_MiniAppRuntimeSessions_RuntimeSessionId",
                table: "MiniAppLaunchCodes",
                column: "RuntimeSessionId",
                principalTable: "MiniAppRuntimeSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MiniApps_MiniAppCategories_CategoryId",
                table: "MiniApps",
                column: "CategoryId",
                principalTable: "MiniAppCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Preserve existing published/review configurations as immutable v1.
            // Domain verification remains unset: legacy websites can launch, but
            // trusted bridge and SSO require the owner to complete verification.
            migrationBuilder.Sql("""
                UPDATE "MiniApps" SET "CallbackUrls" = ARRAY["CallbackUrl"] WHERE "CallbackUrl" <> '';
                UPDATE "MiniApps" SET "AllowedOrigins" = ARRAY[substring("WebUrl" from '^https://[^/?#]+')]
                    WHERE "WebUrl" LIKE 'https://%';
                INSERT INTO "DeveloperMemberships" ("Id", "DeveloperId", "AccountId", "Role", "CreatedAt")
                    SELECT "Id", "Id", "AccountId", 1, "CreatedAt" FROM "Developers" WHERE "AccountId" IS NOT NULL;
                INSERT INTO "MiniAppVersions" ("Id", "MiniAppId", "Version", "Status", "ConfigurationJson", "CreatedAt", "ReviewedAt")
                SELECT m."Id", m."Id", 1, CASE WHEN m."Status" = 1 THEN 1 ELSE 2 END,
                    jsonb_build_object(
                        'Name', m."Name", 'Slug', m."Slug", 'Description', m."Description", 'IconUrl', m."IconUrl", 'CoverUrl', m."CoverUrl",
                        'WebUrl', m."WebUrl", 'CallbackUrl', m."CallbackUrl", 'AllowedDomains', to_jsonb(m."AllowedDomains"),
                        'Permissions', COALESCE((SELECT jsonb_agg(p."Code" ORDER BY p."Code") FROM "MiniAppPermissionMappings" pm
                            JOIN "MiniAppPermissions" p ON p."Id" = pm."PermissionId" WHERE pm."MiniAppId" = m."Id"), '[]'::jsonb),
                        'IsFeatured', m."IsFeatured", 'CategoryId', NULL, 'AuthenticationMode', 'AnktSso',
                        'CallbackUrls', to_jsonb(m."CallbackUrls"), 'AllowedOrigins', to_jsonb(m."AllowedOrigins"),
                        'ClientAuthenticationMethod', 'ClientSecretPost', 'Version', NULL)::text,
                    m."CreatedAt", CASE WHEN m."Status" = 1 THEN NULL ELSE m."UpdatedAt" END
                FROM "MiniApps" m WHERE m."Status" IN (1, 2, 3) AND m."DeletedAt" IS NULL;
                UPDATE "MiniApps" m SET "PublishedVersion" = 1, "PublishedConfigurationJson" = v."ConfigurationJson"
                    FROM "MiniAppVersions" v WHERE v."MiniAppId" = m."Id" AND m."Status" IN (2, 3);
                UPDATE "MiniApps" SET "PendingVersion" = 1 WHERE "Status" = 1 AND "DeletedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MiniAppLaunchCodes_MiniAppRuntimeSessions_RuntimeSessionId",
                table: "MiniAppLaunchCodes");

            migrationBuilder.DropForeignKey(
                name: "FK_MiniApps_MiniAppCategories_CategoryId",
                table: "MiniApps");

            migrationBuilder.DropTable(
                name: "DeveloperMemberships");

            migrationBuilder.DropTable(
                name: "MiniAppCategories");

            migrationBuilder.DropTable(
                name: "MiniAppReports");

            migrationBuilder.DropTable(
                name: "MiniAppRuntimeSessions");

            migrationBuilder.DropTable(
                name: "MiniAppVerifiedDomains");

            migrationBuilder.DropTable(
                name: "MiniAppVersions");

            migrationBuilder.DropIndex(
                name: "IX_MiniApps_CategoryId",
                table: "MiniApps");

            migrationBuilder.DropIndex(
                name: "IX_MiniAppLaunchCodes_RuntimeSessionId",
                table: "MiniAppLaunchCodes");

            migrationBuilder.DropColumn(
                name: "AllowedOrigins",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "AuthenticationMode",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "CallbackUrls",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "ClientAuthenticationMethod",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "PendingVersion",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "PublishedConfigurationJson",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "PublishedVersion",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "CodeChallenge",
                table: "MiniAppLaunchCodes");

            migrationBuilder.DropColumn(
                name: "RedirectUri",
                table: "MiniAppLaunchCodes");

            migrationBuilder.DropColumn(
                name: "RuntimeSessionId",
                table: "MiniAppLaunchCodes");

            migrationBuilder.DropColumn(
                name: "StateHash",
                table: "MiniAppLaunchCodes");

        }
    }
}
