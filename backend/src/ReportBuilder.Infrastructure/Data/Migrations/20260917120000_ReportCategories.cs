using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations;

public partial class ReportCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'rb.ReportCategories', N'U') IS NULL
            BEGIN
                CREATE TABLE [rb].[ReportCategories] (
                    [Id] uniqueidentifier NOT NULL,
                    [Name] nvarchar(120) NOT NULL,
                    [NameArabic] nvarchar(120) NULL,
                    [SortOrder] int NOT NULL CONSTRAINT [DF_ReportCategories_SortOrder] DEFAULT 0,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [UpdatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_ReportCategories] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_ReportCategories_Name] ON [rb].[ReportCategories] ([Name]);
                CREATE INDEX [IX_ReportCategories_SortOrder] ON [rb].[ReportCategories] ([SortOrder]);
            END

            IF COL_LENGTH(N'rb.ReportTemplates', N'CategoryId') IS NULL
                ALTER TABLE [rb].[ReportTemplates] ADD [CategoryId] uniqueidentifier NULL;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ReportTemplates_CategoryId'
                  AND [object_id] = OBJECT_ID(N'rb.ReportTemplates'))
                CREATE INDEX [IX_ReportTemplates_CategoryId] ON [rb].[ReportTemplates] ([CategoryId]);

            IF OBJECT_ID(N'rb.FK_ReportTemplates_ReportCategories_CategoryId', N'F') IS NULL
               AND OBJECT_ID(N'rb.ReportCategories', N'U') IS NOT NULL
               AND COL_LENGTH(N'rb.ReportTemplates', N'CategoryId') IS NOT NULL
                ALTER TABLE [rb].[ReportTemplates] WITH CHECK
                ADD CONSTRAINT [FK_ReportTemplates_ReportCategories_CategoryId]
                    FOREIGN KEY ([CategoryId]) REFERENCES [rb].[ReportCategories] ([Id]) ON DELETE SET NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'rb.FK_ReportTemplates_ReportCategories_CategoryId', N'F') IS NOT NULL
                ALTER TABLE [rb].[ReportTemplates] DROP CONSTRAINT [FK_ReportTemplates_ReportCategories_CategoryId];

            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_ReportTemplates_CategoryId'
                  AND [object_id] = OBJECT_ID(N'rb.ReportTemplates'))
                DROP INDEX [IX_ReportTemplates_CategoryId] ON [rb].[ReportTemplates];

            IF COL_LENGTH(N'rb.ReportTemplates', N'CategoryId') IS NOT NULL
                ALTER TABLE [rb].[ReportTemplates] DROP COLUMN [CategoryId];

            IF OBJECT_ID(N'rb.ReportCategories', N'U') IS NOT NULL
                DROP TABLE [rb].[ReportCategories];
            """);
    }
}
