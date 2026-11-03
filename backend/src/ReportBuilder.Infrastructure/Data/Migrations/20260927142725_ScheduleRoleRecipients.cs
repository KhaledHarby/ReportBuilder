using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleRoleRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReportScheduleRecipients_ReportId_Email",
                schema: "rb",
                table: "ReportScheduleRecipients");

            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                schema: "rb",
                table: "ReportScheduleRecipients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportScheduleRecipients_ReportId_Email",
                schema: "rb",
                table: "ReportScheduleRecipients",
                columns: new[] { "ReportId", "Email" },
                unique: true,
                filter: "[UserId] IS NULL AND [RoleId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportScheduleRecipients_ReportId_RoleId",
                schema: "rb",
                table: "ReportScheduleRecipients",
                columns: new[] { "ReportId", "RoleId" },
                unique: true,
                filter: "[RoleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportScheduleRecipients_RoleId",
                schema: "rb",
                table: "ReportScheduleRecipients",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReportScheduleRecipients_Roles_RoleId",
                schema: "rb",
                table: "ReportScheduleRecipients",
                column: "RoleId",
                principalSchema: "rb",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReportScheduleRecipients_Roles_RoleId",
                schema: "rb",
                table: "ReportScheduleRecipients");

            migrationBuilder.DropIndex(
                name: "IX_ReportScheduleRecipients_ReportId_Email",
                schema: "rb",
                table: "ReportScheduleRecipients");

            migrationBuilder.DropIndex(
                name: "IX_ReportScheduleRecipients_ReportId_RoleId",
                schema: "rb",
                table: "ReportScheduleRecipients");

            migrationBuilder.DropIndex(
                name: "IX_ReportScheduleRecipients_RoleId",
                schema: "rb",
                table: "ReportScheduleRecipients");

            migrationBuilder.DropColumn(
                name: "RoleId",
                schema: "rb",
                table: "ReportScheduleRecipients");

            migrationBuilder.CreateIndex(
                name: "IX_ReportScheduleRecipients_ReportId_Email",
                schema: "rb",
                table: "ReportScheduleRecipients",
                columns: new[] { "ReportId", "Email" },
                unique: true,
                filter: "[UserId] IS NULL");
        }
    }
}
