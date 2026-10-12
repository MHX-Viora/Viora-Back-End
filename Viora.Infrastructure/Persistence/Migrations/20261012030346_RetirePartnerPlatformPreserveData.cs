using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations;

/// <summary>Retire runtime mappings while preserving all existing tables and data.</summary>
public partial class RetirePartnerPlatformPreserveData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Metadata only: retired tables remain in the existing database.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Schema was not changed; there is nothing to recreate or drop.
    }
}
