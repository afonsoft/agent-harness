using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorktreeCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorktreeCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorktreePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    RepoPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    WorkspaceStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StatusCommentedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorktreeCards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeCards_RepoPath",
                table: "WorktreeCards",
                column: "RepoPath");

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeCards_WorktreePath",
                table: "WorktreeCards",
                column: "WorktreePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorktreeCards");
        }
    }
}
