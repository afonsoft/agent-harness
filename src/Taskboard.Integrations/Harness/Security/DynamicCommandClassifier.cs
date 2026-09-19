using Taskboard.Application.Contracts.Harness;
using Taskboard.Dtos;
using Taskboard.Harness;
using Taskboard.Workspace;

namespace Taskboard.Integrations.Harness.Security;

/// <summary>
/// Lexer-based pre-fork command classifier (SPEC-20260919-harness-security-permission-gateway
/// RF-001). Splits the command line into pipeline/chain segments, resolves path
/// arguments against the worktree jail and takes the worst segment risk.
/// Fail-closed: substitutions, globs and unknown binaries classify Dangerous.
/// </summary>
public sealed class DynamicCommandClassifier : ICommandRiskClassifier
{
    // Always dangerous regardless of arguments — elevation, network egress,
    // arbitrary interpreters, environment dumps, destructive system tools.
    private static readonly HashSet<string> DangerousBinaries = new(StringComparer.Ordinal)
    {
        "sudo", "su", "doas", "chmod", "chown", "chgrp",
        "curl", "wget", "nc", "ncat", "netcat", "ssh", "scp", "sftp", "ftp", "telnet",
        "dd", "mkfs", "fdisk", "mount", "umount",
        "shutdown", "reboot", "halt", "poweroff", "kill", "killall", "pkill",
        "env", "printenv", "eval", "exec", "crontab",
        "systemctl", "service", "iptables", "useradd", "userdel", "usermod", "passwd",
        "apt", "apt-get", "yum", "dnf", "brew", "snap",
        "pip", "pip3", "gem", "npx",
        "bash", "sh", "zsh", "fish", "python", "python3", "perl", "ruby", "node",
        "docker", "podman", "kubectl", "terraform"
    };

    // Read-only commands — still jail-checked for path arguments.
    private static readonly HashSet<string> SafeBinaries = new(StringComparer.Ordinal)
    {
        "ls", "cat", "grep", "rg", "find", "pwd", "head", "tail", "wc",
        "file", "stat", "which", "whereis", "tree", "diff", "sed",
        "sort", "uniq", "tr", "cut", "awk", "jq", "date", "uname", "id",
        "whoami", "hostname", "df", "du", "ps", "true", "false", "test",
        "xargs", "readlink", "basename", "dirname", "realpath", "less", "more"
    };

    // Mutating but worktree-bounded — path arguments are jail-checked.
    private static readonly HashSet<string> WriteBinaries = new(StringComparer.Ordinal)
    {
        "touch", "mkdir", "rmdir", "cp", "mv", "ln", "tee", "echo", "printf",
        "npm", "yarn", "pnpm", "dotnet", "make", "cmake", "cargo", "go",
        "tar", "zip", "unzip", "gzip", "gunzip", "rm"
    };

    public CommandRiskAssessment Classify(string command, string worktreePath)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new(SecurityRiskLevel.Dangerous, "Comando vazio — fail-closed.");
        }

        // Command substitution / backticks cannot be inspected safely.
        if (command.Contains("$(", StringComparison.Ordinal) || command.Contains('`'))
        {
            return new(SecurityRiskLevel.Dangerous, "Substituição de comando detectada — não auditável.");
        }

        var worst = new CommandRiskAssessment(SecurityRiskLevel.Safe, "Sem segmentos avaliados.");
        var segments = SplitSegments(command);
        if (segments.Count == 0)
        {
            return new(SecurityRiskLevel.Dangerous, "Comando não parseável — fail-closed.");
        }

        foreach (var segment in segments)
        {
            var assessment = ClassifySegment(segment, worktreePath);
            if (assessment.RiskLevel > worst.RiskLevel)
            {
                worst = assessment;
            }
        }

        return worst;
    }

    private static CommandRiskAssessment ClassifySegment(
        IReadOnlyList<string> tokens, string worktreePath)
    {
        if (tokens.Count == 0)
        {
            return new(SecurityRiskLevel.Dangerous, "Segmento vazio — fail-closed.");
        }

        // Skip leading env assignments (FOO=1 cmd ...) and redirect targets.
        var index = 0;
        while (index < tokens.Count && IsEnvAssignment(tokens[index]))
        {
            index++;
        }

        if (index >= tokens.Count)
        {
            return new(SecurityRiskLevel.Dangerous, "Sem binário após atribuições — fail-closed.");
        }

        var binary = Path.GetFileName(tokens[index]);
        var args = tokens.Skip(index + 1).ToList();

        return binary switch
        {
            "git" => ClassifyGit(args, worktreePath),
            "dotnet" => ClassifyDotnet(args, worktreePath),
            "npm" or "yarn" or "pnpm" => ClassifyNpm(args, worktreePath),
            "rm" => ClassifyRm(args, worktreePath),
            _ when DangerousBinaries.Contains(binary) =>
                new(SecurityRiskLevel.Dangerous, $"'{binary}' requer elevação, rede ou execução arbitrária."),
            // sed/awk com edição in-place são escrita, não leitura.
            _ when binary is "sed" or "awk" && args.Any(a => a.StartsWith("-i"))
                => CheckPaths(args, worktreePath, SecurityRiskLevel.WorkspaceWrite),
            _ when SafeBinaries.Contains(binary) => CheckPaths(args, worktreePath, SecurityRiskLevel.Safe),
            _ when WriteBinaries.Contains(binary) => CheckPaths(args, worktreePath, SecurityRiskLevel.WorkspaceWrite),
            _ => new(SecurityRiskLevel.Dangerous, $"Binário desconhecido '{binary}' — fail-closed.")
        };
    }

    private static CommandRiskAssessment ClassifyGit(
        IReadOnlyList<string> args, string worktreePath)
    {
        var sub = args.FirstOrDefault(a => !a.StartsWith('-'));
        switch (sub)
        {
            case null:
                return new(SecurityRiskLevel.Safe, "git sem subcomando.");
            case "status" or "diff" or "log" or "show" or "blame" or "rev-parse"
                or "ls-files" or "ls-tree" or "describe" or "shortlog" or "reflog"
                or "remote" or "config" or "branch" or "tag" or "stash":
                // branch/tag/stash são Safe apenas sem flags destrutivas — checado abaixo.
                if (sub is "branch" or "tag" && args.Any(a => a is "-D" or "--delete" or "-d"))
                {
                    return new(SecurityRiskLevel.Dangerous, "Deleção de ref local detectada.");
                }

                if (sub == "stash" && args.Any(a => a is "drop" or "clear"))
                {
                    return new(SecurityRiskLevel.Dangerous, "Descarte de stash detectado.");
                }

                if (sub == "remote" && args.Any(a => a is "add" or "remove" or "set-url"))
                {
                    return CheckPaths(args.Skip(1).ToList(), worktreePath, SecurityRiskLevel.WorkspaceWrite);
                }

                if (sub == "config" && args.Any(a => a is "--global" or "--system"))
                {
                    return new(SecurityRiskLevel.Dangerous, "git config fora do repositório.");
                }

                return CheckPaths(args.Skip(1).ToList(), worktreePath, SecurityRiskLevel.Safe);
            case "push" or "pull" or "fetch" or "clone" or "remote-update":
                return new(SecurityRiskLevel.Dangerous, $"git {sub} toca rede/remoto.");
            case "reset" when args.Any(a => a is "--hard"):
                return new(SecurityRiskLevel.Dangerous, "git reset --hard descarta trabalho.");
            case "clean" when args.Any(a => a.StartsWith("-f") || a.Contains('f')):
                return new(SecurityRiskLevel.Dangerous, "git clean -f remove arquivos não rastreados.");
            case "add" or "commit" or "checkout" or "switch" or "restore" or "merge"
                or "rebase" or "cherry-pick" or "mv" or "rm" or "init" or "worktree"
                or "reset" or "clean" or "apply" or "format-patch" or "am":
                return CheckPaths(args.Skip(1).ToList(), worktreePath, SecurityRiskLevel.WorkspaceWrite);
            default:
                return new(SecurityRiskLevel.Dangerous, $"Subcomando git '{sub}' desconhecido — fail-closed.");
        }
    }

    private static CommandRiskAssessment ClassifyDotnet(
        IReadOnlyList<string> args, string worktreePath)
    {
        var sub = args.FirstOrDefault(a => !a.StartsWith('-'));
        switch (sub)
        {
            case "test" or "vstest":
                return CheckPaths(args.Skip(1).ToList(), worktreePath, SecurityRiskLevel.Safe);
            case "build" or "restore" or "run" or "watch" or "publish" or "pack"
                or "format" or "clean" or "new" or "add" or "remove" or "list"
                or "sln" or "build-server":
                return CheckPaths(args.Skip(1).ToList(), worktreePath, SecurityRiskLevel.WorkspaceWrite);
            case "nuget" or "tool" or "workload":
                return new(SecurityRiskLevel.Dangerous, $"dotnet {sub} instala/publica fora do worktree.");
            default:
                return new(SecurityRiskLevel.Dangerous, $"Subcomando dotnet '{sub}' desconhecido — fail-closed.");
        }
    }

    private static CommandRiskAssessment ClassifyNpm(
        IReadOnlyList<string> args, string worktreePath)
    {
        var sub = args.FirstOrDefault(a => !a.StartsWith('-'));
        return sub switch
        {
            "publish" or "login" or "logout" or "token" or "config" =>
                new(SecurityRiskLevel.Dangerous, $"npm {sub} toca credenciais/registry remoto."),
            null or "test" or "run" or "install" or "ci" or "exec" or "build"
                or "start" or "pack" or "link" or "update" or "audit" or "outdated"
                or "list" or "ls" or "why" or "dedupe" or "prune" =>
                CheckPaths(args.Skip(1).ToList(), worktreePath, SecurityRiskLevel.WorkspaceWrite),
            _ => new(SecurityRiskLevel.Dangerous, $"Subcomando npm '{sub}' desconhecido — fail-closed.")
        };
    }

    private static CommandRiskAssessment ClassifyRm(
        IReadOnlyList<string> args, string worktreePath)
    {
        var targets = args.Where(a => !a.StartsWith('-')).ToList();
        if (targets.Count == 0)
        {
            return new(SecurityRiskLevel.Dangerous, "rm sem alvo — fail-closed.");
        }

        foreach (var target in targets)
        {
            if (!TryResolveInsideJail(target, worktreePath))
            {
                return new(SecurityRiskLevel.Dangerous,
                    $"Alvo '{target}' fora ou irresolúvel no worktree — deleção negada.",
                    EscapesSandbox: true);
            }
        }

        return new(SecurityRiskLevel.WorkspaceWrite,
            "Deleção de arquivos dentro do worktree.");
    }

    /// <summary>
    /// Applies the jail check to every path-looking argument; the minimum level
    /// is <paramref name="baseLevel"/> and any escaping target escalates to
    /// Dangerous. Globs and <c>$VAR</c> expansions are unresolvable → Dangerous.
    /// </summary>
    private static CommandRiskAssessment CheckPaths(
        IReadOnlyList<string> args, string worktreePath, SecurityRiskLevel baseLevel)
    {
        var level = baseLevel;
        var sawRedirect = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is ">" or ">>" or "1>" or "2>" or "&>")
            {
                sawRedirect = true;
                if (i + 1 >= args.Count || !TryResolveInsideJail(args[i + 1], worktreePath))
                {
                    return new(SecurityRiskLevel.Dangerous, "Redirect para fora do worktree.", EscapesSandbox: true);
                }

                level = SecurityRiskLevel.WorkspaceWrite;
                i++;
                continue;
            }

            if (arg.StartsWith('-') || arg == "--")
            {
                continue;
            }

            if (!LooksLikePath(arg))
            {
                continue;
            }

            if (!TryResolveInsideJail(arg, worktreePath))
            {
                return new(SecurityRiskLevel.Dangerous,
                    $"Caminho '{arg}' escapa ou não resolve dentro do worktree.",
                    EscapesSandbox: true);
            }
        }

        return sawRedirect && level == SecurityRiskLevel.Safe
            ? new(SecurityRiskLevel.WorkspaceWrite, "Escrita via redirect dentro do worktree.")
            : new(level, baseLevel == SecurityRiskLevel.Safe ? "Leitura confinada ao worktree." : "Escrita confinada ao worktree.");
    }

    private static bool LooksLikePath(string arg)
        => arg.Contains('/') || arg.Contains('\\') || arg.StartsWith('~')
            || arg.StartsWith('.') || arg.StartsWith('$')
            || arg.Contains('*') || arg.Contains('?');

    private static bool TryResolveInsideJail(string arg, string worktreePath)
    {
        // Glob puro: expande para todo o worktree — amplo demais para ser seguro.
        if (arg is "*" or "**" or "*.*")
        {
            return false;
        }

        // Padrões com glob expandem relativo ao cwd (dentro do worktree) —
        // seguros apenas quando não contêm escape: absoluto, ~, $ ou "..".
        if (arg.Contains('*') || arg.Contains('?'))
        {
            return !arg.StartsWith('/') && !arg.StartsWith('~') && !arg.StartsWith('$')
                && !arg.Split('/').Any(seg => seg == "..");
        }

        // ~, $VAR e process substitution nunca resolvem deterministicamente.
        if (arg.StartsWith('~') || arg.StartsWith('$')
            || arg.Contains('(') || arg.Contains(')'))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(arg, worktreePath);
            return WorkspacePaths.IsUnder(worktreePath, full);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEnvAssignment(string token)
    {
        var eq = token.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        var name = token[..eq];
        return char.IsLetter(name[0]) || name[0] == '_'
            ? name.All(c => char.IsLetterOrDigit(c) || c == '_')
            : false;
    }

    /// <summary>
    /// Splits the line on <c>&amp;&amp;</c>, <c>||</c>, <c>;</c>, <c>|</c> outside
    /// quotes, then tokenizes each segment honouring single/double quotes and
    /// redirect operators as standalone tokens.
    /// </summary>
    private static List<IReadOnlyList<string>> SplitSegments(string command)
    {
        var segments = new List<IReadOnlyList<string>>();
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inSingle = false;
        var inDouble = false;

        void FlushToken()
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        void FlushSegment()
        {
            FlushToken();
            if (tokens.Count > 0)
            {
                segments.Add(tokens.ToArray());
                tokens.Clear();
            }
        }

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (inSingle)
            {
                if (c == '\'')
                {
                    inSingle = false;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (inDouble)
            {
                if (c == '"')
                {
                    inDouble = false;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '\'':
                    inSingle = true;
                    break;
                case '"':
                    inDouble = true;
                    break;
                case ' ' or '\t' or '\n' or '\r':
                    FlushToken();
                    break;
                case '&' when i + 1 < command.Length && command[i + 1] == '&':
                    FlushSegment();
                    i++;
                    break;
                case '|':
                    FlushSegment();
                    if (i + 1 < command.Length && command[i + 1] == '|')
                    {
                        i++;
                    }

                    break;
                case ';':
                    FlushSegment();
                    break;
                case '>' or '<':
                    FlushToken();
                    var op = c.ToString();
                    while (i + 1 < command.Length && command[i + 1] == c)
                    {
                        op += c;
                        i++;
                    }

                    tokens.Add(op);
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        FlushSegment();
        return segments;
    }
}
