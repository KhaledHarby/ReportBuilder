using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations;

public partial class ReportDraftVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'rb.ReportTemplates', N'DraftJson') IS NULL
                ALTER TABLE [rb].[ReportTemplates] ADD [DraftJson] nvarchar(max) NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'rb.ReportTemplates', N'PublishedVersion') IS NULL
                ALTER TABLE [rb].[ReportTemplates] ADD [PublishedVersion] int NOT NULL
                    CONSTRAINT [DF_ReportTemplates_PublishedVersion] DEFAULT 0;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'rb.ReportTemplates', N'PublishedVersion') IS NOT NULL
                UPDATE [rb].[ReportTemplates]
                SET [PublishedVersion] = 1
                WHERE [Status] = N'Published' AND [PublishedVersion] = 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'rb.ReportTemplates', N'DraftJson') IS NOT NULL
                ALTER TABLE [rb].[ReportTemplates] DROP COLUMN [DraftJson];

            IF OBJECT_ID(N'rb.DF_ReportTemplates_PublishedVersion', N'D') IS NOT NULL
                ALTER TABLE [rb].[ReportTemplates] DROP CONSTRAINT [DF_ReportTemplates_PublishedVersion];

            IF COL_LENGTH(N'rb.ReportTemplates', N'PublishedVersion') IS NOT NULL
                ALTER TABLE [rb].[ReportTemplates] DROP COLUMN [PublishedVersion];
            """);
    }
}
