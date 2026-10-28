using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations;

// Retired stores remain available for retention and rollback. Purging them is a
// separate, explicitly approved data operation; this migration never drops rows.
[DbContext(typeof(AppDbContext))]
[Migration("20261026040000_RetireMiniAppLegacySsoStores")]
public sealed class RetireMiniAppLegacySsoStores : Migration
{
    private static readonly string[] Tables =
    [
        "OpenIddictApplications", "OpenIddictAuthorizations", "OpenIddictScopes", "OpenIddictTokens",
        "AnktSsoSessions", "SsoLoginTransactions", "MiniAppPairwiseSubjects", "MiniAppUserConsents",
        "MiniAppAccountLinks", "SsoAccountLinkChallenges", "SsoAccountLinkAudits",
        "SandboxTestIdentities", "SandboxIdentitySessions"
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("legacy_sso");
        foreach (var table in Tables)
            migrationBuilder.Sql($"ALTER TABLE public.\"{table}\" SET SCHEMA legacy_sso;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables.Reverse())
            migrationBuilder.Sql($"ALTER TABLE legacy_sso.\"{table}\" SET SCHEMA public;");
        // Keep the empty archive schema: do not remove unrelated operator data.
    }
}
