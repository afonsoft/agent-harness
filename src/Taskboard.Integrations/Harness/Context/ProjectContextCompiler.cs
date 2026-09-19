using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Taskboard;
using Taskboard.Agents;
using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Harness.Context;

/// <summary>
/// <see cref="IContextCompiler"/> — assembles the agent system prompt from
/// instruction files, environment/git metadata, the skills catalog and
/// cross-session memories (SPEC-20260919-harness-context-memory RF-001/002/004).
/// </summary>
public sealed class ProjectContextCompiler : IContextCompiler
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(15);
    private const int MaxFileLines = 500;
    private const int MaxRuleFiles = 10;
    private const int MaxSkillEntries = 25;
    private const int MaxMemories = 10;

    /// <summary>Root-level instruction files, in priority order.</summary>
    private static readonly string[] InstructionFiles =
    [
        "AGENTS.md",
        "CLAUDE.md",
        ".cursorrules",
        ".github/copilot-instructions.md"
    ];

    private readonly IGitCommandRunner _git;
    private readonly IMemoryService? _memory;
    private readonly ILogger<ProjectContextCompiler> _logger;

    public ProjectContextCompiler(
        IGitCommandRunner git,
        IMemoryService? memory,
        ILogger<ProjectContextCompiler> logger)
    {
        _git = git;
        _memory = memory;
        _logger = logger;
    }

    public async Task<ContextCompilationDto> CompileAsync(
        string worktreePath,
        AgentType agentType,
        int maxTokenBudget,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(worktreePath) || !Directory.Exists(worktreePath))
        {
            throw new DomainException(
                TaskboardDomainErrorCodes.InvalidValue,
                $"Worktree path '{worktreePath}' does not exist.");
        }

        var injectedFiles = new List<string>();
        var prompt = new StringBuilder();
        var seenContents = new HashSet<string>(StringComparer.Ordinal);

        // RF-001: hierarquia de instruções — arquivos de raiz primeiro.
        foreach (var relative in InstructionFiles)
        {
            var fullPath = Path.Combine(worktreePath, relative);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var content = ReadBounded(fullPath);
            injectedFiles.Add(relative);

            // RF-001 rule: identical AGENTS.md/CLAUDE.md content is injected once.
            if (!seenContents.Add(content.Trim()))
            {
                continue;
            }

            prompt.AppendLine($"<!-- {relative} -->");
            prompt.AppendLine(content);
            prompt.AppendLine();
        }

        // Always-on rules directory (.claude/rules/*.md).
        var rulesDir = Path.Combine(worktreePath, ".claude", "rules");
        if (Directory.Exists(rulesDir))
        {
            foreach (var file in Directory.GetFiles(rulesDir, "*.md").OrderBy(f => f).Take(MaxRuleFiles))
            {
                var relative = Path.GetRelativePath(worktreePath, file);
                var content = ReadBounded(file);
                injectedFiles.Add(relative);
                if (!seenContents.Add(content.Trim()))
                {
                    continue;
                }

                prompt.AppendLine($"<!-- {relative} -->");
                prompt.AppendLine(content);
                prompt.AppendLine();
            }
        }

        // RF-002: bloco <env> com estado real do worktree.
        prompt.AppendLine(await BuildEnvBlockAsync(worktreePath, agentType, cancellationToken));
        prompt.AppendLine();

        var budgetTokens = (int)(maxTokenBudget * 0.8);

        // Catálogo sumarizado de skills (droppable sob pressão de budget).
        var skillsSection = BuildSkillsCatalog(worktreePath);
        if (skillsSection.Length > 0 && EstimateTokens(prompt.Length + skillsSection.Length) <= budgetTokens)
        {
            prompt.Append(skillsSection);
            prompt.AppendLine();
        }

        // RF-004: memórias cross-session do repositório.
        var memoriesInjected = 0;
        var memoriesSection = await BuildMemoryBlockAsync(worktreePath, cancellationToken);
        if (memoriesSection.Section.Length > 0
            && EstimateTokens(prompt.Length + memoriesSection.Section.Length) <= budgetTokens)
        {
            prompt.Append(memoriesSection.Section);
            memoriesInjected = memoriesSection.Count;
        }

        var systemPrompt = prompt.ToString().TrimEnd();
        return new ContextCompilationDto(
            systemPrompt,
            EstimateTokens(systemPrompt.Length),
            injectedFiles,
            memoriesInjected);
    }

    private async Task<string> BuildEnvBlockAsync(
        string worktreePath,
        AgentType agentType,
        CancellationToken cancellationToken)
    {
        var branch = await TryGitAsync(worktreePath, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken) ?? "unknown";
        var commit = await TryGitAsync(worktreePath, ["rev-parse", "--short", "HEAD"], cancellationToken) ?? "unknown";
        var status = await TryGitAsync(worktreePath, ["status", "--porcelain"], cancellationToken);
        var dirty = status is not null && status.Length > 0;

        var env = new StringBuilder();
        env.AppendLine("<env>");
        env.AppendLine($"  Working directory: {worktreePath}");
        env.AppendLine($"  Agent: {agentType}");
        env.AppendLine($"  Git branch: {branch}");
        env.AppendLine($"  Base commit: {commit}");
        env.AppendLine($"  Working tree: {(dirty ? "dirty" : "clean")}");
        env.AppendLine($"  Runtime: {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}, .NET {Environment.Version}");
        env.AppendLine($"  Date: {DateTimeOffset.UtcNow:yyyy-MM-dd}");
        env.Append("</env>");
        return env.ToString();
    }

    private static string BuildSkillsCatalog(string worktreePath)
    {
        var skillsDir = Path.Combine(worktreePath, ".claude", "skills");
        if (!Directory.Exists(skillsDir))
        {
            return string.Empty;
        }

        var catalog = new StringBuilder();
        var count = 0;
        foreach (var dir in Directory.GetDirectories(skillsDir).OrderBy(d => d))
        {
            var skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile) || count >= MaxSkillEntries)
            {
                continue;
            }

            var (name, description) = ReadFrontmatter(skillFile);
            if (name is null)
            {
                continue;
            }

            catalog.AppendLine($"- **{name}** — {description ?? "no description"}");
            count++;
        }

        if (count == 0)
        {
            return string.Empty;
        }

        var section = new StringBuilder();
        section.AppendLine("<skills>");
        section.Append(catalog);
        section.Append("</skills>");
        return section.ToString();
    }

    private async Task<(string Section, int Count)> BuildMemoryBlockAsync(
        string worktreePath,
        CancellationToken cancellationToken)
    {
        if (_memory is null)
        {
            return (string.Empty, 0);
        }

        var repositoryFullName = await ResolveRepositoryFullNameAsync(worktreePath, cancellationToken);
        if (repositoryFullName is null)
        {
            return (string.Empty, 0);
        }

        IReadOnlyList<ProjectMemoryItemDto> memories;
        try
        {
            memories = await _memory.SearchAsync(repositoryFullName, string.Empty, MaxMemories, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory lookup failed for {Repo}; compiling without memories.", repositoryFullName);
            return (string.Empty, 0);
        }

        if (memories.Count == 0)
        {
            return (string.Empty, 0);
        }

        var section = new StringBuilder();
        section.AppendLine("<project_memory>");
        foreach (var memory in memories)
        {
            var tags = memory.Tags.Count > 0 ? $" [{string.Join(", ", memory.Tags)}]" : string.Empty;
            section.AppendLine($"- ({memory.Type}) {memory.Topic}: {memory.Content}{tags}");
        }

        section.Append("</project_memory>");
        return (section.ToString(), memories.Count);
    }

    /// <summary>owner/name from the git remote URL (https or ssh form).</summary>
    private async Task<string?> ResolveRepositoryFullNameAsync(string worktreePath, CancellationToken cancellationToken)
    {
        var remote = await TryGitAsync(worktreePath, ["remote", "get-url", "origin"], cancellationToken);
        if (string.IsNullOrWhiteSpace(remote))
        {
            return null;
        }

        var trimmed = remote.Trim().TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        var marker = trimmed.Contains(':') && !trimmed.Contains("://", StringComparison.Ordinal)
            ? trimmed.Split(':', 2)[1]
            : trimmed;
        var segments = marker.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? $"{segments[^2]}/{segments[^1]}" : null;
    }

    private async Task<string?> TryGitAsync(
        string worktreePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _git.RunAsync(worktreePath, arguments, GitTimeout, cancellationToken);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "git {Args} failed in {Path}.", string.Join(' ', arguments), worktreePath);
            return null;
        }
    }

    private static string ReadBounded(string path)
    {
        var lines = File.ReadLines(path).Take(MaxFileLines).ToList();
        var content = string.Join('\n', lines);
        return lines.Count == MaxFileLines ? content + "\n<!-- truncated -->" : content;
    }

    /// <summary>YAML frontmatter <c>name</c>/<c>description</c> extraction.</summary>
    private static (string? Name, string? Description) ReadFrontmatter(string path)
    {
        string? name = null;
        string? description = null;
        var inFrontmatter = false;
        foreach (var line in File.ReadLines(path).Take(60))
        {
            var trimmed = line.Trim();
            if (trimmed == "---")
            {
                if (inFrontmatter)
                {
                    break;
                }

                inFrontmatter = true;
                continue;
            }

            if (!inFrontmatter)
            {
                continue;
            }

            if (trimmed.StartsWith("name:", StringComparison.Ordinal))
            {
                name = trimmed[5..].Trim();
            }
            else if (trimmed.StartsWith("description:", StringComparison.Ordinal))
            {
                description = trimmed[12..].Trim();
            }
        }

        return (name, description);
    }

    /// <summary>~4 chars/token heuristic — enough for budget gating without a tokenizer dep.</summary>
    internal static int EstimateTokens(int chars) => Math.Max(1, chars / 4);
}
