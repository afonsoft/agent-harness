using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Default agent prompt template and placeholder substitution
/// (SPEC-20260918-agent-execution-ux RF-004). The runtime override lives at
/// <c>Taskboard:Agents:DefaultPrompt</c>; this builtin is the fallback.
/// </summary>
public static class AgentPromptTemplate
{
    public const string ConfigurationKey = "Taskboard:Agents:DefaultPrompt";

    public const int MaxLength = 8192;

    public const string Builtin = """
        Clone o repositório {repoUrl} dentro do diretório de trabalho atual e aplique as correções descritas na issue abaixo.
        Use as skills disponíveis para otimizar o processo e consulte o MCP "knowledge" para contexto adicional quando necessário.

        Issue: {issueTitle}

        {issueBody}
        """;

    /// <summary>Substitutes <c>{repoUrl}</c>, <c>{issueTitle}</c> and <c>{issueBody}</c>.</summary>
    public static string Render(string template, string? repoUrl, string? issueTitle, string? issueBody) =>
        template
            .Replace("{repoUrl}", repoUrl ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueTitle}", issueTitle ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueBody}", issueBody ?? string.Empty, StringComparison.Ordinal)
            .Trim();
}
