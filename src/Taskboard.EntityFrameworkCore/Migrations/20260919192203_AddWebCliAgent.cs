using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWebCliAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentType",
                table: "AiChatThreads",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "AiChatThreads",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "assistant");

            migrationBuilder.AddColumn<string>(
                name: "RepositoryFullName",
                table: "AiChatThreads",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkspacePath",
                table: "AiChatThreads",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "AiChatEvents",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "message");

            migrationBuilder.AddColumn<string>(
                name: "PayloadJson",
                table: "AiChatEvents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentType",
                table: "AiChatThreads");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "AiChatThreads");

            migrationBuilder.DropColumn(
                name: "RepositoryFullName",
                table: "AiChatThreads");

            migrationBuilder.DropColumn(
                name: "WorkspacePath",
                table: "AiChatThreads");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "AiChatEvents");

            migrationBuilder.DropColumn(
                name: "PayloadJson",
                table: "AiChatEvents");
        }
    }
}
