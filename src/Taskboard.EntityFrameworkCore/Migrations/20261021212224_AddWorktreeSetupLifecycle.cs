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
            migrationBuilder.AddColumn<string>(
                name: "SetupLogTail",
                table: "WorktreeSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SetupState",
                table: "WorktreeSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SetupExitCode",
                table: "WorktreeCards",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SetupFinishedAtUtc",
                table: "WorktreeCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SetupLog",
                table: "WorktreeCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SetupStartedAtUtc",
                table: "WorktreeCards",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SetupState",
                table: "WorktreeCards",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "skipped");

            migrationBuilder.AddColumn<string>(
                name: "SetupLogTail",
                table: "DelegationTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SetupState",
                table: "DelegationTasks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupLogTail",
                table: "WorktreeSessions");

            migrationBuilder.DropColumn(
                name: "SetupState",
                table: "WorktreeSessions");

            migrationBuilder.DropColumn(
                name: "SetupExitCode",
                table: "WorktreeCards");

            migrationBuilder.DropColumn(
                name: "SetupFinishedAtUtc",
                table: "WorktreeCards");

            migrationBuilder.DropColumn(
                name: "SetupLog",
                table: "WorktreeCards");

            migrationBuilder.DropColumn(
                name: "SetupStartedAtUtc",
                table: "WorktreeCards");

            migrationBuilder.DropColumn(
                name: "SetupState",
                table: "WorktreeCards");

            migrationBuilder.DropColumn(
                name: "SetupLogTail",
                table: "DelegationTasks");

            migrationBuilder.DropColumn(
                name: "SetupState",
                table: "DelegationTasks");
        }
    }
}
