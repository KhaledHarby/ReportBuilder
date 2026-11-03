using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ReportBuilder.Infrastructure.Data;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ReportBuilderDbContext))]
    [Migration("20261005021000_ReportScheduleRecipientScope")]
    /// <inheritdoc />
    public class ReportScheduleRecipientScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScopeField",
                schema: "rb",
                table: "ReportSchedules",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeProperty",
                schema: "rb",
                table: "ReportSchedules",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScopeField",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "ScopeProperty",
                schema: "rb",
                table: "ReportSchedules");
        }
    }
}
