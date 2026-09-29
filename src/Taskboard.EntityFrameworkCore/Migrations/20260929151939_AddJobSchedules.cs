using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddJobSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IntervalSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSchedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobSchedules_JobKey",
                table: "JobSchedules",
                column: "JobKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobSchedules");
        }
    }
}
