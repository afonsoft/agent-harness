using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDelegationTasksAndMailbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentMailboxMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FromAgent = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ToAgent = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentMailboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DelegationTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    CliName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DependsOn = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    RetryOf = table.Column<string>(type: "TEXT", maxLength: 96, nullable: true),
                    FanoutGroupId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: true),
                    UseWorktree = table.Column<bool>(type: "INTEGER", nullable: false),
                    WorktreeRunId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: true),
                    WorkspacePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    RepositoryPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    BaseCommitSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ResultSummary = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastHeartbeatAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMailboxMessages_Scope_ToAgent_ReadAt",
                table: "AgentMailboxMessages",
                columns: new[] { "Scope", "ToAgent", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationTasks_FanoutGroupId",
                table: "DelegationTasks",
                column: "FanoutGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationTasks_Scope_CreatedAt",
                table: "DelegationTasks",
                columns: new[] { "Scope", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationTasks_Status",
                table: "DelegationTasks",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentMailboxMessages");

            migrationBuilder.DropTable(
                name: "DelegationTasks");
        }
    }
}
