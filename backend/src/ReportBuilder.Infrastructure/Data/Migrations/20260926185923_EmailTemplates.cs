using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmailTemplateId",
                schema: "rb",
                table: "ReportSchedules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                schema: "rb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    IsHtml = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSchedules_EmailTemplateId",
                schema: "rb",
                table: "ReportSchedules",
                column: "EmailTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_IsDefault",
                schema: "rb",
                table: "EmailTemplates",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_ReportSchedules_EmailTemplates_EmailTemplateId",
                schema: "rb",
                table: "ReportSchedules",
                column: "EmailTemplateId",
                principalSchema: "rb",
                principalTable: "EmailTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReportSchedules_EmailTemplates_EmailTemplateId",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropTable(
                name: "EmailTemplates",
                schema: "rb");

            migrationBuilder.DropIndex(
                name: "IX_ReportSchedules_EmailTemplateId",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "EmailTemplateId",
                schema: "rb",
                table: "ReportSchedules");
        }
    }
}
