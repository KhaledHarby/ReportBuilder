using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "rb",
                table: "ReportTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdminCreated",
                schema: "rb",
                table: "ReportTemplates",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRestricted",
                schema: "rb",
                table: "ReportTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ReportAccess",
                schema: "rb",
                columns: table => new
                {
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportAccess", x => new { x.ReportId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ReportAccess_ReportTemplates_ReportId",
                        column: x => x.ReportId,
                        principalSchema: "rb",
                        principalTable: "ReportTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportAccess_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "rb",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportAccess_UserId",
                schema: "rb",
                table: "ReportAccess",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportAccess",
                schema: "rb");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "rb",
                table: "ReportTemplates");

            migrationBuilder.DropColumn(
                name: "IsAdminCreated",
                schema: "rb",
                table: "ReportTemplates");

            migrationBuilder.DropColumn(
                name: "IsRestricted",
                schema: "rb",
                table: "ReportTemplates");
        }
    }
}
