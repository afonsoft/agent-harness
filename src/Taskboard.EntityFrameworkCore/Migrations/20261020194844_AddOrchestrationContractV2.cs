using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddOrchestrationContractV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveDispatchId",
                table: "DelegationTasks",
                type: "TEXT",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunId",
                table: "DelegationTasks",
                type: "TEXT",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryId",
                table: "AgentMailboxMessages",
                type: "TEXT",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchId",
                table: "AgentMailboxMessages",
                type: "TEXT",
                maxLength: 96,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DelegationRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Objective = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    CoordinatorKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Messages = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dispatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    WorkerKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PreambleJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    TerminalHandle = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ArchivedOutput = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SettledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dispatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMailboxMessages_Scope_DeliveryId",
                table: "AgentMailboxMessages",
                columns: new[] { "Scope", "DeliveryId" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationRuns_Scope_Status",
                table: "DelegationRuns",
                columns: new[] { "Scope", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_RunId_AckedAtUtc",
                table: "Deliveries",
                columns: new[] { "RunId", "AckedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_RunId_State",
                table: "Dispatches",
                columns: new[] { "RunId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_TaskId_Attempt",
                table: "Dispatches",
                columns: new[] { "TaskId", "Attempt" });

            // SPEC-20261020-v2 (não-funcional): dados antigos → run legado
            // 'run-legacy' + delivery única acked — sem replay de histórico.
            migrationBuilder.Sql(
                "INSERT INTO \"DelegationRuns\" (\"Id\", \"Scope\", \"Objective\", \"CoordinatorKey\", \"Status\", \"CreatedAtUtc\", \"SettledAtUtc\", \"Version\") "
                + "VALUES ('run-legacy', 'legacy', 'pre-v2 migration', 'system', 'Settled', '2026-10-20 00:00:00.0000000', '2026-10-20 00:00:00.0000000', 0);");
            migrationBuilder.Sql(
                "UPDATE \"DelegationTasks\" SET \"RunId\" = 'run-legacy' WHERE \"RunId\" IS NULL;");
            migrationBuilder.Sql(
                "INSERT INTO \"Deliveries\" (\"Id\", \"RunId\", \"Messages\", \"CreatedAtUtc\", \"AckedAtUtc\", \"Version\") "
                + "VALUES ('del-legacy', 'run-legacy', '[]', '2026-10-20 00:00:00.0000000', '2026-10-20 00:00:00.0000000', 0);");
            migrationBuilder.Sql(
                "UPDATE \"AgentMailboxMessages\" SET \"DeliveryId\" = 'del-legacy' WHERE \"DeliveryId\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelegationRuns");

            migrationBuilder.DropTable(
                name: "Deliveries");

            migrationBuilder.DropTable(
                name: "Dispatches");

            migrationBuilder.DropIndex(
                name: "IX_AgentMailboxMessages_Scope_DeliveryId",
                table: "AgentMailboxMessages");

            migrationBuilder.DropColumn(
                name: "ActiveDispatchId",
                table: "DelegationTasks");

            migrationBuilder.DropColumn(
                name: "RunId",
                table: "DelegationTasks");

            migrationBuilder.DropColumn(
                name: "DeliveryId",
                table: "AgentMailboxMessages");

            migrationBuilder.DropColumn(
                name: "DispatchId",
                table: "AgentMailboxMessages");
        }
    }
}
