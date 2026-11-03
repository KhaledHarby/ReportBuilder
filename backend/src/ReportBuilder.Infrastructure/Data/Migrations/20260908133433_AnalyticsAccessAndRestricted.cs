using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsAccessAndRestricted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRestricted",
                schema: "rb",
                table: "AnalyticsDashboards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AnalyticsAccess",
                schema: "rb",
                columns: table => new
                {
                    DashboardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsAccess", x => new { x.DashboardId, x.UserId });
                    table.ForeignKey(
                        name: "FK_AnalyticsAccess_AnalyticsDashboards_DashboardId",
                        column: x => x.DashboardId,
                        principalSchema: "rb",
                        principalTable: "AnalyticsDashboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnalyticsAccess_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "rb",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsAccess_UserId",
                schema: "rb",
                table: "AnalyticsAccess",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsAccess",
                schema: "rb");

            migrationBuilder.DropColumn(
                name: "IsRestricted",
                schema: "rb",
                table: "AnalyticsDashboards");
        }
    }
}
