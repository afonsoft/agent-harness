using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskboard.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLocalProjectsAndTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiChatThreads_Projects_OriginProjectId",
                table: "AiChatThreads");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowWorkspaces_Projects_Id",
                table: "WorkflowWorkspaces");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "ProjectSummaries");

            migrationBuilder.DropTable(
                name: "TaskActivities");

            migrationBuilder.DropTable(
                name: "TaskRelations");

            migrationBuilder.DropTable(
                name: "WorkflowSequences");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "WorkflowNodes");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_AiChatThreads_OriginProjectId",
                table: "AiChatThreads");

            migrationBuilder.DropColumn(
                name: "OriginProjectId",
                table: "AiChatThreads");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginProjectId",
                table: "AiChatThreads",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NextTaskNumber = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 1L),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    WorkspacePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    labels = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectSummaries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectSummaries_Projects_Id",
                        column: x => x.Id,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    assignee = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    creator = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ExternalKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ExternalOrigin = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ExternalSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ExternalUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    GitBranch = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Identifier = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    recurrence = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<double>(type: "REAL", nullable: true),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    thread_binding = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    WorkflowId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    WorktreeBranch = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    WorktreePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    labels = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowNodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Config = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PositionX = table.Column<double>(type: "REAL", nullable: false),
                    PositionY = table.Column<double>(type: "REAL", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowNodes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CommentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Filename = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    author = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ThreadId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskActivities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    actor = table.Column<string>(type: "TEXT", nullable: false),
                    Changes = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskActivities_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    relation_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceTaskId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TargetTaskId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskRelations_SourceTask",
                        column: x => x.SourceTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskRelations_TargetTask",
                        column: x => x.TargetTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowSequences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceNodeId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TargetNodeId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowSequences_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowSequences_SourceNode",
                        column: x => x.SourceNodeId,
                        principalTable: "WorkflowNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkflowSequences_TargetNode",
                        column: x => x.TargetNodeId,
                        principalTable: "WorkflowNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiChatThreads_OriginProjectId",
                table: "AiChatThreads",
                column: "OriginProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Task_Created",
                table: "Attachments",
                columns: new[] { "TaskId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_Task_Created",
                table: "Comments",
                columns: new[] { "TaskId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivities_Task_Created",
                table: "TaskActivities",
                columns: new[] { "TaskId", "Timestamp", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRelations_SourceTaskId",
                table: "TaskRelations",
                column: "SourceTaskId");

            migrationBuilder.CreateIndex(
                name: "UIX_TaskRelations_Parent",
                table: "TaskRelations",
                column: "TargetTaskId",
                unique: true,
                filter: "relation_type = 'parent'");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Project_Status_Sort",
                table: "Tasks",
                columns: new[] { "ProjectId", "ArchivedAt", "Status", "SortOrder", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UIX_Tasks_External",
                table: "Tasks",
                columns: new[] { "ExternalSource", "ExternalOrigin", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UIX_Tasks_Identifier",
                table: "Tasks",
                column: "Identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNodes_ProjectId",
                table: "WorkflowNodes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSequences_ProjectId",
                table: "WorkflowSequences",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSequences_SourceNodeId",
                table: "WorkflowSequences",
                column: "SourceNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSequences_TargetNodeId",
                table: "WorkflowSequences",
                column: "TargetNodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_AiChatThreads_Projects_OriginProjectId",
                table: "AiChatThreads",
                column: "OriginProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowWorkspaces_Projects_Id",
                table: "WorkflowWorkspaces",
                column: "Id",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
