using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PipelineExecutions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TemplateId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RepositoryFullName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    BaseBranch = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IssueId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    InitialPrompt = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: false),
                    WorktreePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PipelineStageExecutions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExecutionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StageKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Agent = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ModelTier = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    DependsOn = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AdjustedPrompt = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true),
                    HandoffSummary = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true),
                    ApprovalComment = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineStageExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineStageExecutions_PipelineExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "PipelineExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStageExecutions_ExecutionId_StageKey",
                table: "PipelineStageExecutions",
                columns: new[] { "ExecutionId", "StageKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PipelineStageExecutions");

            migrationBuilder.DropTable(
                name: "PipelineExecutions");
        }
    }
}
