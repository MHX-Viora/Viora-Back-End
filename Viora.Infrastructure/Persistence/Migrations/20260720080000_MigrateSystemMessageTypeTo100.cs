using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260720080000_MigrateSystemMessageTypeTo100")]
public sealed class MigrateSystemMessageTypeTo100 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"Messages\" SET \"MessageType\" = 100 WHERE \"MessageType\" = 8;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"Messages\" SET \"MessageType\" = 8 WHERE \"MessageType\" = 100;");
    }
}
