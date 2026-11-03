using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportRoleAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessMode",
                schema: "rb",
                table: "ReportTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Everyone");

            migrationBuilder.AddColumn<string>(
                name: "AccessMode",
                schema: "rb",
                table: "ReportRevisions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Everyone");

            migrationBuilder.AddColumn<string>(
                name: "AccessRoleJson",
                schema: "rb",
                table: "ReportRevisions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "ReportRoleAccess",
                schema: "rb",
                columns: table => new
                {
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRoleAccess", x => new { x.ReportId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_ReportRoleAccess_ReportTemplates_ReportId",
                        column: x => x.ReportId,
                        principalSchema: "rb",
                        principalTable: "ReportTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportRoleAccess_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "rb",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRoleAccess_RoleId",
                schema: "rb",
                table: "ReportRoleAccess",
                column: "RoleId");

            migrationBuilder.Sql("""
                UPDATE [rb].[ReportTemplates]
                SET [AccessMode] = N'People'
                WHERE [IsRestricted] = 1;

                UPDATE [rb].[ReportRevisions]
                SET [AccessMode] = CASE WHEN [IsRestricted] = 1 THEN N'People' ELSE N'Everyone' END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportRoleAccess",
                schema: "rb");

            migrationBuilder.DropColumn(
                name: "AccessMode",
                schema: "rb",
                table: "ReportTemplates");

            migrationBuilder.DropColumn(
                name: "AccessMode",
                schema: "rb",
                table: "ReportRevisions");

            migrationBuilder.DropColumn(
                name: "AccessRoleJson",
                schema: "rb",
                table: "ReportRevisions");
        }
    }
}
