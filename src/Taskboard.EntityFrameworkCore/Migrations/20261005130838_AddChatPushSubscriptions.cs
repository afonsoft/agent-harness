using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddChatPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatPushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Auth = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatPushSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatPushSubscriptions_Endpoint",
                table: "ChatPushSubscriptions",
                column: "Endpoint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatPushSubscriptions");
        }
    }
}
