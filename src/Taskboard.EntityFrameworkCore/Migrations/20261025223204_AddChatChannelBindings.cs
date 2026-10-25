using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddChatChannelBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatChannelBindings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExternalChannelId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalThreadId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatChannelBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatChannelBindings_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatChannelBindings_Channel_ExternalChannelId_ExternalThreadId",
                table: "ChatChannelBindings",
                columns: new[] { "Channel", "ExternalChannelId", "ExternalThreadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatChannelBindings_ConversationId",
                table: "ChatChannelBindings",
                column: "ConversationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatChannelBindings");
        }
    }
}
