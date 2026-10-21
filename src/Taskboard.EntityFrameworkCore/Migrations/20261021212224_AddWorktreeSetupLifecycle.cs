using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddWorktreeSetupLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // setup outcome snapshot — session + task get the small tail,
            // the card keeps the full lifecycle row.
            foreach (var col in new[] { "SetupLogTail", "SetupState" })
            {
                migrationBuilder.AddColumn<string>(
                    name: col, table: "WorktreeSessions", type: "TEXT", nullable: true);
                migrationBuilder.AddColumn<string>(
                    name: col, table: "DelegationTasks", type: "TEXT", nullable: true);
            }

            migrationBuilder.AddColumn<int>(
                name: "SetupExitCode", table: "WorktreeCards",
                type: "INTEGER", nullable: true);

            foreach (var col in new[] { "SetupFinishedAtUtc", "SetupStartedAtUtc" })
            {
                migrationBuilder.AddColumn<DateTime>(
                    name: col, table: "WorktreeCards", type: "TEXT", nullable: true);
            }

            migrationBuilder.AddColumn<string>(
                name: "SetupLog", table: "WorktreeCards", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "SetupState", table: "WorktreeCards", type: "TEXT",
                maxLength: 16, nullable: false, defaultValue: "skipped");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var col in new[] { "SetupLogTail", "SetupState" })
            {
                migrationBuilder.DropColumn(name: col, table: "WorktreeSessions");
                migrationBuilder.DropColumn(name: col, table: "DelegationTasks");
            }

            migrationBuilder.DropColumn(name: "SetupExitCode", table: "WorktreeCards");

            foreach (var col in new[] { "SetupFinishedAtUtc", "SetupStartedAtUtc", "SetupLog", "SetupState" })
            {
                migrationBuilder.DropColumn(name: col, table: "WorktreeCards");
            }
        }
    }
}
