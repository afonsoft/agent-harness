using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderUsageWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderUsageWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    WindowKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Used = table.Column<long>(type: "INTEGER", nullable: true),
                    Limit = table.Column<long>(type: "INTEGER", nullable: true),
                    UsedPct = table.Column<double>(type: "REAL", nullable: true),
                    ResetsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SampledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Confidence = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    WarnLevel = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderUsageWindows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderUsageWindows_Provider_WindowKind",
                table: "ProviderUsageWindows",
                columns: new[] { "Provider", "WindowKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderUsageWindows_SampledAtUtc",
                table: "ProviderUsageWindows",
                column: "SampledAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderUsageWindows");
        }
    }
}
