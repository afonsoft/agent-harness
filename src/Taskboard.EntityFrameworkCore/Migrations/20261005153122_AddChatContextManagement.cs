using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddChatContextManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompactionCount",
                table: "ChatRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContextTokensLimit",
                table: "ChatRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "normal");

            migrationBuilder.AddColumn<string>(
                name: "SupersedesUntilMessageId",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompactionCount",
                table: "ChatRuns");

            migrationBuilder.DropColumn(
                name: "ContextTokensLimit",
                table: "ChatRuns");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "SupersedesUntilMessageId",
                table: "ChatMessages");
        }
    }
}
