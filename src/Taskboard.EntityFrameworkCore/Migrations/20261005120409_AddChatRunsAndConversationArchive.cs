using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddChatRunsAndConversationArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "ChatConversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TriggerMessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PartialContent = table.Column<string>(type: "TEXT", nullable: true),
                    PartialReasoning = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    TokensIn = table.Column<int>(type: "INTEGER", nullable: true),
                    TokensOut = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatRuns_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ArchivedAt",
                table: "ChatConversations",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRuns_ConversationId_Status",
                table: "ChatRuns",
                columns: new[] { "ConversationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRuns_CreatedAt",
                table: "ChatRuns",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatRuns");

            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ArchivedAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "ChatConversations");
        }
    }
}
