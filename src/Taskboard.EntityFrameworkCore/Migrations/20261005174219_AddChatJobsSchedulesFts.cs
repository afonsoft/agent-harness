using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddChatJobsSchedulesFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Command = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Pid = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatJobs_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatSchedules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CronExpression = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    NextFireAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastDeliveredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatSchedules_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatJobs_ConversationId_CreatedAt",
                table: "ChatJobs",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatJobs_Status",
                table: "ChatJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSchedules_Active_NextFireAtUtc",
                table: "ChatSchedules",
                columns: new[] { "Active", "NextFireAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSchedules_ConversationId",
                table: "ChatSchedules",
                column: "ConversationId");

            // SPEC-20261005-chat-jobs-schedule-search RF-007: FTS5 index over
            // message content — UNINDEXED columns stored, not tokenized.
            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE ChatMessageFts USING fts5("
                + "content, conversationId UNINDEXED, messageId UNINDEXED);");

            // Backfill: index every existing content row.
            migrationBuilder.Sql(
                "INSERT INTO ChatMessageFts(content, conversationId, messageId) "
                + "SELECT Content, ConversationId, Id FROM ChatMessages "
                + "WHERE Content IS NOT NULL AND Content <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS ChatMessageFts;");

            migrationBuilder.DropTable(
                name: "ChatJobs");

            migrationBuilder.DropTable(
                name: "ChatSchedules");
        }
    }
}
