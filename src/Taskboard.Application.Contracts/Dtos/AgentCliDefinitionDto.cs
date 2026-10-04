namespace Taskboard.Dtos;

/// <summary>
/// Custom agent CLI definition (SPEC-20260928-ai-code-generic-cli RF-002).
/// <see cref="Resolved"/> reports whether <see cref="Executable"/> resolves on
/// PATH right now — the def stays saved regardless.
/// </summary>
public sealed record AgentCliDefinitionDto(
    string Id,
    string DisplayName,
    string Executable,
    string ArgsTemplate,
    string Transport,
    string? ModelFlag,
    string VersionArgs,
    bool Enabled,
    bool Resolved,
    /// <summary>SPEC-20261004 RF-006: "argv" | "stdin" — prompt delivery for delegated runs.</summary>
    string PromptDelivery = "argv",
    /// <summary>SPEC-20261004 RF-006: optional argv listing the CLI's models for the picker.</summary>
    string? ModelListArgs = null);

/// <summary>Request body for POST/PUT of a custom CLI definition.</summary>
public sealed record UpsertAgentCliDefinitionRequest(
    string DisplayName,
    string Executable,
    string? ArgsTemplate = null,
    string? Transport = null,
    string? ModelFlag = null,
    string? VersionArgs = null,
    bool Enabled = true,
    string? PromptDelivery = null,
    string? ModelListArgs = null);
