using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationInviteCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_InviteCode",
                table: "Conversations");

            migrationBuilder.Sql(
                "UPDATE \"Conversations\" SET \"InviteCode\" = upper(substr(replace(\"Id\"::text, '-', ''), 1, 20)) WHERE \"InviteCode\" IS NULL OR btrim(\"InviteCode\") = '';");

            migrationBuilder.AlterColumn<string>(
                name: "InviteCode",
                table: "Conversations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_InviteCode",
                table: "Conversations",
                column: "InviteCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_InviteCode",
                table: "Conversations");

            migrationBuilder.AlterColumn<string>(
                name: "InviteCode",
                table: "Conversations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_InviteCode",
                table: "Conversations",
                column: "InviteCode",
                unique: true,
                filter: "\"InviteCode\" IS NOT NULL");
        }
    }
}
