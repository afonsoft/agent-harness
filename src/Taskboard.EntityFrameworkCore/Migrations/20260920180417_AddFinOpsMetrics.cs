using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddFinOpsMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BudgetCapUsd",
                table: "PipelineExecutions",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModelPriceRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModelPattern = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InputPer1M = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    OutputPer1M = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    CacheWritePer1M = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    CacheReadPer1M = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelPriceRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunCostMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StageKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AgentType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheWriteTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheReadTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CostUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    BudgetCapUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunCostMetrics", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ModelPriceRates",
                columns: new[] { "Id", "CacheReadPer1M", "CacheWritePer1M", "InputPer1M", "ModelPattern", "OutputPer1M", "Provider" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000001"), 0.30m, 3.75m, 3.00m, "claude-3-7-sonnet", 15.00m, "anthropic" },
                    { new Guid("a1000000-0000-0000-0000-000000000002"), 0.30m, 3.75m, 3.00m, "claude-sonnet-4", 15.00m, "anthropic" },
                    { new Guid("a1000000-0000-0000-0000-000000000003"), 0.10m, 1.25m, 1.00m, "claude-haiku-4", 5.00m, "anthropic" },
                    { new Guid("a1000000-0000-0000-0000-000000000004"), 1.50m, 18.75m, 15.00m, "claude-opus-4", 75.00m, "anthropic" },
                    { new Guid("a1000000-0000-0000-0000-000000000005"), 0.125m, 1.25m, 1.25m, "gpt-5", 10.00m, "openai" },
                    { new Guid("a1000000-0000-0000-0000-000000000006"), 0.025m, 0.25m, 0.25m, "gpt-5-mini", 2.00m, "openai" },
                    { new Guid("a1000000-0000-0000-0000-000000000007"), 0.50m, 2.00m, 2.00m, "gpt-4.1", 8.00m, "openai" },
                    { new Guid("a1000000-0000-0000-0000-000000000008"), 0.07m, 0.27m, 0.27m, "deepseek-chat", 1.10m, "deepseek" },
                    { new Guid("a1000000-0000-0000-0000-000000000009"), 0.14m, 0.55m, 0.55m, "deepseek-reasoner", 2.19m, "deepseek" },
                    { new Guid("a1000000-0000-0000-0000-000000000010"), 0.125m, 1.25m, 1.25m, "gemini-2.5-pro", 10.00m, "google" },
                    { new Guid("a1000000-0000-0000-0000-000000000011"), 0.03m, 0.30m, 0.30m, "gemini-2.5-flash", 2.50m, "google" },
                    { new Guid("a1000000-0000-0000-0000-000000000012"), 0.30m, 3.75m, 3.00m, "*", 15.00m, "*" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelPriceRates_ModelPattern",
                table: "ModelPriceRates",
                column: "ModelPattern",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunCostMetrics_AgentType_ModelName",
                table: "RunCostMetrics",
                columns: new[] { "AgentType", "ModelName" });

            migrationBuilder.CreateIndex(
                name: "IX_RunCostMetrics_RecordedAtUtc",
                table: "RunCostMetrics",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RunCostMetrics_RunId",
                table: "RunCostMetrics",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelPriceRates");

            migrationBuilder.DropTable(
                name: "RunCostMetrics");

            migrationBuilder.DropColumn(
                name: "BudgetCapUsd",
                table: "PipelineExecutions");
        }
    }
}
