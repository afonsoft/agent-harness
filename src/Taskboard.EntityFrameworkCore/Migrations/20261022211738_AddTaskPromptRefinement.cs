using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskPromptRefinement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PromptRefined",
                table: "DelegationTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptRefinement",
                table: "DelegationTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefinedAt",
                table: "DelegationTasks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromptRefined",
                table: "DelegationTasks");

            migrationBuilder.DropColumn(
                name: "PromptRefinement",
                table: "DelegationTasks");

            migrationBuilder.DropColumn(
                name: "RefinedAt",
                table: "DelegationTasks");
        }
    }
}
