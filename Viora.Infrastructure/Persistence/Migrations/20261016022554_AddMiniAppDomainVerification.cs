using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppDomainVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SsoEnabled",
                table: "MiniApps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MiniAppDomainVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    VerificationMethod = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    VerificationFileName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    VerificationTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VerificationMaterialEncrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SecretKeyVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppDomainVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppDomainVerifications_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppDomainVerifications_Hostname",
                table: "MiniAppDomainVerifications",
                column: "Hostname");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppDomainVerifications_MiniAppId",
                table: "MiniAppDomainVerifications",
                column: "MiniAppId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppDomainVerifications_Status",
                table: "MiniAppDomainVerifications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppDomainVerifications_VerificationFileName",
                table: "MiniAppDomainVerifications",
                column: "VerificationFileName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppDomainVerifications");

            migrationBuilder.DropColumn(
                name: "SsoEnabled",
                table: "MiniApps");
        }
    }
}
