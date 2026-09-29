using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveHostHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HostLastSeenAt",
                table: "Lives",
                type: "timestamp with time zone",
                nullable: true);

            // Give sessions already active during deployment one full grace period.
            migrationBuilder.Sql("UPDATE \"Lives\" SET \"HostLastSeenAt\" = NOW() WHERE \"Status\" IN (1, 2, 3);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HostLastSeenAt",
                table: "Lives");
        }
    }
}
