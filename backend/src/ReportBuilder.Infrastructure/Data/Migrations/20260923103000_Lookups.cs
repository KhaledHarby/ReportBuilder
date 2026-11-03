using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ReportBuilder.Infrastructure.Data;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations;

[DbContext(typeof(ReportBuilderDbContext))]
[Migration("20260923103000_Lookups")]
public class Lookups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'rb.Lookups', N'U') IS NULL
            BEGIN
                CREATE TABLE [rb].[Lookups] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Lookups] PRIMARY KEY,
                    [Name] nvarchar(200) NOT NULL,
                    [NameArabic] nvarchar(200) NULL,
                    [SourceKind] nvarchar(16) NOT NULL,
                    [SchemaName] nvarchar(128) NOT NULL CONSTRAINT [DF_Lookups_SchemaName] DEFAULT N'',
                    [ObjectName] nvarchar(128) NOT NULL CONSTRAINT [DF_Lookups_ObjectName] DEFAULT N'',
                    [SqlText] nvarchar(max) NULL,
                    [KeyColumn] nvarchar(128) NOT NULL CONSTRAINT [DF_Lookups_KeyColumn] DEFAULT N'',
                    [LabelColumn] nvarchar(128) NOT NULL CONSTRAINT [DF_Lookups_LabelColumn] DEFAULT N'',
                    [SortColumn] nvarchar(128) NULL,
                    [SortDirection] nvarchar(4) NOT NULL CONSTRAINT [DF_Lookups_SortDirection] DEFAULT N'asc',
                    [IsValid] bit NOT NULL CONSTRAINT [DF_Lookups_IsValid] DEFAULT 0,
                    [ValidationError] nvarchar(500) NULL,
                    [ColumnsJson] nvarchar(max) NOT NULL CONSTRAINT [DF_Lookups_ColumnsJson] DEFAULT N'[]',
                    [CreatedAt] datetimeoffset NOT NULL,
                    [UpdatedAt] datetimeoffset NOT NULL
                );
                CREATE INDEX [IX_Lookups_Name] ON [rb].[Lookups] ([Name]);
            END

            IF COL_LENGTH(N'rb.DatasetColumns', N'LookupId') IS NULL
                ALTER TABLE [rb].[DatasetColumns] ADD [LookupId] uniqueidentifier NULL;

            IF NOT EXISTS (
                SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DatasetColumns_Lookups_LookupId')
            AND COL_LENGTH(N'rb.DatasetColumns', N'LookupId') IS NOT NULL
                ALTER TABLE [rb].[DatasetColumns] ADD CONSTRAINT [FK_DatasetColumns_Lookups_LookupId]
                    FOREIGN KEY ([LookupId]) REFERENCES [rb].[Lookups] ([Id]) ON DELETE SET NULL;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_DatasetColumns_LookupId' AND object_id = OBJECT_ID(N'rb.DatasetColumns'))
            AND COL_LENGTH(N'rb.DatasetColumns', N'LookupId') IS NOT NULL
                CREATE INDEX [IX_DatasetColumns_LookupId] ON [rb].[DatasetColumns] ([LookupId]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DatasetColumns_Lookups_LookupId')
                ALTER TABLE [rb].[DatasetColumns] DROP CONSTRAINT [FK_DatasetColumns_Lookups_LookupId];
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_DatasetColumns_LookupId' AND object_id = OBJECT_ID(N'rb.DatasetColumns'))
                DROP INDEX [IX_DatasetColumns_LookupId] ON [rb].[DatasetColumns];
            IF COL_LENGTH(N'rb.DatasetColumns', N'LookupId') IS NOT NULL
                ALTER TABLE [rb].[DatasetColumns] DROP COLUMN [LookupId];
            IF OBJECT_ID(N'rb.Lookups', N'U') IS NOT NULL
                DROP TABLE [rb].[Lookups];
            """);
    }
}
