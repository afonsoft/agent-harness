using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDelegationContractV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutputSchema",
                table: "DelegationTasks",
                type: "TEXT",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "DelegationTasks",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "leaf");

            migrationBuilder.AddColumn<bool>(
                name: "SchemaValid",
                table: "DelegationTasks",
                type: "INTEGER",
                nullable: true);

            // RF-G02 backfill: pre-existing coordinators orchestrate by
            // definition — leaf would deadlock them at materialization.
            migrationBuilder.Sql(
                "UPDATE DelegationTasks SET Role = 'orchestrator' WHERE Kind = 'coordinate'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutputSchema",
                table: "DelegationTasks");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "DelegationTasks");

            migrationBuilder.DropColumn(
                name: "SchemaValid",
                table: "DelegationTasks");
        }
    }
}
