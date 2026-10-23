using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniAppVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionCode = table.Column<long>(type: "bigint", nullable: false),
                    VersionName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    SnapshotSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByDeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogoUploadId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppVersions", x => x.Id);
                    table.UniqueConstraint("AK_MiniAppVersions_Id_MiniAppId", x => new { x.Id, x.MiniAppId });
                    table.CheckConstraint("CK_MiniAppVersions_Code", "\"VersionCode\" > 0");
                    table.CheckConstraint("CK_MiniAppVersions_Hash", "length(\"SnapshotHash\") = 64");
                    table.CheckConstraint("CK_MiniAppVersions_Schema", "\"SnapshotSchemaVersion\" = 1");
                    table.CheckConstraint("CK_MiniAppVersions_Snapshot", "length(\"SnapshotJson\") BETWEEN 2 AND 65536");
                    table.CheckConstraint("CK_MiniAppVersions_State", "\"State\" IN (0,1,2)");
                    table.CheckConstraint("CK_MiniAppVersions_Submitted", "(\"State\" = 0 AND \"SubmittedAt\" IS NULL) OR (\"State\" IN (1,2) AND \"SubmittedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_MiniAppVersions_DeveloperAccounts_CreatedByDeveloperId",
                        column: x => x.CreatedByDeveloperId,
                        principalTable: "DeveloperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppVersions_MiniAppLogoUploads_LogoUploadId",
                        column: x => x.LogoUploadId,
                        principalTable: "MiniAppLogoUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppVersions_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppVersions_CreatedByDeveloperId",
                table: "MiniAppVersions",
                column: "CreatedByDeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppVersions_LogoUploadId",
                table: "MiniAppVersions",
                column: "LogoUploadId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppVersions_MiniAppId",
                table: "MiniAppVersions",
                column: "MiniAppId",
                unique: true,
                filter: "\"State\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppVersions_MiniAppId_CreatedAt",
                table: "MiniAppVersions",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppVersions_MiniAppId_VersionCode",
                table: "MiniAppVersions",
                columns: new[] { "MiniAppId", "VersionCode" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION ankt_guard_mini_app_version() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD."State" <> 0 THEN
                            RAISE EXCEPTION 'Submitted version history is immutable' USING ERRCODE = '23514';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF octet_length(NEW."SnapshotJson") > 65536
                        OR jsonb_typeof(NEW."SnapshotJson"::jsonb) <> 'object'
                        OR NEW."SnapshotHash" !~ '^[0-9a-f]+$' THEN
                        RAISE EXCEPTION 'Invalid version snapshot envelope' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF ROW(NEW."Id", NEW."MiniAppId", NEW."VersionCode", NEW."CreatedByDeveloperId", NEW."CreatedAt")
                            IS DISTINCT FROM ROW(OLD."Id", OLD."MiniAppId", OLD."VersionCode", OLD."CreatedByDeveloperId", OLD."CreatedAt") THEN
                            RAISE EXCEPTION 'Version identity is immutable' USING ERRCODE = '23514';
                        END IF;
                        IF OLD."State" <> 0 AND (
                            ROW(NEW."VersionName", NEW."SnapshotSchemaVersion", NEW."SnapshotJson", NEW."SnapshotHash", NEW."LogoUploadId", NEW."SubmittedAt")
                            IS DISTINCT FROM ROW(OLD."VersionName", OLD."SnapshotSchemaVersion", OLD."SnapshotJson", OLD."SnapshotHash", OLD."LogoUploadId", OLD."SubmittedAt")
                            OR (NEW."State" <> OLD."State" AND NOT (OLD."State" = 1 AND NEW."State" = 2))) THEN
                            RAISE EXCEPTION 'Submitted version configuration is immutable' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_MiniAppVersions_Immutable"
                    BEFORE INSERT OR UPDATE OR DELETE ON "MiniAppVersions"
                    FOR EACH ROW EXECUTE FUNCTION ankt_guard_mini_app_version();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppVersions");
            migrationBuilder.Sql("DROP FUNCTION ankt_guard_mini_app_version();");
        }
    }
}
