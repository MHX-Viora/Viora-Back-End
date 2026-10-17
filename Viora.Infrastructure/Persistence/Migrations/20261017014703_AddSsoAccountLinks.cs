using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSsoAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniAppAccountLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<short>(type: "smallint", nullable: false),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PartnerAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnlinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppAccountLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppAccountLinks_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppAccountLinks_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SsoAccountLinkChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    OAuthClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientSecretId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<short>(type: "smallint", nullable: false),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PartnerAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProofHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SsoAccountLinkChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SsoAccountLinkChallenges_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SsoAccountLinkChallenges_MiniAppClientSecrets_ClientSecretId",
                        column: x => x.ClientSecretId,
                        principalTable: "MiniAppClientSecrets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SsoAccountLinkChallenges_MiniAppOAuthClients_OAuthClientId",
                        column: x => x.OAuthClientId,
                        principalTable: "MiniAppOAuthClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SsoAccountLinkChallenges_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SsoAccountLinkAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<short>(type: "smallint", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SsoAccountLinkAudits", x => x.Id);
                    table.CheckConstraint("CK_SsoAccountLinkAudits_Action", "\"Action\" IN ('link', 'unlink', 'relink')");
                    table.ForeignKey(
                        name: "FK_SsoAccountLinkAudits_MiniAppAccountLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "MiniAppAccountLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppAccountLinks_AccountId_MiniAppId_Environment",
                table: "MiniAppAccountLinks",
                columns: new[] { "AccountId", "MiniAppId", "Environment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppAccountLinks_MiniAppId_Environment_PartnerAccountId",
                table: "MiniAppAccountLinks",
                columns: new[] { "MiniAppId", "Environment", "PartnerAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppAccountLinks_MiniAppId_Environment_Subject",
                table: "MiniAppAccountLinks",
                columns: new[] { "MiniAppId", "Environment", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SsoAccountLinkAudits_AccountId_CreatedAt",
                table: "SsoAccountLinkAudits",
                columns: new[] { "AccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SsoAccountLinkAudits_LinkId",
                table: "SsoAccountLinkAudits",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_SsoAccountLinkChallenges_AccountId",
                table: "SsoAccountLinkChallenges",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SsoAccountLinkChallenges_ClientSecretId",
                table: "SsoAccountLinkChallenges",
                column: "ClientSecretId");

            migrationBuilder.CreateIndex(
                name: "IX_SsoAccountLinkChallenges_ExpiresAt",
                table: "SsoAccountLinkChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SsoAccountLinkChallenges_MiniAppId",
                table: "SsoAccountLinkChallenges",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_SsoAccountLinkChallenges_OAuthClientId",
                table: "SsoAccountLinkChallenges",
                column: "OAuthClientId");

            migrationBuilder.CreateIndex(
                name: "IX_SsoAccountLinkChallenges_ProofHash",
                table: "SsoAccountLinkChallenges",
                column: "ProofHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SsoAccountLinkAudits");

            migrationBuilder.DropTable(
                name: "SsoAccountLinkChallenges");

            migrationBuilder.DropTable(
                name: "MiniAppAccountLinks");
        }
    }
}
