using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRetiredPartnerPlatformTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Explicit allowlist, no CASCADE: unrelated dependencies must fail the transaction.
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS
                    "public"."DeveloperApplications",
                    "public"."DeveloperEmailVerifications",
                    "public"."DeveloperMemberships",
                    "public"."DeveloperReauths",
                    "public"."DeveloperReviewHistory",
                    "public"."Developers",
                    "public"."MiniAppAuditLogs",
                    "public"."MiniAppCategories",
                    "public"."MiniAppExternalIdentities",
                    "public"."MiniAppLaunchCodes",
                    "public"."MiniAppLaunchLogs",
                    "public"."MiniAppPermissionMappings",
                    "public"."MiniAppPermissions",
                    "public"."MiniAppReports",
                    "public"."MiniAppRuntimeSessions",
                    "public"."MiniAppTechnicalTests",
                    "public"."MiniAppUserConsents",
                    "public"."MiniAppVerifiedDomains",
                    "public"."MiniAppVersions",
                    "public"."MiniApps",
                    "public"."OpenIddictApplications",
                    "public"."OpenIddictAuthorizations",
                    "public"."OpenIddictScopes",
                    "public"."OpenIddictTokens",
                    "public"."__OpenApiMigrationsHistory"
                RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Removed partner-platform data can only be restored from a verified backup.");
        }
    }
}
