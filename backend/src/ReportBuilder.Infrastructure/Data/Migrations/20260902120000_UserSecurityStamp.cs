using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.Users', N'SecurityStamp') IS NULL
                    ALTER TABLE [rb].[Users]
                        ADD [SecurityStamp] uniqueidentifier NOT NULL
                            CONSTRAINT [DF_Users_SecurityStamp] DEFAULT NEWID();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.Users', N'SecurityStamp') IS NOT NULL
                BEGIN
                    IF OBJECT_ID(N'rb.DF_Users_SecurityStamp', N'D') IS NOT NULL
                        ALTER TABLE [rb].[Users] DROP CONSTRAINT [DF_Users_SecurityStamp];
                    ALTER TABLE [rb].[Users] DROP COLUMN [SecurityStamp];
                END
                """);
        }
    }
}
