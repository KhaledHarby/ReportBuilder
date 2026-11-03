using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatasetColumnDefaultSortDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.DatasetColumns', N'DefaultSortDirection') IS NULL
                    ALTER TABLE [rb].[DatasetColumns] ADD [DefaultSortDirection] nvarchar(4) NOT NULL CONSTRAINT [DF_DatasetColumns_DefaultSortDirection] DEFAULT N'desc';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.DatasetColumns', N'DefaultSortDirection') IS NOT NULL
                BEGIN
                    IF OBJECT_ID(N'rb.DF_DatasetColumns_DefaultSortDirection', N'D') IS NOT NULL
                        ALTER TABLE [rb].[DatasetColumns] DROP CONSTRAINT [DF_DatasetColumns_DefaultSortDirection];
                    ALTER TABLE [rb].[DatasetColumns] DROP COLUMN [DefaultSortDirection];
                END
                """);
        }
    }
}
