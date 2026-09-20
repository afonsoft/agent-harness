using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCliMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CliDailyUsageAggregates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Day = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SessionsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MessagesCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TokensInput = table.Column<long>(type: "INTEGER", nullable: false),
                    TokensOutput = table.Column<long>(type: "INTEGER", nullable: false),
                    TokensCached = table.Column<long>(type: "INTEGER", nullable: false),
                    ModelsJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CliDailyUsageAggregates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CliMetricSources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ResolvedPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SchemaFingerprint = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    WatermarkCursor = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    FileModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    LastSyncUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    RowCount = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CliMetricSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CliSessionMetrics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MessageCount = table.Column<int>(type: "INTEGER", nullable: true),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TokensInput = table.Column<long>(type: "INTEGER", nullable: true),
                    TokensOutput = table.Column<long>(type: "INTEGER", nullable: true),
                    TokensCached = table.Column<long>(type: "INTEGER", nullable: true),
                    IngestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CliSessionMetrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CliDailyUsageAggregates_Kind_Day",
                table: "CliDailyUsageAggregates",
                columns: new[] { "Kind", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CliMetricSources_Kind_SourceName_ResolvedPath",
                table: "CliMetricSources",
                columns: new[] { "Kind", "SourceName", "ResolvedPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CliSessionMetrics_Kind",
                table: "CliSessionMetrics",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_CliSessionMetrics_SourceId_ExternalId",
                table: "CliSessionMetrics",
                columns: new[] { "SourceId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CliSessionMetrics_StartedAtUtc",
                table: "CliSessionMetrics",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CliDailyUsageAggregates");

            migrationBuilder.DropTable(
                name: "CliMetricSources");

            migrationBuilder.DropTable(
                name: "CliSessionMetrics");
        }
    }
}
