using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportScheduleDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cadence",
                schema: "rb",
                table: "ReportSchedules",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DayOfMonth",
                schema: "rb",
                table: "ReportSchedules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DayOfWeek",
                schema: "rb",
                table: "ReportSchedules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                schema: "rb",
                table: "ReportSchedules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Format",
                schema: "rb",
                table: "ReportSchedules",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                schema: "rb",
                table: "ReportSchedules",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRunAt",
                schema: "rb",
                table: "ReportSchedules",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalTime",
                schema: "rb",
                table: "ReportSchedules",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                schema: "rb",
                table: "ReportSchedules",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextRunAt",
                schema: "rb",
                table: "ReportSchedules",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                schema: "rb",
                table: "ReportSchedules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportScheduleRecipients",
                schema: "rb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportScheduleRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportScheduleRecipients_ReportSchedules_ReportId",
                        column: x => x.ReportId,
                        principalSchema: "rb",
                        principalTable: "ReportSchedules",
                        principalColumn: "ReportId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportScheduleRuns",
                schema: "rb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportScheduleRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportScheduleRuns_ReportSchedules_ReportId",
                        column: x => x.ReportId,
                        principalSchema: "rb",
                        principalTable: "ReportSchedules",
                        principalColumn: "ReportId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSchedules_Enabled_NextRunAt",
                schema: "rb",
                table: "ReportSchedules",
                columns: new[] { "Enabled", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportScheduleRecipients_ReportId_Email",
                schema: "rb",
                table: "ReportScheduleRecipients",
                columns: new[] { "ReportId", "Email" },
                unique: true,
                filter: "[UserId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportScheduleRecipients_ReportId_UserId",
                schema: "rb",
                table: "ReportScheduleRecipients",
                columns: new[] { "ReportId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportScheduleRuns_ReportId_StartedAt",
                schema: "rb",
                table: "ReportScheduleRuns",
                columns: new[] { "ReportId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportScheduleRecipients",
                schema: "rb");

            migrationBuilder.DropTable(
                name: "ReportScheduleRuns",
                schema: "rb");

            migrationBuilder.DropIndex(
                name: "IX_ReportSchedules_Enabled_NextRunAt",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "Cadence",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "DayOfMonth",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "DayOfWeek",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "Enabled",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "Format",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "Language",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "LastRunAt",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "LocalTime",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "NextRunAt",
                schema: "rb",
                table: "ReportSchedules");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                schema: "rb",
                table: "ReportSchedules");
        }
    }
}
