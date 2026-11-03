using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivityLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'rb.ActivityLogs', N'U') IS NULL
                BEGIN
                    CREATE TABLE [rb].[ActivityLogs] (
                        [Id] uniqueidentifier NOT NULL,
                        [OccurredAt] datetimeoffset NOT NULL,
                        [Level] nvarchar(16) NOT NULL,
                        [Category] nvarchar(32) NOT NULL,
                        [Action] nvarchar(64) NOT NULL,
                        [Message] nvarchar(1000) NOT NULL,
                        [ActorUserId] uniqueidentifier NULL,
                        [ActorUserName] nvarchar(64) NULL,
                        [TargetUserId] uniqueidentifier NULL,
                        [TargetUserName] nvarchar(64) NULL,
                        [EntityType] nvarchar(32) NULL,
                        [EntityId] uniqueidentifier NULL,
                        [EntityName] nvarchar(200) NULL,
                        [HttpMethod] nvarchar(16) NULL,
                        [Path] nvarchar(256) NULL,
                        [StatusCode] int NULL,
                        [DurationMs] int NULL,
                        [IpAddress] nvarchar(64) NULL,
                        [CorrelationId] nvarchar(64) NULL,
                        [Details] nvarchar(4000) NULL,
                        CONSTRAINT [PK_ActivityLogs] PRIMARY KEY ([Id])
                    );
                    CREATE INDEX [IX_ActivityLogs_ActorUserId] ON [rb].[ActivityLogs] ([ActorUserId]);
                    CREATE INDEX [IX_ActivityLogs_Category] ON [rb].[ActivityLogs] ([Category]);
                    CREATE INDEX [IX_ActivityLogs_Level] ON [rb].[ActivityLogs] ([Level]);
                    CREATE INDEX [IX_ActivityLogs_OccurredAt] ON [rb].[ActivityLogs] ([OccurredAt] DESC);
                END

                IF OBJECT_ID(N'rb.AppSettings', N'U') IS NULL
                BEGIN
                    CREATE TABLE [rb].[AppSettings] (
                        [Id] int NOT NULL,
                        [LogRetentionDays] int NOT NULL,
                        [ModifiedBy] uniqueidentifier NULL,
                        [ModifiedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_AppSettings] PRIMARY KEY ([Id])
                    );
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogs",
                schema: "rb");

            migrationBuilder.DropTable(
                name: "AppSettings",
                schema: "rb");
        }
    }
}
