using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReportBuilder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportRevisions",
                schema: "rb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionArabic = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LayoutJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRestricted = table.Column<bool>(type: "bit", nullable: false),
                    AccessJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportRevisions_ReportTemplates_ReportId",
                        column: x => x.ReportId,
                        principalSchema: "rb",
                        principalTable: "ReportTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportRevisions_Users_PublishedBy",
                        column: x => x.PublishedBy,
                        principalSchema: "rb",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRevisions_PublishedBy",
                schema: "rb",
                table: "ReportRevisions",
                column: "PublishedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRevisions_ReportId_Version",
                schema: "rb",
                table: "ReportRevisions",
                columns: new[] { "ReportId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportRevisions",
                schema: "rb");
        }
    }
}
