using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeDeveloperPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeveloperMemberships_AccountId",
                table: "DeveloperMemberships");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "MiniAppVerifiedDomains",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "MiniAppVerifiedDomains",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedChallengeToken",
                table: "MiniAppVerifiedDomains",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationFileName",
                table: "MiniAppVerifiedDomains",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreviousClientSecretExpiresAt",
                table: "MiniApps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousClientSecretHash",
                table: "MiniApps",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeveloperId",
                table: "DeveloperReauths",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderAuthenticationHash",
                table: "DeveloperReauths",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MiniAppMemberInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppMemberInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppMemberInvitations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppMemberInvitations_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppMemberships_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppMemberships_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_DeveloperId",
                table: "DeveloperReauths",
                column: "DeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_ProviderAuthenticationHash",
                table: "DeveloperReauths",
                column: "ProviderAuthenticationHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperMemberships_AccountId",
                table: "DeveloperMemberships",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppMemberInvitations_AccountId_ExpiresAt",
                table: "MiniAppMemberInvitations",
                columns: new[] { "AccountId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppMemberInvitations_MiniAppId",
                table: "MiniAppMemberInvitations",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppMemberships_AccountId",
                table: "MiniAppMemberships",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppMemberships_MiniAppId_AccountId",
                table: "MiniAppMemberships",
                columns: new[] { "MiniAppId", "AccountId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DeveloperReauths_Developers_DeveloperId",
                table: "DeveloperReauths",
                column: "DeveloperId",
                principalTable: "Developers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "MiniAppMemberships")
                        OR EXISTS (SELECT 1 FROM "MiniAppMemberInvitations")
                        OR EXISTS (SELECT 1 FROM "MiniAppVerifiedDomains" WHERE "VerificationFileName" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "MiniApps" WHERE "PreviousClientSecretHash" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "Accounts" WHERE "EmailVerifiedAt" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "DeveloperReauths" WHERE "ProviderAuthenticationHash" IS NOT NULL OR "DeveloperId" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "DeveloperMemberships" GROUP BY "AccountId" HAVING COUNT(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Rollback refused: upgraded Developer data exists. Restore a backup or migrate this data explicitly before downgrading.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_DeveloperReauths_Developers_DeveloperId",
                table: "DeveloperReauths");

            migrationBuilder.DropTable(
                name: "MiniAppMemberInvitations");

            migrationBuilder.DropTable(
                name: "MiniAppMemberships");

            migrationBuilder.DropIndex(
                name: "IX_DeveloperReauths_DeveloperId",
                table: "DeveloperReauths");

            migrationBuilder.DropIndex(
                name: "IX_DeveloperReauths_ProviderAuthenticationHash",
                table: "DeveloperReauths");

            migrationBuilder.DropIndex(
                name: "IX_DeveloperMemberships_AccountId",
                table: "DeveloperMemberships");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "MiniAppVerifiedDomains");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "MiniAppVerifiedDomains");

            migrationBuilder.DropColumn(
                name: "ProtectedChallengeToken",
                table: "MiniAppVerifiedDomains");

            migrationBuilder.DropColumn(
                name: "VerificationFileName",
                table: "MiniAppVerifiedDomains");

            migrationBuilder.DropColumn(
                name: "PreviousClientSecretExpiresAt",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "PreviousClientSecretHash",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "DeveloperId",
                table: "DeveloperReauths");

            migrationBuilder.DropColumn(
                name: "ProviderAuthenticationHash",
                table: "DeveloperReauths");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperMemberships_AccountId",
                table: "DeveloperMemberships",
                column: "AccountId",
                unique: true);
        }
    }
}
