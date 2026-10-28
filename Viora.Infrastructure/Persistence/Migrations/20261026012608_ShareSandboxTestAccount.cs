using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShareSandboxTestAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SandboxTestIdentities_AccountId",
                table: "SandboxTestIdentities");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxTestIdentities_AccountId",
                table: "SandboxTestIdentities",
                column: "AccountId");

            // Keep old accounts for audit; changing a mapping invalidates every
            // old session before it can represent the shared account.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (
                    SELECT 1 FROM "SandboxIdentityTemplates" t JOIN "Accounts" a ON a."Id" = t."AccountId"
                    WHERE NOT a."IsSandbox" OR a."Role" <> 0 OR a."DeletedAt" IS NOT NULL
                  ) OR EXISTS (
                    SELECT 1 FROM "SandboxTestIdentities" i JOIN "Accounts" a ON a."Id" = i."AccountId"
                    WHERE NOT a."IsSandbox"
                  ) THEN RAISE EXCEPTION 'Unsafe Sandbox mapping; no production account may be adopted'; END IF;
                END $$;
                UPDATE "SandboxIdentitySessions" s SET "RevokedAt" = CURRENT_TIMESTAMP
                FROM "SandboxTestIdentities" i, "SandboxIdentityTemplates" t
                WHERE s."IdentityId" = i."Id" AND i."AccountId" <> t."AccountId"
                  AND t."Id" = 'ac744f84-d1eb-4c32-a8a0-9ac94165be10' AND s."RevokedAt" IS NULL;
                UPDATE "SandboxTestIdentities" i SET "AccountId" = t."AccountId"
                FROM "SandboxIdentityTemplates" t
                WHERE t."Id" = 'ac744f84-d1eb-4c32-a8a0-9ac94165be10' AND i."AccountId" <> t."AccountId";
                UPDATE "Accounts" a SET "Email" = 'sandbox-test@domain.test'
                FROM "SandboxIdentityTemplates" t WHERE t."AccountId" = a."Id" AND a."IsSandbox"
                  AND a."Email" = 'sandbox@example.test';
                UPDATE "Users" u SET "DisplayName" = 'ANKT Sandbox User', "IsVerified" = TRUE
                FROM "SandboxIdentityTemplates" t WHERE t."AccountId" = u."AccountId" AND u."DisplayName" = 'ANKT Test';
                UPDATE "SandboxIdentityTemplates" SET "FullName" = 'ANKT Developer Test', "Nickname" = 'ankt_sandbox_test'
                WHERE "FullName" = 'ANKT Sandbox User' AND "Nickname" = 'ankt_sandbox';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT "AccountId" FROM "SandboxTestIdentities" GROUP BY "AccountId" HAVING COUNT(*) > 1)
                  THEN RAISE EXCEPTION 'Cannot restore unique Sandbox accounts while shared mappings exist'; END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_SandboxTestIdentities_AccountId",
                table: "SandboxTestIdentities");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxTestIdentities_AccountId",
                table: "SandboxTestIdentities",
                column: "AccountId",
                unique: true);
        }
    }
}
