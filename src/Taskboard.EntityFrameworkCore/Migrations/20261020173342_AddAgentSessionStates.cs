using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSessionStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentSessionStates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Cli = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    AttentionReason = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    StateChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSignalAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSignalKind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    WeakSignalCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSessionStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentSessionStateTransitions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    FromState = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ToState = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    SignalKind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSessionStateTransitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessionStates_LastSignalAtUtc",
                table: "AgentSessionStates",
                column: "LastSignalAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessionStates_State_LastSignalAtUtc",
                table: "AgentSessionStates",
                columns: new[] { "State", "LastSignalAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSessionStateTransitions_SessionKey_AtUtc",
                table: "AgentSessionStateTransitions",
                columns: new[] { "SessionKey", "AtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentSessionStates");

            migrationBuilder.DropTable(
                name: "AgentSessionStateTransitions");
        }
    }
}
