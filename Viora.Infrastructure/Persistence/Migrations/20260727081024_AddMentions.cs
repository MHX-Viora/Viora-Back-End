using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Mentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MentionedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MentionedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mentions_Users_MentionedByUserId",
                        column: x => x.MentionedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mentions_Users_MentionedUserId",
                        column: x => x.MentionedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Mentions_MentionedByUserId",
                table: "Mentions",
                column: "MentionedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Mentions_MentionedUserId",
                table: "Mentions",
                column: "MentionedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Mentions_TargetId",
                table: "Mentions",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_Mentions_TargetId_TargetType_MentionedUserId",
                table: "Mentions",
                columns: new[] { "TargetId", "TargetType", "MentionedUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mentions_TargetType",
                table: "Mentions",
                column: "TargetType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Mentions");
        }
    }
}
