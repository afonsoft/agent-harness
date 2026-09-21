using System.Text.RegularExpressions;

namespace Taskboard.Integrations.Harness.Security;

/// <summary>
/// Masks known credential patterns in output streams before they reach logs,
/// SignalR or the agent context (SPEC-20260919-harness-security-permission-gateway
/// RF-003). The original secret value is never returned, logged or persisted.
/// </summary>
public sealed partial class SecretScrubber : Taskboard.Agents.ISecretRedactor
{
    public const string Redacted = "[REDACTED_SECRET]";

    /// <inheritdoc />
    public string? Redact(string? text) => text is null ? null : Scrub(text);

    public string Scrub(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return output;
        }

        var result = output;
        result = GitHubTokenRegex().Replace(result, Redacted);
        result = GitHubPatRegex().Replace(result, Redacted);
        result = OpenAiKeyRegex().Replace(result, Redacted);
        result = AwsKeyRegex().Replace(result, Redacted);
        result = BearerTokenRegex().Replace(result, $"Bearer {Redacted}");
        result = PrivateKeyRegex().Replace(result, Redacted);
        return result;
    }

    // ghp_/gho_/ghu_/ghs_/ghr_ personal + OAuth tokens (36+ chars após prefixo).
    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9_]{36,}", RegexOptions.Compiled)]
    private static partial Regex GitHubTokenRegex();

    // github_pat_ fine-grained PATs.
    [GeneratedRegex(@"github_pat_[A-Za-z0-9_]{22,}", RegexOptions.Compiled)]
    private static partial Regex GitHubPatRegex();

    // sk- OpenAI/Anthropic-style API keys.
    [GeneratedRegex(@"sk-[A-Za-z0-9_-]{20,}", RegexOptions.Compiled)]
    private static partial Regex OpenAiKeyRegex();

    // AWS access key ids.
    [GeneratedRegex(@"\b(?:AKIA|ASIA|AGPA|AIDA|AROA)[A-Z0-9]{16}\b", RegexOptions.Compiled)]
    private static partial Regex AwsKeyRegex();

    // Authorization: Bearer <token>.
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=-]{20,}", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    // PEM private key headers.
    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled)]
    private static partial Regex PrivateKeyRegex();
}
