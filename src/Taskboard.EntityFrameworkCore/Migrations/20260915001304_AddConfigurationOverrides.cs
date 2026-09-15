using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfigurationOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationOverrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationOverrides_Key",
                table: "ConfigurationOverrides",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfigurationOverrides");
        }
    }
}
