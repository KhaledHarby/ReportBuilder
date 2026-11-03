using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatasetSqlCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.Datasets', N'IsCached') IS NULL
                    ALTER TABLE [rb].[Datasets] ADD [IsCached] bit NOT NULL
                        CONSTRAINT [DF_Datasets_IsCached] DEFAULT (0);

                IF COL_LENGTH(N'rb.Datasets', N'CacheScheduleMode') IS NULL
                    ALTER TABLE [rb].[Datasets] ADD [CacheScheduleMode] nvarchar(16) NOT NULL
                        CONSTRAINT [DF_Datasets_CacheScheduleMode] DEFAULT (N'Manual');

                IF COL_LENGTH(N'rb.Datasets', N'CacheIntervalMinutes') IS NULL
                    ALTER TABLE [rb].[Datasets] ADD [CacheIntervalMinutes] int NULL;

                IF COL_LENGTH(N'rb.Datasets', N'CacheDailyAtLocalTime') IS NULL
                    ALTER TABLE [rb].[Datasets] ADD [CacheDailyAtLocalTime] nvarchar(5) NULL;

                IF OBJECT_ID(N'rb.DatasetCacheStates', N'U') IS NULL
                BEGIN
                    CREATE TABLE [rb].[DatasetCacheStates] (
                        [DatasetId] uniqueidentifier NOT NULL,
                        [Status] nvarchar(16) NOT NULL,
                        [CacheTableName] nvarchar(128) NULL,
                        [LastRefreshStartedAt] datetimeoffset NULL,
                        [LastRefreshCompletedAt] datetimeoffset NULL,
                        [LastSuccessAt] datetimeoffset NULL,
                        [NextRefreshAt] datetimeoffset NULL,
                        [RowCount] bigint NULL,
                        [DurationMs] int NULL,
                        [SizeBytes] bigint NULL,
                        [RefreshedByServer] nvarchar(128) NULL,
                        [RefreshedByUserId] uniqueidentifier NULL,
                        [LastError] nvarchar(2000) NULL,
                        [CacheVersion] int NOT NULL CONSTRAINT [DF_DatasetCacheStates_CacheVersion] DEFAULT (0),
                        CONSTRAINT [PK_DatasetCacheStates] PRIMARY KEY ([DatasetId]),
                        CONSTRAINT [FK_DatasetCacheStates_Datasets_DatasetId]
                            FOREIGN KEY ([DatasetId]) REFERENCES [rb].[Datasets] ([Id]) ON DELETE CASCADE
                    );

                    CREATE INDEX [IX_DatasetCacheStates_NextRefreshAt]
                        ON [rb].[DatasetCacheStates] ([NextRefreshAt]);

                    CREATE INDEX [IX_DatasetCacheStates_Status]
                        ON [rb].[DatasetCacheStates] ([Status]);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'rb.DatasetCacheStates', N'U') IS NOT NULL
                    DROP TABLE [rb].[DatasetCacheStates];

                IF COL_LENGTH(N'rb.Datasets', N'CacheDailyAtLocalTime') IS NOT NULL
                    ALTER TABLE [rb].[Datasets] DROP COLUMN [CacheDailyAtLocalTime];

                IF COL_LENGTH(N'rb.Datasets', N'CacheIntervalMinutes') IS NOT NULL
                    ALTER TABLE [rb].[Datasets] DROP COLUMN [CacheIntervalMinutes];

                IF COL_LENGTH(N'rb.Datasets', N'CacheScheduleMode') IS NOT NULL
                BEGIN
                    IF OBJECT_ID(N'rb.DF_Datasets_CacheScheduleMode', N'D') IS NOT NULL
                        ALTER TABLE [rb].[Datasets] DROP CONSTRAINT [DF_Datasets_CacheScheduleMode];
                    ALTER TABLE [rb].[Datasets] DROP COLUMN [CacheScheduleMode];
                END

                IF COL_LENGTH(N'rb.Datasets', N'IsCached') IS NOT NULL
                BEGIN
                    IF OBJECT_ID(N'rb.DF_Datasets_IsCached', N'D') IS NOT NULL
                        ALTER TABLE [rb].[Datasets] DROP CONSTRAINT [DF_Datasets_IsCached];
                    ALTER TABLE [rb].[Datasets] DROP COLUMN [IsCached];
                END
                """);
        }
    }
}
