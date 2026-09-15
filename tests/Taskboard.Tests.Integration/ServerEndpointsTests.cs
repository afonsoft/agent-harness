using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;
using Taskboard.Server;

namespace Taskboard.Tests.Integration;

public class ServerEndpointsTests : IClassFixture<TaskboardWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TaskboardWebApplicationFactory _factory;

    public ServerEndpointsTests(TaskboardWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // /api requires a session since SPEC-20260915-api-authorization-hardening
    private Task<HttpClient> ApiClientAsync() => _factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task Given_NoAuth_When_GetHealth_Then_Returns200()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Given_NoAuth_When_GetSwaggerJson_Then_ReturnsOpenApi()
    {
        // Covers FR-001: Swagger JSON is reachable without authentication
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result.ShouldNotBeNull();
        result["openapi"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Given_NoAuth_When_GetRoot_Then_Returns200OrRedirect()
    {
        var response = await _client.GetAsync("/");

        // Root may serve Blazor app or redirect to login
        response.StatusCode.ShouldBeOneOf(System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Given_ExistingSkill_When_GetSkillDetail_Then_ReturnsSkillContent()
    {
        // Covers FR-003: skill detail API
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/skills/taskboard/manage-taskboard");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result.ShouldNotBeNull();
        result["skill"].ShouldNotBeNull();
        var content = result["skill"]?["content"]?.GetValue<string>();
        content.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Given_ExistingSkill_When_GetSkillDetail_Then_ReturnsFileTree()
    {
        // Covers RF-006: skill detail includes the file list
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/skills/taskboard/manage-taskboard");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        var files = result?["skill"]?["files"] as JsonArray;
        files.ShouldNotBeNull();
        var paths = files!.Select(f => f!["relativePath"]!.GetValue<string>()).ToList();
        paths.ShouldContain("SKILL.md");
        paths.ShouldContain("references/cli.md");
    }

    [Fact]
    public async Task Given_ExistingSkillFile_When_GetSkillFile_Then_ReturnsContent()
    {
        // Covers RF-007: file content endpoint returns text content
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/skills/taskboard/manage-taskboard/files/references/cli.md");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result.ShouldNotBeNull();
        result["path"]?.GetValue<string>().ShouldBe("references/cli.md");
        result["content"]?.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("/api/skills/taskboard/manage-taskboard/files/..%2Fadmin.json")]
    [InlineData("/api/skills/taskboard/manage-taskboard/files/references%2F..%2F..%2Fetc%2Fpasswd")]
    public async Task Given_TraversalPath_When_GetSkillFile_Then_Returns400(string url)
    {
        // Covers RF-007: path traversal is rejected
        var client = await ApiClientAsync();
        var response = await client.GetAsync(url);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_MissingSkillFile_When_GetSkillFile_Then_Returns404()
    {
        // Covers RF-007: missing file returns 404
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/skills/taskboard/manage-taskboard/files/does-not-exist.md");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Given_ProjectExists_When_ListProjects_Then_ReturnsProjectsObject()
    {
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/projects");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result.ShouldNotBeNull();
        result["projects"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Given_ValidProject_When_CreateProject_Then_ProjectCreated()
    {
        var client = await ApiClientAsync();
        var project = new
        {
            name = "Test Project Integration"
        };

        var response = await client.PostAsJsonAsync("/api/projects", project);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonObject>();
        created.ShouldNotBeNull();

        // Response format: { "project": { "id": "...", "name": "..." } }
        var projectObj = created["project"] as JsonObject;
        projectObj.ShouldNotBeNull();
        var name = projectObj!["name"]?.GetValue<string>();
        name.ShouldBe("Test Project Integration");
    }

    [Fact]
    public async Task Given_ProjectExists_When_CreateTask_Then_TaskCreated()
    {
        var client = await ApiClientAsync();

        // First create a project
        var createProjectResponse = await client.PostAsJsonAsync("/api/projects", new { name = "Project for Tasks" });
        createProjectResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
        var projectObj = (await createProjectResponse.Content.ReadFromJsonAsync<JsonObject>())!["project"] as JsonObject;
        var projectId = projectObj!["id"]?.GetValue<string>();

        // Create a task
        var task = new
        {
            projectId = projectId,
            title = "Integration Test Task",
            status = "todo",
            priority = "high"
        };

        var response = await client.PostAsJsonAsync("/api/tasks", task);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonObject>();
        created.ShouldNotBeNull();
        var taskObj = created["task"] as JsonObject;
        taskObj.ShouldNotBeNull();
        var title = taskObj!["title"]?.GetValue<string>();
        title.ShouldBe("Integration Test Task");
    }

    [Fact]
    public async Task Given_TaskExists_When_ListTasks_Then_ReturnsTasks()
    {
        var client = await ApiClientAsync();
        var response = await client.GetAsync("/api/tasks");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>();
        result.ShouldNotBeNull();
        result["tasks"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Given_TaskExists_When_AddComment_Then_CommentAdded()
    {
        var client = await ApiClientAsync();

        // Create project and task
        var createProjectResponse = await client.PostAsJsonAsync("/api/projects", new { name = "Comment Test" });
        createProjectResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
        var projectObj = (await createProjectResponse.Content.ReadFromJsonAsync<JsonObject>())!["project"] as JsonObject;
        var projectId = projectObj!["id"]?.GetValue<string>();

        var taskResponse = await client.PostAsJsonAsync("/api/tasks", new { projectId, title = "Task for Comment", status = "todo", priority = "medium" });
        taskResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
        var taskObj = (await taskResponse.Content.ReadFromJsonAsync<JsonObject>())!["task"] as JsonObject;
        var taskId = taskObj!["id"]?.GetValue<string>();

        // Add comment
        var comment = new { body = "Test comment from integration test" };
        var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/comments", comment);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonObject>();
        created.ShouldNotBeNull();
        var commentObj = created["comment"] as JsonObject;
        commentObj.ShouldNotBeNull();
        var body = commentObj!["body"]?.GetValue<string>();
        body.ShouldBe("Test comment from integration test");
    }

    [Fact]
    public async Task Given_AttachmentUploaded_When_GetTaskAttachments_Then_AttachmentListed()
    {
        var client = await ApiClientAsync();

        var createProjectResponse = await client.PostAsJsonAsync("/api/projects", new { name = "Attachment List Test" });
        createProjectResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
        var projectObj = (await createProjectResponse.Content.ReadFromJsonAsync<JsonObject>())!["project"] as JsonObject;
        var projectId = projectObj!["id"]?.GetValue<string>();

        var taskResponse = await client.PostAsJsonAsync("/api/tasks", new { projectId, title = "Task for Attachment", status = "todo", priority = "medium" });
        taskResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
        var taskObj = (await taskResponse.Content.ReadFromJsonAsync<JsonObject>())!["task"] as JsonObject;
        var taskId = taskObj!["id"]?.GetValue<string>();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("hello attachment"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "note.txt");
        form.Add(new StringContent(taskId!), "taskId");
        var uploadResponse = await client.PostAsync("/api/attachments", form);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        uploadResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, uploadBody);

        var listResponse = await client.GetAsync($"/api/tasks/{taskId}/attachments");

        listResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var listed = await listResponse.Content.ReadFromJsonAsync<JsonObject>();
        var attachments = listed!["attachments"] as JsonArray;
        attachments.ShouldNotBeNull();
        attachments!.Count.ShouldBe(1);
        attachments[0]!["filename"]?.GetValue<string>().ShouldBe("note.txt");
    }

    [Fact]
    public async Task Given_ServerRunning_When_GetBlazorWebJs_Then_Returns200()
    {
        // Regression: _framework/blazor.web.js must be served as a static web asset
        // (RequiresAspNetWebAssets in Taskboard.Server.csproj). Without it the
        // sidebar NavLinks are dead — see SPEC-20260914-blazor-web-assets.
        var response = await _client.GetAsync("/_framework/blazor.web.js");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Dado_RequisicaoComAcceptEncodingGzip_Quando_GetProjects_Entao_RetornaConteudoComprimido()
    {
        // Covers FR-006: response compression for dynamic responses
        var client = await ApiClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/projects");
        request.Headers.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldContain("gzip");
    }
}
