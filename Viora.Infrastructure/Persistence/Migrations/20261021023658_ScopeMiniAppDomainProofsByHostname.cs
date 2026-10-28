using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeMiniAppDomainProofsByHostname : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MiniAppDomainVerifications_MiniAppId",
                table: "MiniAppDomainVerifications");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppDomainVerifications_MiniAppId_Hostname",
                table: "MiniAppDomainVerifications",
                columns: new[] { "MiniAppId", "Hostname" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "MiniAppDomainVerifications" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "MiniAppDomainVerifications" WHERE "RevokedAt" IS NULL GROUP BY "MiniAppId" HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Explicitly resolve multiple live host proofs before restoring the legacy app-only index' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_MiniAppDomainVerifications_MiniAppId_Hostname",
                table: "MiniAppDomainVerifications");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppDomainVerifications_MiniAppId",
                table: "MiniAppDomainVerifications",
                column: "MiniAppId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }
    }
}
