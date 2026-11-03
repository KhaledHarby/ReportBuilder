using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatasetAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.Datasets', N'IsRestricted') IS NULL
                    ALTER TABLE [rb].[Datasets] ADD [IsRestricted] bit NOT NULL CONSTRAINT [DF_Datasets_IsRestricted] DEFAULT CONVERT(bit, 0);

                IF OBJECT_ID(N'rb.DatasetAccess', N'U') IS NULL
                BEGIN
                    CREATE TABLE [rb].[DatasetAccess] (
                        [DatasetId] uniqueidentifier NOT NULL,
                        [UserId] uniqueidentifier NOT NULL,
                        CONSTRAINT [PK_DatasetAccess] PRIMARY KEY ([DatasetId], [UserId]),
                        CONSTRAINT [FK_DatasetAccess_Datasets_DatasetId] FOREIGN KEY ([DatasetId]) REFERENCES [rb].[Datasets] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_DatasetAccess_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [rb].[Users] ([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_DatasetAccess_UserId] ON [rb].[DatasetAccess] ([UserId]);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatasetAccess",
                schema: "rb");

            migrationBuilder.DropColumn(
                name: "IsRestricted",
                schema: "rb",
                table: "Datasets");
        }
    }
}
