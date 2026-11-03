using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "rb",
                table: "ReportTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Published");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                schema: "rb",
                table: "ReportTemplates");
        }
    }
}
