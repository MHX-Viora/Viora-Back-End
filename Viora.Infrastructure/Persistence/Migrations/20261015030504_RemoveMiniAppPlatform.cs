using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations;

public partial class RemoveMiniAppPlatform : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // One statement resolves internal dependencies. RESTRICT refuses external ones.
        migrationBuilder.Sql("""
            SET LOCAL lock_timeout = '15s';
            DROP TABLE IF EXISTS
                "public"."DeveloperApplications",
                "public"."DeveloperEmailVerifications",
                "public"."DeveloperMemberships",
                "public"."DeveloperReauths",
                "public"."Developers",
                "public"."MiniAppAuditLogs",
                "public"."MiniAppBanners",
                "public"."MiniAppCategories",
                "public"."MiniAppExternalIdentities",
                "public"."MiniAppFavorites",
                "public"."MiniAppLaunchLogs",
                "public"."MiniAppMemberInvitations",
                "public"."MiniAppMemberships",
                "public"."MiniAppPermissionMappings",
                "public"."MiniAppPermissions",
                "public"."MiniAppPlacements",
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
                "public"."__OpenApiMigrationsHistory" RESTRICT;
            ALTER TABLE "public"."Accounts" DROP COLUMN IF EXISTS "MiniAppHistoryClearedBefore" RESTRICT;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Mini App removal is destructive. Restore the pre-removal backup before reintroducing the platform.");
}
