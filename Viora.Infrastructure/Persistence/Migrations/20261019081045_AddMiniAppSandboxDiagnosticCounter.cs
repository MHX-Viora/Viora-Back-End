using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppSandboxDiagnosticCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DroppedDiagnosticCount",
                table: "MiniAppSandboxSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MiniAppSandboxSessions_DroppedDiagnostics",
                table: "MiniAppSandboxSessions",
                sql: "\"DroppedDiagnosticCount\" BETWEEN 0 AND 1000000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MiniAppSandboxSessions_DroppedDiagnostics",
                table: "MiniAppSandboxSessions");

            migrationBuilder.DropColumn(
                name: "DroppedDiagnosticCount",
                table: "MiniAppSandboxSessions");
        }
    }
}
