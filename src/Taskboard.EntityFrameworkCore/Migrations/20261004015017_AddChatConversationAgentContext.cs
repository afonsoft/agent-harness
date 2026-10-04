using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddChatConversationAgentContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentCli",
                table: "ChatConversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentModel",
                table: "ChatConversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepositoryFullName",
                table: "ChatConversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkspacePath",
                table: "ChatConversations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentCli",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AgentModel",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "RepositoryFullName",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "WorkspacePath",
                table: "ChatConversations");
        }
    }
}
