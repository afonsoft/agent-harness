using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentBoardReadMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentBoardReadMarkers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CardId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentBoardReadMarkers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentBoardReadMarkers_UserId",
                table: "AgentBoardReadMarkers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentBoardReadMarkers");
        }
    }
}
