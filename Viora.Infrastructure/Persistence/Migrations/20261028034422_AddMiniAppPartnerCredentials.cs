using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppPartnerCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniAppPartnerCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<short>(type: "smallint", nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretEncrypted = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SecretKeyVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppPartnerCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppPartnerCredentials_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPartnerCredentials_MiniAppId_Environment",
                table: "MiniAppPartnerCredentials",
                columns: new[] { "MiniAppId", "Environment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppPartnerCredentials");
        }
    }
}
