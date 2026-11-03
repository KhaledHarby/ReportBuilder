using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsRoleAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessMode",
                schema: "rb",
                table: "AnalyticsDashboards",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Everyone");

            migrationBuilder.CreateTable(
                name: "AnalyticsRoleAccess",
                schema: "rb",
                columns: table => new
                {
                    DashboardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsRoleAccess", x => new { x.DashboardId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AnalyticsRoleAccess_AnalyticsDashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalSchema: "rb",
                        principalTable: "AnalyticsDashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnalyticsRoleAccess_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "rb",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsRoleAccess_RoleId",
                schema: "rb",
                table: "AnalyticsRoleAccess",
                column: "RoleId");

            migrationBuilder.Sql("""
                UPDATE [rb].[AnalyticsDashboards]
                SET [AccessMode] = N'People'
                WHERE [IsRestricted] = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsRoleAccess",
                schema: "rb");

            migrationBuilder.DropColumn(
                name: "AccessMode",
                schema: "rb",
                table: "AnalyticsDashboards");
        }
    }
}
