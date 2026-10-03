using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Shouldly;
using Taskboard.Mcp.Services;
using Taskboard.Mcp.Tools;
using Xunit;

namespace Taskboard.Tests.Unit.Mcp;

public class TaskboardToolsTests
{
    private sealed class StubClient : ITaskboardApiClient
    {
        public List<(string Method, string Path)> Calls { get; } = [];
        public JsonNode? GetResult { get; set; } = new JsonObject();
        public JsonNode? PutResult { get; set; } = new JsonObject();
        public object? LastPutPayload { get; private set; }

        public Task<JsonNode?> GetAsync(string path, CancellationToken ct = default)
        {
            Calls.Add(("GET", path));
            return Task.FromResult(GetResult);
        }

        public Task<JsonNode?> PostAsync(string path, object? payload, CancellationToken ct = default)
        {
            Calls.Add(("POST", path));
            return Task.FromResult<JsonNode?>(new JsonObject());
        }

        public Task<JsonNode?> PutAsync(string path, object? payload, CancellationToken ct = default)
        {
            Calls.Add(("PUT", path));
            LastPutPayload = payload;
            return Task.FromResult(PutResult);
        }

        public Task<JsonNode?> PatchAsync(string path, object? payload, CancellationToken ct = default) =>
            Task.FromResult<JsonNode?>(new JsonObject());

        public Task DeleteAsync(string path, CancellationToken ct = default) => Task.CompletedTask;

        public Task<JsonNode?> PostMultipartAsync(string path, MultipartFormDataContent content, CancellationToken ct = default) =>
            Task.FromResult<JsonNode?>(new JsonObject());
    }

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    [Fact]
    public async Task Dado_board_populado_Quando_list_issues_Entao_retorna_issues()
    {
        var client = new StubClient
        {
            GetResult = JsonNode.Parse("""{"issues":[{"number":1,"title":"Bug"}]}"""),
        };

        var result = await TaskboardTools.ListIssuesAsync(client, "afonsoft", "agent-harness");

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldContain("\"number\":1");
        client.Calls.Single().Path.ShouldBe("/api/github/repos/afonsoft/agent-harness/issues");
    }

    [Fact]
    public async Task Dado_owner_vazio_Quando_list_issues_Entao_erro_sem_chamada_http()
    {
        var client = new StubClient();

        var result = await TaskboardTools.ListIssuesAsync(client, " ", "agent-harness");

        result.IsError.ShouldBe(true);
        client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_board_vazio_Quando_list_issues_Entao_retorna_array_vazio()
    {
        var client = new StubClient { GetResult = JsonNode.Parse("""{"issues":[]}""") };

        var result = await TaskboardTools.ListIssuesAsync(client, "afonsoft", "agent-harness");

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldBe("[]");
    }

    [Fact]
    public async Task Dado_issue_valida_Quando_get_issue_Entao_retorna_detalhe()
    {
        var client = new StubClient
        {
            GetResult = JsonNode.Parse("""{"issue":{"number":42,"title":"X"}}"""),
        };

        var result = await TaskboardTools.GetIssueAsync(client, "afonsoft", "agent-harness", 42);

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldContain("\"number\":42");
        client.Calls.Single().Path.ShouldBe("/api/github/repos/afonsoft/agent-harness/issues/42");
    }

    [Fact]
    public async Task Dado_coluna_valida_Quando_move_issue_Entao_chama_put_column_com_enum()
    {
        var client = new StubClient
        {
            PutResult = JsonNode.Parse("""{"issue":{"number":7}}"""),
        };

        var result = await TaskboardTools.MoveIssueAsync(client, "afonsoft", "agent-harness", 7, "in_progress", "todo");

        result.IsError.ShouldNotBe(true);
        var call = client.Calls.Single();
        call.Method.ShouldBe("PUT");
        call.Path.ShouldBe("/api/github/repos/afonsoft/agent-harness/issues/7/column");
        var payload = client.LastPutPayload.ShouldBeOfType<Taskboard.Requests.UpdateGitHubIssueColumnRequest>();
        payload.NewColumn.ShouldBe(Taskboard.GitHub.GitHubBoardColumn.InProgress);
        payload.OldColumn.ShouldBe(Taskboard.GitHub.GitHubBoardColumn.Todo);
    }

    [Fact]
    public async Task Dado_coluna_invalida_Quando_move_issue_Entao_erro_sem_chamada_http()
    {
        var client = new StubClient();

        var result = await TaskboardTools.MoveIssueAsync(client, "afonsoft", "agent-harness", 7, "flying");

        result.IsError.ShouldBe(true);
        Text(result).ShouldContain("in-progress");
        client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_spec_valido_Quando_get_spec_Entao_chama_api_specs()
    {
        var client = new StubClient
        {
            GetResult = JsonNode.Parse("""{"id":"SPEC-1","status":"Done"}"""),
        };

        var result = await TaskboardTools.GetSpecAsync(client, "SPEC-1", repo: "owner/repo");

        result.IsError.ShouldNotBe(true);
        client.Calls.Single().Path.ShouldBe("/api/specs/SPEC-1?repo=owner%2Frepo");
    }

    [Fact]
    public async Task Dado_path_traversal_Quando_get_spec_Entao_rejeita_sem_chamada_http()
    {
        var client = new StubClient();

        var result = await TaskboardTools.GetSpecAsync(client, "../../etc/passwd");

        result.IsError.ShouldBe(true);
        client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_filtros_Quando_list_specs_Entao_constroi_query()
    {
        var client = new StubClient { GetResult = new JsonArray() };

        var result = await TaskboardTools.ListSpecsAsync(client, status: "Draft", q: "backup", repo: "o/r");

        result.IsError.ShouldNotBe(true);
        client.Calls.Single().Path.ShouldBe("/api/specs?status=Draft&q=backup&repo=o%2Fr");
    }

    [Fact]
    public async Task Dado_jobs_ativos_Quando_list_jobs_Entao_retorna_status()
    {
        var client = new StubClient
        {
            GetResult = JsonNode.Parse("""[{"key":"spec-drift","running":false}]"""),
        };

        var result = await TaskboardTools.ListJobsAsync(client);

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldContain("spec-drift");
        client.Calls.Single().Path.ShouldBe("/api/jobs");
    }

    [Fact]
    public async Task Dado_run_id_Quando_get_run_status_Entao_chama_endpoint_do_run()
    {
        var client = new StubClient
        {
            GetResult = JsonNode.Parse("""{"execution":{"id":"run-1","state":"running"}}"""),
        };

        var result = await TaskboardTools.GetRunStatusAsync(client, "run-1");

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldContain("run-1");
        client.Calls.Single().Path.ShouldBe("/api/harness/runs/run-1");
    }

    [Fact]
    public async Task Dado_run_id_vazio_Quando_get_run_status_Entao_erro_sem_chamada_http()
    {
        var client = new StubClient();

        var result = await TaskboardTools.GetRunStatusAsync(client, "");

        result.IsError.ShouldBe(true);
        client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_api_falha_Quando_list_issues_Entao_erro_isolado()
    {
        var client = new StubClient { GetResult = null };

        var result = await TaskboardTools.ListIssuesAsync(client, "afonsoft", "agent-harness");

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldBe("[]");
    }
}
