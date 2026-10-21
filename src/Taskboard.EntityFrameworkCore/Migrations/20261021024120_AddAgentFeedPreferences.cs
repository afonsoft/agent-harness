using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentFeedPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserFeedPreferences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Badge = table.Column<bool>(type: "INTEGER", nullable: false),
                    Sound = table.Column<bool>(type: "INTEGER", nullable: false),
                    Persist = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFeedPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFeedPreferences_UserId",
                table: "UserFeedPreferences",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserFeedPreferences");
        }
    }
}
