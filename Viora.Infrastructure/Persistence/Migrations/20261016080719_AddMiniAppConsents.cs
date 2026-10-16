using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniAppPairwiseSubjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<short>(type: "smallint", nullable: false),
                    Subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppPairwiseSubjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppPairwiseSubjects_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppPairwiseSubjects_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppUserConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    OAuthClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<short>(type: "smallint", nullable: false),
                    AuthorizationId = table.Column<string>(type: "text", nullable: true),
                    GrantedScopes = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppUserConsents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_MiniAppOAuthClients_OAuthClientId",
                        column: x => x.OAuthClientId,
                        principalTable: "MiniAppOAuthClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_OpenIddictAuthorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalTable: "OpenIddictAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPairwiseSubjects_AccountId_MiniAppId_Environment",
                table: "MiniAppPairwiseSubjects",
                columns: new[] { "AccountId", "MiniAppId", "Environment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPairwiseSubjects_MiniAppId_Environment_Subject",
                table: "MiniAppPairwiseSubjects",
                columns: new[] { "MiniAppId", "Environment", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_AccountId_OAuthClientId",
                table: "MiniAppUserConsents",
                columns: new[] { "AccountId", "OAuthClientId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_AuthorizationId",
                table: "MiniAppUserConsents",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_MiniAppId",
                table: "MiniAppUserConsents",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_OAuthClientId",
                table: "MiniAppUserConsents",
                column: "OAuthClientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppPairwiseSubjects");

            migrationBuilder.DropTable(
                name: "MiniAppUserConsents");
        }
    }
}
