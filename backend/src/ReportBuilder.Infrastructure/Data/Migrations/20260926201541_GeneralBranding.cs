using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class GeneralBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationName",
                schema: "rb",
                table: "AppSettings",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationNameArabic",
                schema: "rb",
                table: "AppSettings",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "Logo",
                schema: "rb",
                table: "AppSettings",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoContentType",
                schema: "rb",
                table: "AppSettings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizationName",
                schema: "rb",
                table: "AppSettings",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrganizationNameArabic",
                schema: "rb",
                table: "AppSettings",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StudioName",
                schema: "rb",
                table: "AppSettings",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StudioNameArabic",
                schema: "rb",
                table: "AppSettings",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicationName",
                schema: "rb",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ApplicationNameArabic",
                schema: "rb",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "Logo",
                schema: "rb",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LogoContentType",
                schema: "rb",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "OrganizationName",
                schema: "rb",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "OrganizationNameArabic",
                schema: "rb",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "StudioName",
                schema: "rb",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "StudioNameArabic",
                schema: "rb",
                table: "AppSettings");
        }
    }
}
