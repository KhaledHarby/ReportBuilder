using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations;

public partial class SsoLaunch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExternalId",
            schema: "rb",
            table: "Users",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExternalSource",
            schema: "rb",
            table: "Users",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Users_ExternalSource_ExternalId",
            schema: "rb",
            table: "Users",
            columns: new[] { "ExternalSource", "ExternalId" },
            unique: true,
            filter: "[ExternalId] IS NOT NULL AND [ExternalSource] IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "LaunchCodes",
            schema: "rb",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ReturnPath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LaunchCodes", x => x.Id);
                table.ForeignKey(
                    name: "FK_LaunchCodes_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "rb",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LaunchCodes_CodeHash",
            schema: "rb",
            table: "LaunchCodes",
            column: "CodeHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LaunchCodes_ExpiresAt",
            schema: "rb",
            table: "LaunchCodes",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_LaunchCodes_UserId",
            schema: "rb",
            table: "LaunchCodes",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "LaunchCodes",
            schema: "rb");

        migrationBuilder.DropIndex(
            name: "IX_Users_ExternalSource_ExternalId",
            schema: "rb",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "ExternalId",
            schema: "rb",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "ExternalSource",
            schema: "rb",
            table: "Users");
    }
}
