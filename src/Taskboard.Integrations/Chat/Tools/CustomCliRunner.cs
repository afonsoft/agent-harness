using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Agents;
using Taskboard.Application.Contracts.Chat;
using Taskboard.Dtos;
using Taskboard.Integrations.Agents;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Shared resolution + one-shot execution of user-declared agent CLIs
/// (<see cref="AgentCliDefinitionDto"/>) for the chat delegation tools —
/// SPEC-20261004 RF-007: an enabled def is matched by id (<c>custom-…</c>),
/// display name or executable name, resolved on PATH, then exec'd via
/// <see cref="ChatProcessRunner"/> with the rendered args template and
/// optional stdin prompt delivery.
/// </summary>
internal static class CustomCliRunner
{
    /// <summary>
    /// Finds an enabled def matching <paramref name="requested"/> by id,
    /// display name or executable name (case-insensitive); null when none.
    /// </summary>
    public static async Task<AgentCliDefinitionDto?> FindAsync(
        IServiceScopeFactory scopeFactory, string? requested, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var name = requested.Trim();
        await using var scope = scopeFactory.CreateAsyncScope();
        var defs = scope.ServiceProvider.GetRequiredService<IAgentCliDefinitionRepository>();
        var list = await defs.ListAsync(cancellationToken).ConfigureAwait(false);
        return list.FirstOrDefault(d =>
            d.Enabled
            && (string.Equals(d.Id, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Executable, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Resolves a def's executable: the absolute path when rooted, otherwise
    /// a PATH lookup; null when it can't be located.
    /// </summary>
    public static string? ResolveExecutable(string executable) =>
        Path.IsPathRooted(executable)
            ? File.Exists(executable) ? Path.GetFullPath(executable) : null
            : PathSearch.FindExecutable(executable);

    /// <summary>
    /// Renders the def's argv template for a delegated prompt: the
    /// <c>{prompt}</c>/<c>{model}</c> tokens are replaced first; when the
    /// template carries no <c>{prompt}</c> token, delivery follows
    /// <see cref="AgentCliDefinitionDto.PromptDelivery"/> — argv appends the
    /// prompt as trailing argument, stdin pipes it.
    /// </summary>
    public static (IReadOnlyList<string> Argv, string? Stdin) BuildInvocation(
        AgentCliDefinitionDto def, string prompt, string? model)
    {
        var hasPromptToken = def.ArgsTemplate.Contains("{prompt}", StringComparison.Ordinal);
        var argv = AgentCliArgsTemplate.Render(
            def.ArgsTemplate,
            def.ModelFlag,
            prompt: hasPromptToken ? prompt : null,
            model: model).ToList();

        string? stdin = null;
        if (!hasPromptToken)
        {
            if (def.PromptDelivery.Equals("stdin", StringComparison.OrdinalIgnoreCase))
            {
                stdin = prompt;
            }
            else
            {
                argv.Add(prompt);
            }
        }

        return (argv, stdin);
    }

    /// <summary>Executes the resolved binary and packages the result JSON.</summary>
    public static async Task<ChatToolResult> ExecAsync(
        string resolvedPath,
        IReadOnlyList<string> argv,
        string? stdin,
        string workingDirectory,
        ISecretRedactor redactor,
        CancellationToken cancellationToken,
        string? displayName = null)
    {
        var result = await ChatProcessRunner.RunAsync(
            resolvedPath, argv, workingDirectory,
            TimeSpan.FromSeconds(120), cancellationToken, stdin).ConfigureAwait(false);

        var output = redactor.Redact(ChatProcessRunner.Truncate(
            $"exitCode: {result.ExitCode}\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}"));
        return new ChatToolResult(JsonSerializer.Serialize(new
        {
            cli = displayName ?? Path.GetFileName(resolvedPath),
            exitCode = result.ExitCode,
            timedOut = result.TimedOut,
            output,
        }));
    }
}
