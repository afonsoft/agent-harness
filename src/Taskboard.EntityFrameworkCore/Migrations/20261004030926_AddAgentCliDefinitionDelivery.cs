using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentCliDefinitionDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelListArgs",
                table: "AgentCliDefinitions",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptDelivery",
                table: "AgentCliDefinitions",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "argv");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelListArgs",
                table: "AgentCliDefinitions");

            migrationBuilder.DropColumn(
                name: "PromptDelivery",
                table: "AgentCliDefinitions");
        }
    }
}
