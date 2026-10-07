using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveAgoraParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "NextAgoraUid",
                table: "Lives",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "LiveAgoraParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgoraUid = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveAgoraParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveAgoraParticipants_Lives_LiveId",
                        column: x => x.LiveId,
                        principalTable: "Lives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveAgoraParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveAgoraParticipants_LiveId_AgoraUid",
                table: "LiveAgoraParticipants",
                columns: new[] { "LiveId", "AgoraUid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveAgoraParticipants_LiveId_UserId",
                table: "LiveAgoraParticipants",
                columns: new[] { "LiveId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveAgoraParticipants_UserId",
                table: "LiveAgoraParticipants",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveAgoraParticipants");

            migrationBuilder.DropColumn(
                name: "NextAgoraUid",
                table: "Lives");
        }
    }
}
