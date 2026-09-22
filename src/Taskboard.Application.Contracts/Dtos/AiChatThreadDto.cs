namespace Taskboard.Dtos;

public sealed record AiChatThreadDto(
    string Id,
    string Title,
    string Model,
    string ReasoningEffort,
    string Sandbox,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long Version,
    string Mode = "assistant",
    string? AgentType = null,
    string? WorkspacePath = null,
    string? RepositoryFullName = null,
    /// <summary>SPEC-20260921-ai-code-thread-config RF-006: tier usado na escolha do modelo.</summary>
    string? ModelTier = null,
    /// <summary>SPEC-20260921-ai-code-thread-config RF-006: origem do modelo efetivo (acp|probe|curated|custom).</summary>
    string? ModelSource = null);
