using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatasetDesignerRoleAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessMode",
                schema: "rb",
                table: "Datasets",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Everyone");

            migrationBuilder.CreateTable(
                name: "DatasetRoleAccess",
                schema: "rb",
                columns: table => new
                {
                    DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetRoleAccess", x => new { x.DatasetId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_DatasetRoleAccess_Datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalSchema: "rb",
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DatasetRoleAccess_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "rb",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetRoleAccess_RoleId",
                schema: "rb",
                table: "DatasetRoleAccess",
                column: "RoleId");

            migrationBuilder.Sql("""
                UPDATE [rb].[Datasets]
                SET [AccessMode] = N'People'
                WHERE [IsRestricted] = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatasetRoleAccess",
                schema: "rb");

            migrationBuilder.DropColumn(
                name: "AccessMode",
                schema: "rb",
                table: "Datasets");
        }
    }
}
