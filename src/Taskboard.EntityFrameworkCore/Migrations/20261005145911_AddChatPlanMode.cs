using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddChatPlanMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanMode",
                table: "ChatConversations",
                type: "TEXT",
                maxLength: 8,
                nullable: false,
                defaultValue: "off");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ChatApprovals",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "tool-call");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanMode",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ChatApprovals");
        }
    }
}
