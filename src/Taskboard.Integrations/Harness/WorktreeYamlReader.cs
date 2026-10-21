using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Harness;
using YamlDotNet.RepresentationModel;

namespace Taskboard.Integrations.Harness;

/// <summary>
/// SPEC-20261021-worktree-setup-scripts RF-001: single reader for
/// <c>.harness/worktree.yaml</c> (v2 schema) — shared paths, setup/archive
/// command lists, agent startup policy, default tabs and the issue-command
/// template. The file is trusted repo content (same trust level as its
/// code): malformed yaml degrades per-field, never throws.
/// </summary>
public sealed class WorktreeYamlReader : IWorktreeYamlReader
{
    /// <summary>Repo-level declaration file inside <c>.harness/</c>.</summary>
    internal const string RepoYamlFileName = ".harness/worktree.yaml";

    /// <summary>RF limits — mirrors Orca <c>orca.yaml</c> bounds.</summary>
    internal const int MaxCommands = 20;
    internal const int MaxCommandLength = 2000;
    internal const int MaxTabs = 10;
    internal const int MaxIssueCommandLength = 4000;
    internal const long MaxFileBytes = 256 * 1024;

    private const string ConfigSetupSection = "Taskboard:Worktree:Setup";
    private const string ConfigPolicyKey = "Taskboard:Worktree:SetupPolicy";

    private readonly IConfiguration _configuration;
    private readonly ILogger<WorktreeYamlReader>? _logger;

    public WorktreeYamlReader(
        IConfiguration configuration,
        ILogger<WorktreeYamlReader>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public WorktreeYamlDoc Read(string repositoryPath) =>
        Parse(repositoryPath, _logger);

    /// <inheritdoc />
    public IReadOnlyList<string> EffectiveSetupCommands(
        string repositoryPath, WorktreeYamlDoc doc)
    {
        var config = ReadConfigCommands();
        return config.Count > 0 ? config : doc.Setup;
    }

    /// <inheritdoc />
    public string EffectiveStartupPolicy(
        string repositoryPath, WorktreeYamlDoc doc)
    {
        var configured = _configuration[ConfigPolicyKey];
        if (WorktreeSetupPolicies.IsValid(configured ?? string.Empty))
        {
            return configured!;
        }

        return WorktreeSetupPolicies.IsValid(doc.AgentStartupPolicy ?? string.Empty)
            ? doc.AgentStartupPolicy!
            : WorktreeSetupPolicies.WaitForSetup;
    }

    /// <summary><c>Taskboard:Worktree:Setup</c> — scalar or
    /// <c>{command}</c> children, limits enforced.</summary>
    private List<string> ReadConfigCommands()
    {
        var commands = new List<string>();
        foreach (var child in _configuration.GetSection(ConfigSetupSection).GetChildren())
        {
            var raw = child["command"] ?? child.Value;
            if (SanitizeCommand(raw) is { } cmd)
            {
                commands.Add(cmd);
            }
        }

        return commands;
    }

    /// <summary>Parses the yaml once — every consumer (provisioner, setup
    /// runner, tabs, issue command) shares the same doc.</summary>
    internal static WorktreeYamlDoc Parse(string repositoryPath, ILogger? logger = null)
    {
        var empty = new WorktreeYamlDoc([], [], [], [], null, [], null);
        var file = Path.Join(repositoryPath, RepoYamlFileName);
        if (!File.Exists(file))
        {
            return empty;
        }

        YamlNode root;
        try
        {
            if (new FileInfo(file).Length > MaxFileBytes)
            {
                logger?.LogWarning(
                    "{File} exceeds {Max} bytes — skipping worktree yaml.", file, MaxFileBytes);
                return empty;
            }

            var stream = new YamlStream();
            using (var reader = File.OpenText(file))
            {
                stream.Load(reader);
            }

            root = stream.Documents.Count == 0
                ? new YamlMappingNode()
                : stream.Documents[0].RootNode;
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or IOException)
        {
            logger?.LogWarning(
                ex, "Could not parse {File} — skipping worktree yaml.", file);
            return empty;
        }

        if (root is not YamlMappingNode map)
        {
            return empty;
        }

        return new WorktreeYamlDoc(
            Shared: ReadShared(map),
            Exclude: ReadScalarList(map, "exclude", int.MaxValue),
            Setup: ReadCommandList(map, "setup", logger),
            Archive: ReadCommandList(map, "archive", logger),
            AgentStartupPolicy: ScalarOf(map, "agentStartupPolicy"),
            DefaultTabs: ReadTabs(map),
            IssueCommand: ReadIssueCommand(map));
    }

    private static List<WorktreeYamlSharedEntry> ReadShared(YamlMappingNode map)
    {
        var shared = new List<WorktreeYamlSharedEntry>();
        foreach (var node in SequenceOf(map, "shared"))
        {
            var (raw, mode) = node switch
            {
                YamlScalarNode scalar => (scalar.Value, null),
                YamlMappingNode m => (ScalarOf(m, "path"), ScalarOf(m, "mode")),
                _ => (null, null),
            };
            if (raw is null || Normalize(raw) is not { } path)
            {
                continue;
            }

            var invalid = Path.IsPathRooted(raw)
                || path.Split('/').Any(s => s == "..");
            shared.Add(new WorktreeYamlSharedEntry(path, mode, invalid));
        }

        return shared;
    }

    private static List<string> ReadScalarList(
        YamlMappingNode map, string key, int cap)
    {
        var items = new List<string>();
        foreach (var node in SequenceOf(map, "exclude"))
        {
            if (node is YamlScalarNode scalar
                && scalar.Value is not null
                && Normalize(scalar.Value) is { } path)
            {
                items.Add(path);
            }
        }

        return items.Count <= cap ? items : items.Take(cap).ToList();
    }

    private static List<string> ReadCommandList(
        YamlMappingNode map, string key, ILogger? logger)
    {
        var commands = new List<string>();
        var index = 0;
        foreach (var node in SequenceOf(map, key))
        {
            index++;
            var raw = node switch
            {
                YamlScalarNode scalar => scalar.Value,
                YamlMappingNode m => ScalarOf(m, "command"),
                _ => null,
            };
            if (commands.Count >= MaxCommands)
            {
                logger?.LogWarning(
                    "worktree.yaml {Key}: only the first {Max} commands apply.",
                    key, MaxCommands);
                break;
            }

            if (SanitizeCommand(raw) is not { } cmd)
            {
                logger?.LogWarning(
                    "worktree.yaml {Key}[{Index}] ignored — empty or >{Max} chars.",
                    key, index, MaxCommandLength);
                continue;
            }

            commands.Add(cmd);
        }

        return commands;
    }

    private static List<WorktreeYamlTab> ReadTabs(YamlMappingNode map)
    {
        var tabs = new List<WorktreeYamlTab>();
        foreach (var node in SequenceOf(map, "defaultTabs"))
        {
            if (tabs.Count >= MaxTabs)
            {
                break;
            }

            if (node is not YamlMappingNode m
                || ScalarOf(m, "name") is not { Length: > 0 } name
                || SanitizeCommand(ScalarOf(m, "command")) is not { } command)
            {
                continue;
            }

            tabs.Add(new WorktreeYamlTab(name.Trim(), command));
        }

        return tabs;
    }

    private static string? ReadIssueCommand(YamlMappingNode map)
    {
        var raw = ScalarOf(map, "issueCommand")?.Trim();
        return raw is { Length: > 0 and <= MaxIssueCommandLength } ? raw : null;
    }

    /// <summary>Trims a command string and enforces the length cap; rejects
    /// anything that is not a plain command line (no newlines — each entry
    /// is executed as a single <c>bash -lc</c> invocation).</summary>
    internal static string? SanitizeCommand(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || trimmed!.Length > MaxCommandLength
            || trimmed.Contains('\n') || trimmed.Contains('\r'))
        {
            return null;
        }

        return trimmed;
    }

    private static IEnumerable<YamlNode> SequenceOf(
        YamlMappingNode map, string key)
    {
        var keyNode = new YamlScalarNode(key);
        return map.Children.TryGetValue(keyNode, out var node)
            && node is YamlSequenceNode seq
                ? seq.Children
                : Enumerable.Empty<YamlNode>();
    }

    private static string? ScalarOf(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node)
            && node is YamlScalarNode scalar
                ? scalar.Value
                : null;

    private static string? Normalize(string raw)
    {
        var trimmed = raw.Trim().Replace('\\', '/').Trim('/');
        return trimmed.Length == 0 ? null : trimmed;
    }
}
