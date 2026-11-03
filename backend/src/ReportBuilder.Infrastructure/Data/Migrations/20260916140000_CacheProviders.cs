using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations;

public partial class CacheProviders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'rb.DatasetCacheStates', N'CacheProvider') IS NULL
                ALTER TABLE [rb].[DatasetCacheStates] ADD [CacheProvider] nvarchar(32) NULL;

            IF OBJECT_ID(N'rb.CacheSettings', N'U') IS NULL
                CREATE TABLE [rb].[CacheSettings] (
                    [Id] int NOT NULL,
                    [DefaultProvider] nvarchar(32) NOT NULL CONSTRAINT [DF_CacheSettings_DefaultProvider] DEFAULT N'SqlServer',
                    [ProvidersJson] nvarchar(max) NOT NULL CONSTRAINT [DF_CacheSettings_ProvidersJson] DEFAULT N'{}',
                    [ModifiedBy] uniqueidentifier NULL,
                    [ModifiedAt] datetimeoffset NOT NULL CONSTRAINT [DF_CacheSettings_ModifiedAt] DEFAULT SYSDATETIMEOFFSET(),
                    CONSTRAINT [PK_CacheSettings] PRIMARY KEY ([Id])
                );

            IF NOT EXISTS (SELECT 1 FROM [rb].[CacheSettings] WHERE [Id] = 1)
                INSERT INTO [rb].[CacheSettings] ([Id], [DefaultProvider], [ProvidersJson], [ModifiedAt])
                VALUES (1, N'SqlServer', N'{}', SYSDATETIMEOFFSET());
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'rb.CacheSettings', N'U') IS NOT NULL
                DROP TABLE [rb].[CacheSettings];

            IF COL_LENGTH(N'rb.DatasetCacheStates', N'CacheProvider') IS NOT NULL
                ALTER TABLE [rb].[DatasetCacheStates] DROP COLUMN [CacheProvider];
            """);
    }
}
