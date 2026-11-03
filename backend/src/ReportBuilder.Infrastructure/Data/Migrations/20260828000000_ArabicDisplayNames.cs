using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArabicDisplayNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.Datasets', N'DisplayNameArabic') IS NULL
                    ALTER TABLE [rb].[Datasets] ADD [DisplayNameArabic] nvarchar(256) NULL;
                IF COL_LENGTH(N'rb.DatasetColumns', N'DisplayNameArabic') IS NULL
                    ALTER TABLE [rb].[DatasetColumns] ADD [DisplayNameArabic] nvarchar(256) NULL;
                IF COL_LENGTH(N'rb.ReportTemplates', N'NameArabic') IS NULL
                    ALTER TABLE [rb].[ReportTemplates] ADD [NameArabic] nvarchar(200) NULL;
                IF COL_LENGTH(N'rb.ReportTemplates', N'DescriptionArabic') IS NULL
                    ALTER TABLE [rb].[ReportTemplates] ADD [DescriptionArabic] nvarchar(1000) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayNameArabic",
                schema: "rb",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "DisplayNameArabic",
                schema: "rb",
                table: "DatasetColumns");

            migrationBuilder.DropColumn(
                name: "NameArabic",
                schema: "rb",
                table: "ReportTemplates");

            migrationBuilder.DropColumn(
                name: "DescriptionArabic",
                schema: "rb",
                table: "ReportTemplates");
        }
    }
}
