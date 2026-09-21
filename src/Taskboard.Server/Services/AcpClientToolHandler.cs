using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskboard.Domain.Entities;
using Taskboard.Integrations.Agents;
using Taskboard.Integrations.Workspace;
using Taskboard.Repositories;
using Taskboard.ValueObjects;

namespace Taskboard.Server.Services;

/// <summary>
/// Client-side implementation of the ACP v1 agent→client surface
/// (SPEC-20260921-acp-v1-conformance RF-008/RF-009): fs/read_text_file,
/// fs/write_text_file and terminal/* — every path is sandboxed to the session
/// workspace and every write/exec goes through the PermissionGate so the user
/// sees and approves what the agent does on the server.
/// </summary>
public sealed class AcpClientToolHandler : IAcpClientToolHandler
{
    private const int DefaultOutputByteLimit = 1024 * 1024;

    private sealed class TerminalEntry
    {
        public required string TerminalId { get; init; }
        public required Process Process { get; init; }
        public required StringBuilder Output { get; init; }
        public required int ByteLimit { get; init; }
        public bool Truncated { get; set; }
        public TaskCompletionSource Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkspaceService _workspace;
    private readonly PermissionGate _permissionGate;
    private readonly AcpSessionOptions _options;
    private readonly ILogger<AcpClientToolHandler> _logger;
    private readonly ConcurrentDictionary<string, TerminalEntry> _terminals = new();

    public AcpClientToolHandler(
        IServiceScopeFactory scopeFactory,
        WorkspaceService workspace,
        PermissionGate permissionGate,
        AcpSessionOptions options,
        ILogger<AcpClientToolHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _workspace = workspace;
        _permissionGate = permissionGate;
        _options = options;
        _logger = logger;
    }

    public async Task<JsonElement> HandleAsync(
        string threadId, string sessionId, string method, JsonElement p, CancellationToken cancellationToken)
    {
        var root = await ResolveWorkspaceRootAsync(threadId, cancellationToken).ConfigureAwait(false);

        return method switch
        {
            "fs/read_text_file" when _options.ClientFs => await ReadTextFile(root, p),
            "fs/write_text_file" when _options.ClientFs => await WriteTextFile(threadId, root, p),
            "terminal/create" when _options.ClientTerminal => await TerminalCreate(threadId, sessionId, root, p),
            "terminal/output" when _options.ClientTerminal => TerminalOutput(p),
            "terminal/wait_for_exit" when _options.ClientTerminal => await TerminalWaitForExit(p),
            "terminal/kill" when _options.ClientTerminal => TerminalKill(p),
            "terminal/release" when _options.ClientTerminal => TerminalRelease(p),
            _ => throw new AcpException(AcpErrorCode.MethodNotFound, method,
                $"Method '{method}' not supported by this client."),
        };
    }

    private async Task<string> ResolveWorkspaceRootAsync(string threadId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var threadRepo = scope.ServiceProvider.GetRequiredService<IRepository<AiChatThread>>();
        var thread = await threadRepo.GetAsync(AiChatThreadId.From(threadId), ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(thread?.WorkspacePath))
        {
            return thread.WorkspacePath;
        }

        if (!string.IsNullOrWhiteSpace(thread?.RepositoryFullName))
        {
            return _workspace.ResolveCardWorkdir(thread.RepositoryFullName, out _);
        }

        return _workspace.EnsureRoot();
    }

    /// <summary>RF-008: paths must resolve inside the session workspace.</summary>
    private static string SandboxPath(string root, string path)
    {
        if (!Path.IsPathRooted(path))
        {
            throw new AcpException(AcpErrorCode.InvalidParams, "fs", "path must be absolute.");
        }

        var full = Path.GetFullPath(path);
        var rootFull = Path.GetFullPath(root);
        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal) && !string.Equals(full, rootFull, StringComparison.Ordinal))
        {
            throw new AcpException(AcpErrorCode.InvalidParams, "fs", $"path escapes session workspace: {path}");
        }

        return full;
    }

    private Task<JsonElement> ReadTextFile(string root, JsonElement p)
    {
        var path = SandboxPath(root, Required(p, "path"));
        if (!File.Exists(path))
        {
            throw new AcpException(AcpErrorCode.InvalidParams, "fs/read_text_file", $"file not found: {path}");
        }

        var line = OptInt(p, "line");   // 1-based
        var limit = OptInt(p, "limit");
        var content = File.ReadAllText(path);

        if (line is not null || limit is not null)
        {
            var lines = content.Split('\n');
            var start = Math.Max(0, (line ?? 1) - 1);
            var slice = lines.Skip(start);
            if (limit is { } l)
            {
                slice = slice.Take(l);
            }

            content = string.Join('\n', slice);
        }

        return Task.FromResult(JsonSerializer.SerializeToElement(new { content }));
    }

    private async Task<JsonElement> WriteTextFile(string threadId, string root, JsonElement p)
    {
        var path = SandboxPath(root, Required(p, "path"));
        var content = Required(p, "content");

        var outcome = await _permissionGate.RequestPermissionAsync(
            threadId,
            "fs/write_text_file",
            $"{path} ({content.Length} chars)",
            ["allow", "deny"]).ConfigureAwait(false);

        if (!string.Equals(outcome, "allow", StringComparison.OrdinalIgnoreCase))
        {
            throw new AcpException(AcpErrorCode.RequestCancelled, "fs/write_text_file", "write denied by user.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
        _logger.LogInformation("ACP fs/write_text_file: {Path} ({Length} chars) approved for thread {ThreadId}.",
            path, content.Length, threadId);
        return JsonSerializer.SerializeToElement(new { });
    }

    private async Task<JsonElement> TerminalCreate(string threadId, string sessionId, string root, JsonElement p)
    {
        var command = Required(p, "command");
        var args = p.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray()
            : [];
        var cwd = p.TryGetProperty("cwd", out var c) && c.GetString() is { } cw
            ? SandboxPath(root, cw)
            : Path.GetFullPath(root);
        var byteLimit = OptInt(p, "outputByteLimit") ?? DefaultOutputByteLimit;

        var outcome = await _permissionGate.RequestPermissionAsync(
            threadId,
            "terminal/create",
            $"{command} {string.Join(' ', args)} (cwd: {cwd})",
            ["allow", "deny"]).ConfigureAwait(false);

        if (!string.Equals(outcome, "allow", StringComparison.OrdinalIgnoreCase))
        {
            throw new AcpException(AcpErrorCode.RequestCancelled, "terminal/create", "command denied by user.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (p.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in env.EnumerateArray())
            {
                var name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
                var value = e.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (name is not null)
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        var process = Process.Start(startInfo)
            ?? throw new AcpException(AcpErrorCode.Internal, "terminal/create", $"failed to start '{command}'.");

        var entry = new TerminalEntry
        {
            TerminalId = $"term_{Guid.NewGuid():N}",
            Process = process,
            Output = new StringBuilder(),
            ByteLimit = byteLimit
        };
        _terminals[entry.TerminalId] = entry;

        void Append(string? data)
        {
            if (data is null)
            {
                return;
            }

            lock (entry.Output)
            {
                entry.Output.AppendLine(data);
                if (entry.Output.Length > entry.ByteLimit)
                {
                    entry.Output.Remove(0, entry.Output.Length - entry.ByteLimit);
                    entry.Truncated = true;
                }
            }
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => entry.Exit.TrySetResult();

        _logger.LogInformation("ACP terminal/create: '{Command}' approved for thread {ThreadId} → {TerminalId}.",
            command, threadId, entry.TerminalId);
        return JsonSerializer.SerializeToElement(new { terminalId = entry.TerminalId });
    }

    private JsonElement TerminalOutput(JsonElement p)
    {
        var entry = GetTerminal(p);
        string output;
        lock (entry.Output)
        {
            output = entry.Output.ToString();
        }

        object? exitStatus = entry.Process.HasExited
            ? new { exitCode = (int?)entry.Process.ExitCode, signal = (string?)null }
            : null;
        return JsonSerializer.SerializeToElement(new { output, truncated = entry.Truncated, exitStatus });
    }

    private async Task<JsonElement> TerminalWaitForExit(JsonElement p)
    {
        var entry = GetTerminal(p);
        await entry.Exit.Task.ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(new { exitCode = (int?)entry.Process.ExitCode, signal = (string?)null });
    }

    private JsonElement TerminalKill(JsonElement p)
    {
        var entry = GetTerminal(p);
        try
        {
            if (!entry.Process.HasExited)
            {
                entry.Process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "terminal/kill on already-dead process.");
        }

        return JsonSerializer.SerializeToElement(new { });
    }

    private JsonElement TerminalRelease(JsonElement p)
    {
        var entry = GetTerminal(p);
        try
        {
            if (!entry.Process.HasExited)
            {
                entry.Process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "terminal/release kill failed.");
        }

        entry.Process.Dispose();
        _terminals.TryRemove(entry.TerminalId, out _);
        return JsonSerializer.SerializeToElement(new { });
    }

    private TerminalEntry GetTerminal(JsonElement p)
    {
        var id = Required(p, "terminalId");
        return _terminals.TryGetValue(id, out var entry)
            ? entry
            : throw new AcpException(AcpErrorCode.InvalidParams, "terminal", $"unknown terminalId '{id}'.");
    }

    private static string Required(JsonElement p, string name) =>
        p.TryGetProperty(name, out var el) && el.GetString() is { } s
            ? s
            : throw new AcpException(AcpErrorCode.InvalidParams, "acp", $"missing required param '{name}'.");

    private static int? OptInt(JsonElement p, string name) =>
        p.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;
}
