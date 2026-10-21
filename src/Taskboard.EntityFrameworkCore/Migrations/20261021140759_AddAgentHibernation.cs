using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentHibernation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentHibernations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Cli = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsHibernated = table.Column<bool>(type: "INTEGER", nullable: false),
                    HibernatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IdleAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResumeArgvJson = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    NativeSessionId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Cwd = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AutoResumedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Pinned = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentHibernations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentHibernations_UserId",
                table: "AgentHibernations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentHibernations");
        }
    }
}
