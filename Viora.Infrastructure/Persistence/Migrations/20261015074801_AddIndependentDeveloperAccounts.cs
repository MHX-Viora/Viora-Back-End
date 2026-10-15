using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentDeveloperAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeveloperAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EmailNormalized = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    EmailVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TermsAcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    FailedLoginAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SecurityVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperActionTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<short>(type: "smallint", nullable: false),
                    SecurityVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperActionTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperActionTokens_DeveloperAccounts_DeveloperAccountId",
                        column: x => x.DeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Metadata = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperAuditLogs_DeveloperAccounts_DeveloperAccountId",
                        column: x => x.DeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProviderSubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperExternalIdentities_DeveloperAccounts_DeveloperAcco~",
                        column: x => x.DeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AbsoluteExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperSessions_DeveloperAccounts_DeveloperAccountId",
                        column: x => x.DeveloperAccountId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperAccounts_EmailNormalized",
                table: "DeveloperAccounts",
                column: "EmailNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperActionTokens_DeveloperAccountId_Purpose_CreatedAt",
                table: "DeveloperActionTokens",
                columns: new[] { "DeveloperAccountId", "Purpose", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperActionTokens_ExpiresAt",
                table: "DeveloperActionTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperActionTokens_TokenHash",
                table: "DeveloperActionTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperAuditLogs_CreatedAt",
                table: "DeveloperAuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperAuditLogs_DeveloperAccountId_CreatedAt",
                table: "DeveloperAuditLogs",
                columns: new[] { "DeveloperAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperExternalIdentities_DeveloperAccountId",
                table: "DeveloperExternalIdentities",
                column: "DeveloperAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperExternalIdentities_Provider_ProviderSubjectId",
                table: "DeveloperExternalIdentities",
                columns: new[] { "Provider", "ProviderSubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperSessions_DeveloperAccountId_RevokedAt_ExpiresAt",
                table: "DeveloperSessions",
                columns: new[] { "DeveloperAccountId", "RevokedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperSessions_ExpiresAt",
                table: "DeveloperSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperSessions_TokenHash",
                table: "DeveloperSessions",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeveloperActionTokens");

            migrationBuilder.DropTable(
                name: "DeveloperAuditLogs");

            migrationBuilder.DropTable(
                name: "DeveloperExternalIdentities");

            migrationBuilder.DropTable(
                name: "DeveloperSessions");

            migrationBuilder.DropTable(
                name: "DeveloperAccounts");
        }
    }
}
