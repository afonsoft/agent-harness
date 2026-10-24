using System.Text;
using System.Text.RegularExpressions;

namespace Taskboard.Integrations.Harness.Security;

/// <summary>
/// Credential-path denylist (SPEC-20261023-p0-agent-hardening RF-A01/RF-A03).
/// fnmatch-style patterns matched against the normalized path (separators
/// unified to '/', relative args treated as rooted). Matches report a short
/// pattern <em>class</em> (e.g. <c>ssh-dir</c>) — never the matched path — so
/// denial reasons audit without leaking secret locations. Built-ins are
/// non-removable; operator extras (<c>Taskboard:Security:DenyPathPatterns</c>)
/// are append-only and report class <c>custom</c>.
/// </summary>
public sealed class SensitivePathDenylist
{
    private static readonly RegexOptions Options =
        RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    // Builtin → pattern class. Directory-style rules pair `*/X` + `*/X/*` so
    // both the dir itself and any content below it deny. Declared before
    // `Shared` — static fields initialize in textual order.
    private static readonly (string Pattern, string Class)[] BuiltIn =
    [
        ("*/.ssh", "ssh-dir"), ("*/.ssh/*", "ssh-dir"),
        ("*/.gnupg", "gpg-dir"), ("*/.gnupg/*", "gpg-dir"),
        ("*/.azure", "azure-credentials"), ("*/.azure/*", "azure-credentials"),
        ("*/.config/gcloud", "gcp-credentials"), ("*/.config/gcloud/*", "gcp-credentials"),
        ("*/.aws/credentials", "aws-credentials"),
        ("*/.aws/config", "aws-credentials"),
        ("*/.docker/config.json", "docker-config"),
        ("*/.kube/config", "kube-config"),
        ("*/.agent-harness/secrets*", "harness-secrets"),
        ("*/.agent-harness/credentials*", "harness-secrets"),
    ];

    /// <summary>Built-ins only — the default when no operator extras are wired.</summary>
    public static readonly SensitivePathDenylist Shared = new();

    private readonly List<(Regex Regex, string Class)> _rules;

    public SensitivePathDenylist(IEnumerable<string>? extraPatterns = null)
    {
        _rules = BuiltIn.Select(b => (Compile(b.Pattern), b.Class)).ToList();
        if (extraPatterns is null)
        {
            return;
        }

        foreach (var pattern in extraPatterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                _rules.Add((Compile(pattern), "custom"));
            }
        }
    }

    /// <summary>
    /// Reports whether <paramref name="path"/> hits the denylist and, when it
    /// does, which pattern class matched (for audit — the path itself is never
    /// surfaced in denial reasons).
    /// </summary>
    public bool TryMatch(string path, out string patternClass)
    {
        patternClass = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = Normalize(path);
        foreach (var (regex, cls) in _rules)
        {
            try
            {
                if (regex.IsMatch(normalized))
                {
                    patternClass = cls;
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Fail-closed: a pathological operator pattern denies rather
                // than silently passing a candidate credential path.
                patternClass = "denylist-error";
                return true;
            }
        }

        return false;
    }

    // Root relative args so `*/`-anchored patterns match them: `.ssh/x` →
    // `/.ssh/x`, `./.ssh/x` → `/.ssh/x`, `~/.ssh/x` → `/~/.ssh/x`.
    private static string Normalize(string path)
    {
        var p = path.Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        return p.StartsWith('/') ? p : "/" + p;
    }

    private static Regex Compile(string pattern)
        => new(PatternToRegex(pattern), Options, MatchTimeout);

    // fnmatch (Python-style): `*` crosses separators, `?` is any char,
    // `[seq]`/`[!seq]` are char classes. Everything else is literal.
    private static string PatternToRegex(string pattern)
    {
        var b = new StringBuilder("^");
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    b.Append(".*");
                    break;
                case '?':
                    b.Append('.');
                    break;
                case '[':
                    {
                        var close = pattern.IndexOf(']', i + 1);
                        if (close < 0)
                        {
                            b.Append("\\[");
                            break;
                        }

                        var body = pattern.Substring(i + 1, close - i - 1);
                        if (body.StartsWith('!'))
                        {
                            body = "^" + body[1..];
                        }

                        b.Append('[').Append(body).Append(']');
                        i = close;
                        break;
                    }
                default:
                    b.Append(Regex.Escape(c.ToString()));
                    break;
            }

            i++;
        }

        b.Append('$');
        return b.ToString();
    }
}
