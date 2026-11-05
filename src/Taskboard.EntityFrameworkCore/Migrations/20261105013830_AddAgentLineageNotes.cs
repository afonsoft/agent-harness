using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentLineageNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParentDispatchId",
                table: "Dispatches",
                type: "TEXT",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyParent",
                table: "DelegationTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OutliveParent",
                table: "DelegationTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ParentDispatchId",
                table: "DelegationTasks",
                type: "TEXT",
                maxLength: 96,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentOutboxEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    SessionKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentOutboxEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_ParentDispatchId",
                table: "Dispatches",
                column: "ParentDispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentOutboxEntries_SessionKey_CreatedAtUtc",
                table: "AgentOutboxEntries",
                columns: new[] { "SessionKey", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentOutboxEntries");

            migrationBuilder.DropIndex(
                name: "IX_Dispatches_ParentDispatchId",
                table: "Dispatches");

            migrationBuilder.DropColumn(
                name: "ParentDispatchId",
                table: "Dispatches");

            migrationBuilder.DropColumn(
                name: "NotifyParent",
                table: "DelegationTasks");

            migrationBuilder.DropColumn(
                name: "OutliveParent",
                table: "DelegationTasks");

            migrationBuilder.DropColumn(
                name: "ParentDispatchId",
                table: "DelegationTasks");
        }
    }
}
