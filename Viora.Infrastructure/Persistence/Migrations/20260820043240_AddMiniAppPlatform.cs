using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Developers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Website = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Developers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Developers_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppLaunchLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppLaunchLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MiniApps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IconUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CoverUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    WebUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CallbackUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ClientSecretHash = table.Column<string>(type: "text", nullable: false),
                    SecretRotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedDomains = table.Column<string[]>(type: "text[]", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniApps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniApps_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppExternalIdentities_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppExternalIdentities_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppLaunchCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppLaunchCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppLaunchCodes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppLaunchCodes_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppPermissionMappings",
                columns: table => new
                {
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppPermissionMappings", x => new { x.MiniAppId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_MiniAppPermissionMappings_MiniAppPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "MiniAppPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppPermissionMappings_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppUserConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Granted = table.Column<bool>(type: "boolean", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppUserConsents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_MiniAppPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "MiniAppPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "MiniAppPermissions",
                columns: new[] { "Id", "Code", "CreatedAt", "Description", "IsSensitive", "Name", "Status", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111101"), "identity.login", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Đăng nhập ANKT", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111102"), "profile.basic", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Hồ sơ cơ bản", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111103"), "profile.email", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Địa chỉ email", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111104"), "profile.phone", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Số điện thoại", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111105"), "app.close", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Đóng Mini App", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111106"), "app.open_url", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Mở liên kết ngoài", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111107"), "app.theme", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Đọc giao diện", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Developers_AccountId",
                table: "Developers",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Developers_Email",
                table: "Developers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Developers_Status",
                table: "Developers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppAuditLogs_MiniAppId_CreatedAt",
                table: "MiniAppAuditLogs",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppExternalIdentities_AccountId_MiniAppId",
                table: "MiniAppExternalIdentities",
                columns: new[] { "AccountId", "MiniAppId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppExternalIdentities_MiniAppId_Subject",
                table: "MiniAppExternalIdentities",
                columns: new[] { "MiniAppId", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_AccountId",
                table: "MiniAppLaunchCodes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_CodeHash",
                table: "MiniAppLaunchCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_ExpiresAt",
                table: "MiniAppLaunchCodes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_MiniAppId",
                table: "MiniAppLaunchCodes",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchLogs_MiniAppId_CreatedAt",
                table: "MiniAppLaunchLogs",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPermissionMappings_PermissionId",
                table: "MiniAppPermissionMappings",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPermissions_Code",
                table: "MiniAppPermissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_ClientId",
                table: "MiniApps",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_DeveloperId",
                table: "MiniApps",
                column: "DeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_Slug",
                table: "MiniApps",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_Status",
                table: "MiniApps",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_AccountId_MiniAppId",
                table: "MiniAppUserConsents",
                columns: new[] { "AccountId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_AccountId_MiniAppId_PermissionId",
                table: "MiniAppUserConsents",
                columns: new[] { "AccountId", "MiniAppId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_MiniAppId",
                table: "MiniAppUserConsents",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_PermissionId",
                table: "MiniAppUserConsents",
                column: "PermissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppAuditLogs");

            migrationBuilder.DropTable(
                name: "MiniAppExternalIdentities");

            migrationBuilder.DropTable(
                name: "MiniAppLaunchCodes");

            migrationBuilder.DropTable(
                name: "MiniAppLaunchLogs");

            migrationBuilder.DropTable(
                name: "MiniAppPermissionMappings");

            migrationBuilder.DropTable(
                name: "MiniAppUserConsents");

            migrationBuilder.DropTable(
                name: "MiniAppPermissions");

            migrationBuilder.DropTable(
                name: "MiniApps");

            migrationBuilder.DropTable(
                name: "Developers");
        }
    }
}
