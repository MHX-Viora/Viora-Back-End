using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppProductionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_MiniAppProductionHostSessions_Id_MiniAppId",
                table: "MiniAppProductionHostSessions",
                columns: new[] { "Id", "MiniAppId" });

            migrationBuilder.CreateTable(
                name: "MiniAppProductionRuntimeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<short>(type: "smallint", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppProductionRuntimeEvents", x => x.Id);
                    table.CheckConstraint("CK_ProductionEvent_Code", "\"Code\" BETWEEN 0 AND 7");
                    table.ForeignKey(
                        name: "FK_MiniAppProductionRuntimeEvents_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppProductionRuntimeEvents_MiniAppProductionHostSession~",
                        columns: x => new { x.HostSessionId, x.MiniAppId },
                        principalTable: "MiniAppProductionHostSessions",
                        principalColumns: new[] { "Id", "MiniAppId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppProductionRuntimeEvents_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionRuntimeEvents_AccountId",
                table: "MiniAppProductionRuntimeEvents",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionRuntimeEvents_CreatedAt",
                table: "MiniAppProductionRuntimeEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionRuntimeEvents_HostSessionId_Code",
                table: "MiniAppProductionRuntimeEvents",
                columns: new[] { "HostSessionId", "Code" },
                unique: true,
                filter: "\"HostSessionId\" IS NOT NULL AND \"Code\" IN (0,1,3,4)");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionRuntimeEvents_HostSessionId_CreatedAt",
                table: "MiniAppProductionRuntimeEvents",
                columns: new[] { "HostSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionRuntimeEvents_HostSessionId_MiniAppId",
                table: "MiniAppProductionRuntimeEvents",
                columns: new[] { "HostSessionId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppProductionRuntimeEvents_MiniAppId_CreatedAt",
                table: "MiniAppProductionRuntimeEvents",
                columns: new[] { "MiniAppId", "CreatedAt" });
            migrationBuilder.Sql("""
                CREATE FUNCTION ankt_production_event_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION 'immutable production event' USING ERRCODE = '23514';
                    ELSIF TG_OP = 'DELETE' THEN
                        IF OLD."CreatedAt" > clock_timestamp() - interval '1 day' THEN
                            RAISE EXCEPTION 'production event retention not elapsed' USING ERRCODE = '23514';
                        END IF;
                        RETURN OLD;
                    ELSIF NEW."HostSessionId" IS NOT NULL THEN
                        PERFORM 1 FROM "MiniAppProductionHostSessions" s WHERE s."Id" = NEW."HostSessionId"
                            AND s."MiniAppId" = NEW."MiniAppId" AND s."AccountId" IS NOT DISTINCT FROM NEW."AccountId"
                            AND s."CorrelationId" = NEW."CorrelationId" FOR SHARE OF s;
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'invalid production event provenance' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER ankt_production_event_guard BEFORE INSERT OR UPDATE OR DELETE ON "MiniAppProductionRuntimeEvents"
                    FOR EACH ROW EXECUTE FUNCTION ankt_production_event_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppProductionRuntimeEvents");
            migrationBuilder.Sql("DROP FUNCTION ankt_production_event_guard();");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MiniAppProductionHostSessions_Id_MiniAppId",
                table: "MiniAppProductionHostSessions");
        }
    }
}
