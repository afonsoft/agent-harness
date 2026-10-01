using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RepairPipelineStageTriedAgentsDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddPipelineStageTriedAgents created the column with DEFAULT '',
            // which is not valid JSON for the list value converter.
            migrationBuilder.Sql(
                "UPDATE \"PipelineStageExecutions\" SET \"TriedAgents\" = '[]' WHERE \"TriedAgents\" = '' OR \"TriedAgents\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op by design: the data repair is idempotent — reverting the
            // schema change is unnecessary, so Down has no statements.
        }
    }
}
