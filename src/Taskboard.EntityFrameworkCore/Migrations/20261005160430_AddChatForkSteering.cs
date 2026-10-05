using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddChatForkSteering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ForkedFromMessageId",
                table: "ChatMessages",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForkedAtMessageId",
                table: "ChatConversations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForkedFromConversationId",
                table: "ChatConversations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatSteers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSteers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatSteers_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ForkedFromConversationId",
                table: "ChatConversations",
                column: "ForkedFromConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSteers_ConversationId",
                table: "ChatSteers",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSteers_RunId_ClaimedAt",
                table: "ChatSteers",
                columns: new[] { "RunId", "ClaimedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatSteers");

            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ForkedFromConversationId",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "ForkedFromMessageId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ForkedAtMessageId",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "ForkedFromConversationId",
                table: "ChatConversations");
        }
    }
}
