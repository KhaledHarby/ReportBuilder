using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class DatasetColumnIsIndexed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'rb.DatasetColumns', N'IsIndexed') IS NULL
                ALTER TABLE [rb].[DatasetColumns] ADD [IsIndexed] bit NOT NULL CONSTRAINT [DF_DatasetColumns_IsIndexed] DEFAULT 0;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'rb.DatasetColumns', N'IsIndexed') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'rb.DF_DatasetColumns_IsIndexed', N'D') IS NOT NULL
                    ALTER TABLE [rb].[DatasetColumns] DROP CONSTRAINT [DF_DatasetColumns_IsIndexed];
                ALTER TABLE [rb].[DatasetColumns] DROP COLUMN [IsIndexed];
            END
            """);
    }
}
