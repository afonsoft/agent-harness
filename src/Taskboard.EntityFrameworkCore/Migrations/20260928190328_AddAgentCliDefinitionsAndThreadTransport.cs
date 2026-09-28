using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentCliDefinitionsAndThreadTransport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentCliId",
                table: "AiChatThreads",
                type: "TEXT",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContainerContext",
                table: "AiChatThreads",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Transport",
                table: "AiChatThreads",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "acp");

            migrationBuilder.CreateTable(
                name: "AgentCliDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Executable = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ArgsTemplate = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Transport = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ModelFlag = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    VersionArgs = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentCliDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentCliDefinitions_DisplayName",
                table: "AgentCliDefinitions",
                column: "DisplayName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentCliDefinitions");

            migrationBuilder.DropColumn(
                name: "AgentCliId",
                table: "AiChatThreads");

            migrationBuilder.DropColumn(
                name: "ContainerContext",
                table: "AiChatThreads");

            migrationBuilder.DropColumn(
                name: "Transport",
                table: "AiChatThreads");
        }
    }
}
