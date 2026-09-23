using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineAutoRetryAndFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoRetryCount",
                table: "PipelineStageExecutions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAutoRetryAtUtc",
                table: "PipelineStageExecutions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "PipelineExecutions",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoRetryCount",
                table: "PipelineStageExecutions");

            migrationBuilder.DropColumn(
                name: "NextAutoRetryAtUtc",
                table: "PipelineStageExecutions");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "PipelineExecutions");
        }
    }
}
