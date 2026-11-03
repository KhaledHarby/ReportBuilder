using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatasetSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.Datasets', N'SortOrder') IS NULL
                    ALTER TABLE [rb].[Datasets] ADD [SortOrder] int NOT NULL CONSTRAINT [DF_Datasets_SortOrder] DEFAULT 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.Datasets', N'SortOrder') IS NOT NULL
                BEGIN
                    IF OBJECT_ID(N'rb.DF_Datasets_SortOrder', N'D') IS NOT NULL
                        ALTER TABLE [rb].[Datasets] DROP CONSTRAINT [DF_Datasets_SortOrder];
                    ALTER TABLE [rb].[Datasets] DROP COLUMN [SortOrder];
                END
                """);
        }
    }
}
