using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorktreeSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorktreeSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RepositoryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    BaseBranch = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CommitSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RetainOnFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorktreeSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeSessions_RunId",
                table: "WorktreeSessions",
                column: "RunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorktreeSessions");
        }
    }
}
