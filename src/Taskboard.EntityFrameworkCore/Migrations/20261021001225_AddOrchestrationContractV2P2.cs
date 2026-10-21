using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddOrchestrationContractV2P2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DelegationGates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationGates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingQuestions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    FromAgent = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Answer = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingQuestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationGates_Scope_Status",
                table: "DelegationGates",
                columns: new[] { "Scope", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationGates_TaskId_Status",
                table: "DelegationGates",
                columns: new[] { "TaskId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingQuestions_MessageId",
                table: "PendingQuestions",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingQuestions_Scope_Status",
                table: "PendingQuestions",
                columns: new[] { "Scope", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelegationGates");

            migrationBuilder.DropTable(
                name: "PendingQuestions");
        }
    }
}
