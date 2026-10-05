using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddChatApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedToolsJson",
                table: "ChatConversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PermissionPreset",
                table: "ChatConversations",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ChatApprovals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ToolCallId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ArgumentsPreview = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatApprovals_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatApprovals_ConversationId_Status",
                table: "ChatApprovals",
                columns: new[] { "ConversationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatApprovals_RunId",
                table: "ChatApprovals",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatApprovals");

            migrationBuilder.DropColumn(
                name: "AllowedToolsJson",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "PermissionPreset",
                table: "ChatConversations");
        }
    }
}
