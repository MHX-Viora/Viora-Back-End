using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppProductionCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_MiniAppPublications_Id_MiniAppId_MiniAppVersionId_ApprovedR~",
                table: "MiniAppPublications",
                columns: new[] { "Id", "MiniAppId", "MiniAppVersionId", "ApprovedReviewRequestId" });

            migrationBuilder.CreateTable(
                name: "MiniAppCatalogPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedReviewRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    VersionName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WebsiteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    SsoEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ProductionClientKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductionClientType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ProductionScopes = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppCatalogPublications", x => x.Id);
                    table.CheckConstraint("CK_MiniAppCatalogPublications_Description", "\"Description\" IS NULL OR length(\"Description\") <= 300");
                    table.CheckConstraint("CK_MiniAppCatalogPublications_Client", "(\"SsoEnabled\" AND \"ProductionClientKey\" IS NOT NULL AND length(\"ProductionClientKey\") BETWEEN 1 AND 100 AND \"ProductionClientType\" IS NOT NULL AND \"ProductionClientType\" IN ('Public','Confidential') AND \"ProductionScopes\" IS NOT NULL AND length(\"ProductionScopes\") BETWEEN 1 AND 64) OR (NOT \"SsoEnabled\" AND \"ProductionClientKey\" IS NULL AND \"ProductionClientType\" IS NULL AND \"ProductionScopes\" IS NULL)");
                    table.CheckConstraint("CK_MiniAppCatalogPublications_Domain", "length(\"Domain\") BETWEEN 1 AND 253 AND length(\"WebsiteUrl\") BETWEEN 1 AND 2048");
                    table.CheckConstraint("CK_MiniAppCatalogPublications_Hash", "length(\"SnapshotHash\") = 64 AND \"SnapshotHash\" = lower(\"SnapshotHash\")");
                    table.CheckConstraint("CK_MiniAppCatalogPublications_Name", "length(trim(\"Name\")) BETWEEN 1 AND 120");
                    table.CheckConstraint("CK_MiniAppCatalogPublications_Version", "length(trim(\"VersionName\")) BETWEEN 1 AND 80");
                    table.ForeignKey(
                        name: "FK_MiniAppCatalogPublications_MiniAppPublications_PublicationI~",
                        columns: x => new { x.PublicationId, x.MiniAppId, x.MiniAppVersionId, x.ApprovedReviewRequestId },
                        principalTable: "MiniAppPublications",
                        principalColumns: new[] { "Id", "MiniAppId", "MiniAppVersionId", "ApprovedReviewRequestId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppCatalogPublications_PublicationId",
                table: "MiniAppCatalogPublications",
                column: "PublicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppCatalogPublications_PublicationId_MiniAppId_MiniAppV~",
                table: "MiniAppCatalogPublications",
                columns: new[] { "PublicationId", "MiniAppId", "MiniAppVersionId", "ApprovedReviewRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppCatalogPublications_PublishedAt_PublicationId",
                table: "MiniAppCatalogPublications",
                columns: new[] { "PublishedAt", "PublicationId" });

            migrationBuilder.Sql("""
                CREATE FUNCTION ankt_catalog_projection_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    snapshot jsonb;
                    expected_hash text;
                    expected_version text;
                    expected_published timestamptz;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'Catalog projection is immutable';
                    END IF;
                    SELECT v."SnapshotJson"::jsonb, v."SnapshotHash", v."VersionName", p."PublishedAt"
                    INTO snapshot, expected_hash, expected_version, expected_published
                    FROM "MiniAppPublications" p
                    JOIN "MiniAppVersions" v ON v."Id" = p."MiniAppVersionId" AND v."MiniAppId" = p."MiniAppId"
                    JOIN "MiniAppReviewRequests" r ON r."Id" = p."ApprovedReviewRequestId"
                        AND r."MiniAppId" = p."MiniAppId" AND r."MiniAppVersionId" = v."Id"
                    WHERE p."Id" = NEW."PublicationId" AND p."MiniAppId" = NEW."MiniAppId"
                        AND v."Id" = NEW."MiniAppVersionId" AND r."Id" = NEW."ApprovedReviewRequestId"
                        AND p."Environment" = 1 AND p."Status" = 0 AND p."UnpublishedAt" IS NULL
                        AND v."State" <> 0 AND v."SubmittedAt" IS NOT NULL AND r."Status" = 4 AND r."DecidedAt" IS NOT NULL
                    FOR SHARE OF p, v, r;
                    IF NOT FOUND OR NEW."SnapshotHash" IS DISTINCT FROM expected_hash
                        OR NEW."VersionName" IS DISTINCT FROM expected_version
                        OR NEW."PublishedAt" IS DISTINCT FROM expected_published
                        OR NEW."Name" IS DISTINCT FROM (snapshot->>'name')
                        OR NEW."Category" IS DISTINCT FROM (snapshot->>'category')
                        OR NEW."LogoUrl" IS DISTINCT FROM (snapshot->>'logoUrl')
                        OR NEW."Description" IS DISTINCT FROM left(COALESCE(snapshot->>'shortDescription', snapshot->>'description'), 300)
                        OR NEW."WebsiteUrl" IS DISTINCT FROM (snapshot->>'websiteUrl')
                        OR NEW."Domain" IS DISTINCT FROM (snapshot->>'domain')
                        OR NEW."SsoEnabled" IS DISTINCT FROM ((snapshot->>'ssoEnabled')::boolean)
                        OR NEW."ProductionClientKey" IS DISTINCT FROM (CASE WHEN NEW."SsoEnabled" THEN snapshot->'production'->>'clientId' END)
                        OR NEW."ProductionClientType" IS DISTINCT FROM (CASE WHEN NEW."SsoEnabled" THEN snapshot->'production'->>'clientType' END)
                        OR NEW."ProductionScopes" IS DISTINCT FROM (CASE WHEN NEW."SsoEnabled" THEN
                            (SELECT string_agg(value, ' ' ORDER BY ord) FROM jsonb_array_elements_text(snapshot->'production'->'scopes') WITH ORDINALITY AS s(value,ord)) END)
                    THEN
                        RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'Catalog projection must match its approved published snapshot';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER ankt_catalog_projection_guard BEFORE INSERT OR UPDATE OR DELETE
                    ON "MiniAppCatalogPublications" FOR EACH ROW EXECUTE FUNCTION ankt_catalog_projection_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppCatalogPublications");

            migrationBuilder.Sql("DROP FUNCTION ankt_catalog_projection_guard();");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MiniAppPublications_Id_MiniAppId_MiniAppVersionId_ApprovedR~",
                table: "MiniAppPublications");
        }
    }
}
