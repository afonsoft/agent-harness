using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddRunTerminals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstMessageFailedReason",
                table: "Dispatches",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstMessageSentAtUtc",
                table: "Dispatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RunTerminals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    DispatchId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: true),
                    PtySessionId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    CliName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ControlOwner = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ArchivedOutput = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunTerminals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunTerminals_DispatchId",
                table: "RunTerminals",
                column: "DispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RunTerminals_PtySessionId",
                table: "RunTerminals",
                column: "PtySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RunTerminals_TaskId_State",
                table: "RunTerminals",
                columns: new[] { "TaskId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunTerminals");

            migrationBuilder.DropColumn(
                name: "FirstMessageFailedReason",
                table: "Dispatches");

            migrationBuilder.DropColumn(
                name: "FirstMessageSentAtUtc",
                table: "Dispatches");
        }
    }
}
