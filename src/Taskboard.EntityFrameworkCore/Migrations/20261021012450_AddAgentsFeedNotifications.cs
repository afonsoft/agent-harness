using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentsFeedNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentFeedItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 280, nullable: true),
                    DeepLink = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SessionKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Cli = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IssueNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NeedsAttention = table.Column<bool>(type: "INTEGER", nullable: false),
                    RepeatCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentFeedItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserFeedReads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 224, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ManuallyUnread = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFeedReads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentFeedItems_UserId_AtUtc",
                table: "AgentFeedItems",
                columns: new[] { "UserId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentFeedItems_UserId_SessionKey",
                table: "AgentFeedItems",
                columns: new[] { "UserId", "SessionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedReads_UserId",
                table: "UserFeedReads",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentFeedItems");

            migrationBuilder.DropTable(
                name: "UserFeedReads");
        }
    }
}
