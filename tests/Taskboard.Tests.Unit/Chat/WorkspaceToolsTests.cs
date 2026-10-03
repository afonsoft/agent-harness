using System.Text.Json;
using Shouldly;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Integrations.Chat.Tools;
using Taskboard.Integrations.Harness.Security;
using Xunit;

namespace Taskboard.Tests.Unit.Chat;

/// <summary>
/// SPEC-20261003-ai-code-opencode-parity: novas tools workspace no padrão
/// OpenCode — edit_file, search_files, find_files, git (read-only),
/// run_tests e todo — todas path-jailed e scrubadas.
/// </summary>
public sealed class WorkspaceToolsTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"chat-ws-{Guid.NewGuid():N}");
    private readonly string _storeDir = Path.Combine(Path.GetTempPath(), $"chat-todo-{Guid.NewGuid():N}");

    public WorkspaceToolsTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

        if (Directory.Exists(_storeDir))
        {
            Directory.Delete(_storeDir, recursive: true);
        }
    }

    private static ChatToolContext Context(string workspace) => new(
        WorkspacePath: workspace,
        ProviderId: Guid.NewGuid(),
        ProviderBaseUrl: "http://p.test",
        ProviderApiKey: "sk-x",
        ImageModel: "",
        SearchBackend: "none",
        SearchUrl: "",
        SearchApiKey: "",
        ConversationId: "conv-1");

    private ChatToolContext WorkspaceContext() => Context(_workspace);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    // ---- edit_file ----

    [Fact]
    public async Task Dado_TextoAmbiguo_Quando_EditFile_Entao_ExigeReplaceAll()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "a.txt"), "hello world hello world");
        var tool = new EditFileTool();

        var result = await tool.ExecuteAsync(
            Args("""{"path":"a.txt","old_string":"world","new_string":"there"}"""),
            WorkspaceContext(), CancellationToken.None);

        // "world" aparece 2x — sem replace_all deve recusar por ambiguidade.
        result.Json.ShouldContain("matches 2 times");
        (await File.ReadAllTextAsync(Path.Combine(_workspace, "a.txt"))).ShouldBe("hello world hello world");
    }

    [Fact]
    public async Task Dado_TextoUnicoComContexto_Quando_EditFile_Entao_Edita()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "b.txt"), "alpha beta gamma");
        var tool = new EditFileTool();

        var result = await tool.ExecuteAsync(
            Args("""{"path":"b.txt","old_string":"beta gamma","new_string":"BETA"}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Json.ShouldContain("\"edited\":true");
        (await File.ReadAllTextAsync(Path.Combine(_workspace, "b.txt"))).ShouldBe("alpha BETA");
    }

    [Fact]
    public async Task Dado_ReplaceAll_Quando_EditFile_Entao_SubstituiTodas()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "c.txt"), "x=1; x=2;");
        var tool = new EditFileTool();

        var result = await tool.ExecuteAsync(
            Args("""{"path":"c.txt","old_string":"x","new_string":"y","replace_all":true}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Json.ShouldContain("\"replacements\":2");
        (await File.ReadAllTextAsync(Path.Combine(_workspace, "c.txt"))).ShouldBe("y=1; y=2;");
    }

    [Fact]
    public async Task Dado_PathForaDoJail_Quando_EditFile_Entao_Recusado()
    {
        var tool = new EditFileTool();

        var result = await tool.ExecuteAsync(
            Args("""{"path":"../escape.txt","old_string":"a","new_string":"b"}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    // ---- search_files ----

    [Fact]
    public async Task Dado_ArquivosNoWorkspace_Quando_SearchFiles_Entao_RetornaMatches()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "src"));
        await File.WriteAllTextAsync(Path.Combine(_workspace, "src", "a.cs"), "class Foo {}\n// TODO: fix");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "src", "b.txt"), "nothing here");
        var tool = new SearchFilesTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            Args("""{"pattern":"TODO","include":"*.cs"}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Json.ShouldContain("\"count\":1");
        result.Json.ShouldContain("a.cs");
        result.Json.Contains("b.txt", StringComparison.Ordinal).ShouldBeFalse("b.txt não é .cs");
    }

    [Fact]
    public async Task Dado_RegexInvalida_Quando_SearchFiles_Entao_Recusado()
    {
        var tool = new SearchFilesTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            Args("""{"pattern":"(["}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
        result.Json.ShouldContain("invalid regex");
    }

    [Fact]
    public async Task Dado_SegredoNaLinha_Quando_SearchFiles_Entao_Scrubado()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "secrets.txt"),
            "token = ghp_abcdefghijklmnopqrstuvwxyz0123456789AB");
        var tool = new SearchFilesTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            Args("""{"pattern":"token"}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Json.ShouldContain(SecretScrubber.Redacted);
        result.Json.Contains("ghp_", StringComparison.Ordinal).ShouldBeFalse("o token deve ser scrubado");
    }

    // ---- find_files ----

    [Fact]
    public async Task Dado_GlobSimples_Quando_FindFiles_Entao_ListaRecursivo()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "sub", "deep"));
        await File.WriteAllTextAsync(Path.Combine(_workspace, "root.cs"), "");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "sub", "deep", "leaf.cs"), "");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "sub", "note.txt"), "");
        var tool = new FindFilesTool();

        var result = await tool.ExecuteAsync(
            Args("""{"pattern":"*.cs"}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Json.ShouldContain("root.cs");
        result.Json.ShouldContain("sub/deep/leaf.cs");
        result.Json.Contains("note.txt", StringComparison.Ordinal).ShouldBeFalse("*.cs não casa .txt");
    }

    [Fact]
    public async Task Dado_GlobComChaves_Quando_FindFiles_Entao_CasaAlternativas()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "x.json"), "");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "x.yaml"), "");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "x.cs"), "");
        var tool = new FindFilesTool();

        var result = await tool.ExecuteAsync(
            Args("""{"pattern":"*.{json,yaml}"}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Json.ShouldContain("x.json");
        result.Json.ShouldContain("x.yaml");
        result.Json.Contains("x.cs", StringComparison.Ordinal).ShouldBeFalse("braces não devem casar .cs");
    }

    // ---- git ----

    [Fact]
    public async Task Dado_RepositorioGit_Quando_Status_Entao_RetornaBranch()
    {
        // O próprio checkout de teste não é o workspace — cria um repo mínimo.
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = _workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "init", "-b", "main" })
        {
            psi.ArgumentList.Add(arg);
        }

        using var init = System.Diagnostics.Process.Start(psi)!;
        await init.WaitForExitAsync();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "tracked.txt"), "new file");
        var tool = new GitTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            Args("""{"action":"status"}"""), WorkspaceContext(), CancellationToken.None);

        result.Json.ShouldContain("\"exitCode\":0");
        result.Json.ShouldContain("tracked.txt");
    }

    [Fact]
    public async Task Dado_PathEscape_Quando_GitDiff_Entao_Recusado()
    {
        var tool = new GitTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            Args("""{"action":"diff","path":"../../etc/passwd"}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    [Fact]
    public async Task Dado_AcaoInvalida_Quando_Git_Entao_Recusado()
    {
        var tool = new GitTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            Args("""{"action":"push"}"""), WorkspaceContext(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    // ---- run_tests ----

    [Fact]
    public async Task Dado_PathForaDoJail_Quando_RunTests_Entao_Recusado()
    {
        var tool = new RunTestsTool(new SecretScrubber());

        var result = await tool.ExecuteAsync(
            Args("""{"path":"../../outside.sln"}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }

    // ---- todo ----

    [Fact]
    public async Task Dado_ListaVazia_Quando_TodoList_Entao_ArrayVazio()
    {
        var tool = new TodoTool(new ChatTodoStore(_storeDir));

        var result = await tool.ExecuteAsync(
            Args("""{"action":"list"}"""), WorkspaceContext(), CancellationToken.None);

        result.Json.ShouldContain("\"items\":[]");
    }

    [Fact]
    public async Task Dado_ItemsValidos_Quando_TodoWrite_Entao_PersisteELe()
    {
        var store = new ChatTodoStore(_storeDir);
        var tool = new TodoTool(store);

        await tool.ExecuteAsync(
            Args("""{"action":"write","items":[{"content":"step one","status":"in_progress"},{"content":"step two","status":"pending"}]}"""),
            WorkspaceContext(), CancellationToken.None);
        var result = await tool.ExecuteAsync(
            Args("""{"action":"list"}"""), WorkspaceContext(), CancellationToken.None);

        result.Json.ShouldContain("step one");
        result.Json.ShouldContain("in_progress");
    }

    [Fact]
    public async Task Dado_StatusInvalido_Quando_TodoWrite_Entao_Recusado()
    {
        var tool = new TodoTool(new ChatTodoStore(_storeDir));

        var result = await tool.ExecuteAsync(
            Args("""{"action":"write","items":[{"content":"x","status":"doing"}]}"""),
            WorkspaceContext(), CancellationToken.None);

        result.Refused.ShouldBeTrue();
    }
}
