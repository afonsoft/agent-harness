using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRunEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentRunEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScopeKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ScopeId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StageId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ParentEventId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ToolCallId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    RawJson = table.Column<string>(type: "TEXT", nullable: true),
                    Stream = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRunEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunEvents_ScopeId_Kind",
                table: "AgentRunEvents",
                columns: new[] { "ScopeId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunEvents_ScopeKind_ScopeId_Sequence",
                table: "AgentRunEvents",
                columns: new[] { "ScopeKind", "ScopeId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunEvents_TimestampUtc",
                table: "AgentRunEvents",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRunEvents");
        }
    }
}
