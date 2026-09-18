using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueHistoryEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssueHistoryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IssueId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Repository = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    From = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    To = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueHistoryEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueHistoryEvents_IssueId",
                table: "IssueHistoryEvents",
                column: "IssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueHistoryEvents");
        }
    }
}
