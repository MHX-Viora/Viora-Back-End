using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeveloperPlatformLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfigurationHash",
                table: "MiniAppVersions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewState",
                table: "MiniAppVersions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<string>(
                name: "TechnicalTestJson",
                table: "MiniAppVersions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TestedAt",
                table: "MiniAppVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgeRating",
                table: "MiniApps",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Everyone");

            migrationBuilder.AddColumn<string>(
                name: "DevelopmentUrl",
                table: "MiniApps",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntegrationType",
                table: "MiniApps",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Website");

            migrationBuilder.AddColumn<string>(
                name: "PrivacyPolicyUrl",
                table: "MiniApps",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "ReleaseRegions",
                table: "MiniApps",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "SsoMode",
                table: "MiniApps",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Disabled");

            migrationBuilder.AddColumn<string>(
                name: "SupportEmail",
                table: "MiniApps",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsUrl",
                table: "MiniApps",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Developers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessRegistrationNumber",
                table: "Developers",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Developers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Developers",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftApplicationJson",
                table: "Developers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "Developers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartnerType",
                table: "Developers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Individual");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationStatus",
                table: "Developers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<int>(
                name: "RegistrationVersion",
                table: "Developers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RepresentativeName",
                table: "Developers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradingName",
                table: "Developers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Preserve the interpretation of existing registrations and releases.
            // Existing approved versions remain live; missing technical evidence
            // must be supplied only when submitting a new version for review.
            migrationBuilder.Sql("""
                UPDATE "MiniApps"
                SET "IntegrationType" = CASE WHEN "AuthenticationMode" = 1 THEN 'Sso' ELSE 'Website' END,
                    "SsoMode" = CASE WHEN "AuthenticationMode" = 1 THEN 'Optional' ELSE 'Disabled' END;
                UPDATE "MiniAppVersions"
                SET "ReviewState" = CASE "Status"
                    WHEN 1 THEN 'Submitted' WHEN 2 THEN 'Approved'
                    WHEN 4 THEN 'Rejected' ELSE 'Draft' END;
                """);

            migrationBuilder.CreateTable(
                name: "DeveloperApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TermsAcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperApplications_Accounts_SubmittedByAccountId",
                        column: x => x.SubmittedByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeveloperApplications_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperEmailVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestWindow = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    DeliverySucceeded = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperEmailVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperEmailVerifications_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperReauths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProofHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperReauths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperReauths_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeveloperReauths_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperReviewHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ReviewerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InternalNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperReviewHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperReviewHistory_Accounts_ReviewerAccountId",
                        column: x => x.ReviewerAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeveloperReviewHistory_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppTechnicalTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppTechnicalTests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppTechnicalTests_Accounts_TestedByAccountId",
                        column: x => x.TestedByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppTechnicalTests_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperApplications_DeveloperId_Version",
                table: "DeveloperApplications",
                columns: new[] { "DeveloperId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperApplications_SubmittedByAccountId",
                table: "DeveloperApplications",
                column: "SubmittedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperEmailVerifications_AccountId_CreatedAt",
                table: "DeveloperEmailVerifications",
                columns: new[] { "AccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperEmailVerifications_AccountId_RequestWindow",
                table: "DeveloperEmailVerifications",
                columns: new[] { "AccountId", "RequestWindow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperEmailVerifications_ExpiresAt",
                table: "DeveloperEmailVerifications",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_AccountId",
                table: "DeveloperReauths",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_ExpiresAt",
                table: "DeveloperReauths",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_MiniAppId",
                table: "DeveloperReauths",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_ProofHash",
                table: "DeveloperReauths",
                column: "ProofHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReviewHistory_DeveloperId_CreatedAt",
                table: "DeveloperReviewHistory",
                columns: new[] { "DeveloperId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReviewHistory_ReviewerAccountId",
                table: "DeveloperReviewHistory",
                column: "ReviewerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppTechnicalTests_MiniAppId_CreatedAt",
                table: "MiniAppTechnicalTests",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppTechnicalTests_TestedByAccountId",
                table: "MiniAppTechnicalTests",
                column: "TestedByAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeveloperApplications");

            migrationBuilder.DropTable(
                name: "DeveloperEmailVerifications");

            migrationBuilder.DropTable(
                name: "DeveloperReauths");

            migrationBuilder.DropTable(
                name: "DeveloperReviewHistory");

            migrationBuilder.DropTable(
                name: "MiniAppTechnicalTests");

            migrationBuilder.DropColumn(
                name: "ConfigurationHash",
                table: "MiniAppVersions");

            migrationBuilder.DropColumn(
                name: "ReviewState",
                table: "MiniAppVersions");

            migrationBuilder.DropColumn(
                name: "TechnicalTestJson",
                table: "MiniAppVersions");

            migrationBuilder.DropColumn(
                name: "TestedAt",
                table: "MiniAppVersions");

            migrationBuilder.DropColumn(
                name: "AgeRating",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "DevelopmentUrl",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "IntegrationType",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "PrivacyPolicyUrl",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "ReleaseRegions",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "SsoMode",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "SupportEmail",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "TermsUrl",
                table: "MiniApps");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "BusinessRegistrationNumber",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "DraftApplicationJson",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "PartnerType",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "RegistrationStatus",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "RegistrationVersion",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "RepresentativeName",
                table: "Developers");

            migrationBuilder.DropColumn(
                name: "TradingName",
                table: "Developers");
        }
    }
}
