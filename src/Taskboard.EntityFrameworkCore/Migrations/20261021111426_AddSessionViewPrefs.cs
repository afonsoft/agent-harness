using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionViewPrefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HideEmptySessionGroups",
                table: "UserPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "SessionCliVisibilities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Cli = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Hidden = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionCliVisibilities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionCliVisibilities_UserId",
                table: "SessionCliVisibilities",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionCliVisibilities");

            migrationBuilder.DropColumn(
                name: "HideEmptySessionGroups",
                table: "UserPreferences");
        }
    }
}
