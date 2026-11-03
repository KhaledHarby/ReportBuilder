using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserDisplayNameArabic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.Users', N'DisplayNameArabic') IS NULL
                    ALTER TABLE [rb].[Users] ADD [DisplayNameArabic] nvarchar(128) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'rb.Users', N'DisplayNameArabic') IS NOT NULL
                    ALTER TABLE [rb].[Users] DROP COLUMN [DisplayNameArabic];
                """);
        }
    }
}
