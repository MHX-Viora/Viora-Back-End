using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppSandboxEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_MiniAppSandboxSessions_Id_ConfigurationFingerprint",
                table: "MiniAppSandboxSessions",
                columns: new[] { "Id", "ConfigurationFingerprint" });

            migrationBuilder.CreateTable(
                name: "MiniAppSandboxTestRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SandboxSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<short>(type: "smallint", nullable: false),
                    ConfigurationFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    SummaryCode = table.Column<short>(type: "smallint", nullable: false),
                    DeadlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppSandboxTestRuns", x => x.Id);
                    table.UniqueConstraint("AK_MiniAppSandboxTestRuns_Id_SandboxSessionId", x => new { x.Id, x.SandboxSessionId });
                    table.CheckConstraint("CK_SandboxRuns_Completion", "(\"Status\" IN (0,1) AND \"CompletedAt\" IS NULL) OR (\"Status\" IN (2,3,4) AND \"CompletedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_SandboxRuns_Environment", "\"Environment\" = 0");
                    table.CheckConstraint("CK_SandboxRuns_Fingerprint", "char_length(\"ConfigurationFingerprint\") = 64");
                    table.CheckConstraint("CK_SandboxRuns_Started", "\"Status\" <> 1 OR \"StartedAt\" IS NOT NULL");
                    table.CheckConstraint("CK_SandboxRuns_Status", "\"Status\" IN (0,1,2,3,4)");
                    table.CheckConstraint("CK_SandboxRuns_Summary", "\"SummaryCode\" IN (0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23)");
                    table.CheckConstraint("CK_SandboxRuns_Times", "\"DeadlineAt\" > \"CreatedAt\" AND (\"StartedAt\" IS NULL OR \"StartedAt\" >= \"CreatedAt\") AND (\"CompletedAt\" IS NULL OR \"CompletedAt\" >= \"CreatedAt\") AND (\"StartedAt\" IS NULL OR \"CompletedAt\" IS NULL OR \"CompletedAt\" >= \"StartedAt\")");
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxTestRuns_MiniAppSandboxSessions_SandboxSessio~",
                        columns: x => new { x.SandboxSessionId, x.ConfigurationFingerprint },
                        principalTable: "MiniAppSandboxSessions",
                        principalColumns: new[] { "Id", "ConfigurationFingerprint" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppSandboxDiagnostics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SandboxSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<short>(type: "smallint", nullable: false),
                    Category = table.Column<short>(type: "smallint", nullable: false),
                    Code = table.Column<short>(type: "smallint", nullable: false),
                    EvidenceSource = table.Column<short>(type: "smallint", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppSandboxDiagnostics", x => x.Id);
                    table.CheckConstraint("CK_SandboxDiagnostics_Category", "\"Category\" IN (0,1,2,3,4,5)");
                    table.CheckConstraint("CK_SandboxDiagnostics_Code", "\"Code\" IN (0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23)");
                    table.CheckConstraint("CK_SandboxDiagnostics_Count", "\"Count\" IS NULL OR \"Count\" BETWEEN 0 AND 1000000");
                    table.CheckConstraint("CK_SandboxDiagnostics_Level", "\"Level\" IN (0,1,2)");
                    table.CheckConstraint("CK_SandboxDiagnostics_Source", "\"EvidenceSource\" IN (0,1,2,3,4)");
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxDiagnostics_MiniAppSandboxSessions_SandboxSes~",
                        column: x => x.SandboxSessionId,
                        principalTable: "MiniAppSandboxSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxDiagnostics_MiniAppSandboxTestRuns_TestRunId_~",
                        columns: x => new { x.TestRunId, x.SandboxSessionId },
                        principalTable: "MiniAppSandboxTestRuns",
                        principalColumns: new[] { "Id", "SandboxSessionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppSandboxTestResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestKey = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    MessageCode = table.Column<short>(type: "smallint", nullable: false),
                    EvidenceSource = table.Column<short>(type: "smallint", nullable: false),
                    EvidenceAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppSandboxTestResults", x => x.Id);
                    table.UniqueConstraint("AK_MiniAppSandboxTestResults_TestRunId_TestKey", x => new { x.TestRunId, x.TestKey });
                    table.CheckConstraint("CK_SandboxResults_Duration", "\"DurationMs\" BETWEEN 0 AND 1800000");
                    table.CheckConstraint("CK_SandboxResults_Key", "\"TestKey\" IN (0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17)");
                    table.CheckConstraint("CK_SandboxResults_Message", "\"MessageCode\" IN (0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23)");
                    table.CheckConstraint("CK_SandboxResults_PassedEvidence", "\"Status\" <> 0 OR (\"EvidenceAt\" IS NOT NULL AND \"EvidenceSource\" IN (0,3,4))");
                    table.CheckConstraint("CK_SandboxResults_ProtocolProof", "\"Status\" <> 0 OR \"TestKey\" NOT IN (15,16,17) OR \"EvidenceSource\" = 3");
                    table.CheckConstraint("CK_SandboxResults_RuntimeProof", "\"Status\" <> 0 OR \"TestKey\" NOT IN (13,14) OR \"EvidenceSource\" = 4");
                    table.CheckConstraint("CK_SandboxResults_Source", "\"EvidenceSource\" IN (0,1,2,3,4)");
                    table.CheckConstraint("CK_SandboxResults_Status", "\"Status\" IN (0,1,2,3,4)");
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxTestResults_MiniAppSandboxTestRuns_TestRunId",
                        column: x => x.TestRunId,
                        principalTable: "MiniAppSandboxTestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppSandboxProtocolEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestKey = table.Column<short>(type: "smallint", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Stage = table.Column<short>(type: "smallint", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    AuthorizationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TokenId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppSandboxProtocolEvidence", x => x.Id);
                    table.CheckConstraint("CK_SandboxProtocol_Binding", "(\"Stage\" = 0 AND \"TestKey\" = 15) OR (\"Stage\" = 1 AND \"TestKey\" = 16) OR (\"Stage\" = 2 AND \"TestKey\" = 17)");
                    table.CheckConstraint("CK_SandboxProtocol_EventKey", "char_length(\"EventKey\") = 64");
                    table.CheckConstraint("CK_SandboxProtocol_Key", "\"TestKey\" IN (15,16,17)");
                    table.CheckConstraint("CK_SandboxProtocol_Outcome", "\"Outcome\" IN (0,1,2,3,4)");
                    table.CheckConstraint("CK_SandboxProtocol_Stage", "\"Stage\" IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_MiniAppSandboxProtocolEvidence_MiniAppSandboxTestResults_Te~",
                        columns: x => new { x.TestRunId, x.TestKey },
                        principalTable: "MiniAppSandboxTestResults",
                        principalColumns: new[] { "TestRunId", "TestKey" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxDiagnostics_CreatedAt",
                table: "MiniAppSandboxDiagnostics",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxDiagnostics_SandboxSessionId_CreatedAt_Id",
                table: "MiniAppSandboxDiagnostics",
                columns: new[] { "SandboxSessionId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxDiagnostics_TestRunId_SandboxSessionId",
                table: "MiniAppSandboxDiagnostics",
                columns: new[] { "TestRunId", "SandboxSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxProtocolEvidence_CreatedAt",
                table: "MiniAppSandboxProtocolEvidence",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxProtocolEvidence_EventKey",
                table: "MiniAppSandboxProtocolEvidence",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxProtocolEvidence_TestRunId_TestKey",
                table: "MiniAppSandboxProtocolEvidence",
                columns: new[] { "TestRunId", "TestKey" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxTestRuns_CompletedAt",
                table: "MiniAppSandboxTestRuns",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxTestRuns_SandboxSessionId",
                table: "MiniAppSandboxTestRuns",
                column: "SandboxSessionId",
                unique: true,
                filter: "\"Status\" IN (0,1)");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxTestRuns_SandboxSessionId_ConfigurationFinger~",
                table: "MiniAppSandboxTestRuns",
                columns: new[] { "SandboxSessionId", "ConfigurationFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxTestRuns_SandboxSessionId_CreatedAt",
                table: "MiniAppSandboxTestRuns",
                columns: new[] { "SandboxSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppSandboxTestRuns_Status_DeadlineAt",
                table: "MiniAppSandboxTestRuns",
                columns: new[] { "Status", "DeadlineAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniAppSandboxDiagnostics");

            migrationBuilder.DropTable(
                name: "MiniAppSandboxProtocolEvidence");

            migrationBuilder.DropTable(
                name: "MiniAppSandboxTestResults");

            migrationBuilder.DropTable(
                name: "MiniAppSandboxTestRuns");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MiniAppSandboxSessions_Id_ConfigurationFingerprint",
                table: "MiniAppSandboxSessions");
        }
    }
}
