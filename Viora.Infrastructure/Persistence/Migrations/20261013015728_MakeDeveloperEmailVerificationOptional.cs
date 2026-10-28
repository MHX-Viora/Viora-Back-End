using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeDeveloperEmailVerificationOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "DeveloperApplications",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A rollback must not invent verification timestamps for new applications.
            migrationBuilder.Sql("""
                LOCK TABLE "DeveloperApplications" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "DeveloperApplications" WHERE "EmailVerifiedAt" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot restore required email verification while unverified developer applications exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "DeveloperApplications",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
