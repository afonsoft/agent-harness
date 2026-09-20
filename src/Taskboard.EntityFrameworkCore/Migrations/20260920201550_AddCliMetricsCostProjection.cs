using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCliMetricsCostProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostUsd",
                table: "CliSessionMetrics",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostUsd",
                table: "CliDailyUsageAggregates",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.InsertData(
                table: "ModelPriceRates",
                columns: new[] { "Id", "CacheReadPer1M", "CacheWritePer1M", "InputPer1M", "ModelPattern", "OutputPer1M", "Provider" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000013"), 0m, 0m, 0m, "free-stack", 0m, "omniroute" },
                    { new Guid("a1000000-0000-0000-0000-000000000014"), 0m, 0m, 0m, "auto/best-free", 0m, "omniroute" },
                    { new Guid("a1000000-0000-0000-0000-000000000015"), 0.30m, 3.75m, 3.00m, "auto/best-coding", 15.00m, "omniroute" },
                    { new Guid("a1000000-0000-0000-0000-000000000016"), 1.50m, 18.75m, 15.00m, "Opus", 75.00m, "omniroute" },
                    { new Guid("a1000000-0000-0000-0000-000000000017"), 0.07m, 0.27m, 0.27m, "DeepSeek", 1.10m, "omniroute" },
                    { new Guid("a1000000-0000-0000-0000-000000000018"), 0m, 0m, 0m, "mimo-v2.5-free", 0m, "opencode" },
                    { new Guid("a1000000-0000-0000-0000-000000000019"), 0m, 0m, 0m, "hy3-free", 0m, "opencode" },
                    { new Guid("a1000000-0000-0000-0000-000000000020"), 0m, 0m, 0m, "muse-spark-1.2-contributor-free", 0m, "opencode" },
                    { new Guid("a1000000-0000-0000-0000-000000000021"), 0.03m, 0.30m, 0.30m, "gemini-3.8-flash", 2.50m, "google" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ModelPriceRates",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                table: "ModelPriceRates",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                table: "ModelPriceRates",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000015"));

            migrationBuilder.DeleteData(
                table: "ModelPriceRates",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000016"));

            migrationBuilder.DeleteData(
                table: "ModelPriceRates",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000017"));

            migrationBuilder.DeleteData(
                table: "ModelPriceRates",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000018"));

            migrationBuilder.DeleteData(
                table: "ModelPriceRates",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000019"));

            migrationBuilder.DeleteData(
                table: "ModelPriceRates",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000020"));

            migrationBuilder.DeleteData(
                table: "ModelPriceRates",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000021"));

            migrationBuilder.DropColumn(
                name: "CostUsd",
                table: "CliSessionMetrics");

            migrationBuilder.DropColumn(
                name: "CostUsd",
                table: "CliDailyUsageAggregates");
        }
    }
}
