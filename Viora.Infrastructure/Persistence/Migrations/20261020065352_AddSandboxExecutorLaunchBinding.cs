using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSandboxExecutorLaunchBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExecutorRunId",
                table: "MiniAppSandboxLaunchGrants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutorRunVersion",
                table: "MiniAppSandboxLaunchGrants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxLaunchGrants_ExecutorRunId_SandboxSessionId",
                table: "MiniAppSandboxLaunchGrants",
                columns: new[] { "ExecutorRunId", "SandboxSessionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SandboxGrants_ExecutorBinding",
                table: "MiniAppSandboxLaunchGrants",
                sql: "(\"ExecutorRunId\" IS NULL AND \"ExecutorRunVersion\" IS NULL) OR (\"ExecutorRunId\" IS NOT NULL AND \"ExecutorRunVersion\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_MiniAppSandboxLaunchGrants_MiniAppSandboxTestRuns_ExecutorR~",
                table: "MiniAppSandboxLaunchGrants",
                columns: new[] { "ExecutorRunId", "SandboxSessionId" },
                principalTable: "MiniAppSandboxTestRuns",
                principalColumns: new[] { "Id", "SandboxSessionId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A downgraded binary must never reinterpret an unused executor grant as an owner grant.
            migrationBuilder.Sql("UPDATE \"MiniAppSandboxLaunchGrants\" SET \"ConsumedAt\" = COALESCE(\"ConsumedAt\", CURRENT_TIMESTAMP) WHERE \"ExecutorRunId\" IS NOT NULL;");
            migrationBuilder.DropForeignKey(
                name: "FK_MiniAppSandboxLaunchGrants_MiniAppSandboxTestRuns_ExecutorR~",
                table: "MiniAppSandboxLaunchGrants");

            migrationBuilder.DropIndex(
                name: "IX_MiniAppSandboxLaunchGrants_ExecutorRunId_SandboxSessionId",
                table: "MiniAppSandboxLaunchGrants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SandboxGrants_ExecutorBinding",
                table: "MiniAppSandboxLaunchGrants");

            migrationBuilder.DropColumn(
                name: "ExecutorRunId",
                table: "MiniAppSandboxLaunchGrants");

            migrationBuilder.DropColumn(
                name: "ExecutorRunVersion",
                table: "MiniAppSandboxLaunchGrants");
        }
    }
}
