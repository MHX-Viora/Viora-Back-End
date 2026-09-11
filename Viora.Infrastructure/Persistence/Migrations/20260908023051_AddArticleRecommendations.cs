using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArticleInteractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    InteractionType = table.Column<short>(type: "smallint", nullable: false),
                    ReadDuration = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ReadPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleInteractions", x => x.Id);
                    table.CheckConstraint("CK_ArticleInteractions_ReadDuration", "\"ReadDuration\" >= 0 AND \"ReadDuration\" <= 86400");
                    table.CheckConstraint("CK_ArticleInteractions_ReadPercentage", "\"ReadPercentage\" >= 0 AND \"ReadPercentage\" <= 100");
                    table.ForeignKey(
                        name: "FK_ArticleInteractions_Posts_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArticleInteractions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleInteractions_ArticleId_InteractionType_CreatedAt",
                table: "ArticleInteractions",
                columns: new[] { "ArticleId", "InteractionType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleInteractions_UserId_ArticleId_InteractionType",
                table: "ArticleInteractions",
                columns: new[] { "UserId", "ArticleId", "InteractionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticleInteractions_UserId_CreatedAt",
                table: "ArticleInteractions",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleInteractions");
        }
    }
}
