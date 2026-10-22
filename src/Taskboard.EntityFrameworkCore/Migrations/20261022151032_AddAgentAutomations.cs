using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentAutomations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentAutomations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    AgentCli = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ScheduleKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CronExpression = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    NextFireAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MissedRunGraceMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ReuseSession = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExtraAgentArgs = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Precheck = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Host = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastFiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentAutomations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentAutomationRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AutomationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SkipReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentAutomationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentAutomationRuns_AgentAutomations_AutomationId",
                        column: x => x.AutomationId,
                        principalTable: "AgentAutomations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentAutomationRuns_AutomationId_FiredAtUtc",
                table: "AgentAutomationRuns",
                columns: new[] { "AutomationId", "FiredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentAutomationRuns_Outcome",
                table: "AgentAutomationRuns",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_AgentAutomations_CreatedBy",
                table: "AgentAutomations",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgentAutomations_Enabled_NextFireAtUtc",
                table: "AgentAutomations",
                columns: new[] { "Enabled", "NextFireAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentAutomationRuns");

            migrationBuilder.DropTable(
                name: "AgentAutomations");
        }
    }
}
