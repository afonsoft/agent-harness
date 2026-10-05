using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Configuration;
using Taskboard.Domain.Entities;
using Taskboard.Domain.Shared.Configuration;
using Taskboard.Repositories;

namespace Taskboard.Application.Configuration;

/// <summary>
/// Exposes the known configuration catalog: effective value, source
/// (<c>db|env|appsettings|default</c>) and per-key validation for database overrides.
/// </summary>
public sealed class RuntimeConfigurationService
{
    private const string HttpsScheme = "https";

    private static readonly string[] LogLevels =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

    private static readonly CatalogEntry[] Catalog =
    [
        new("Taskboard:Port", "47823", Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Server binding — change via HARNESS_PORT or appsettings.json.",
            EnvAlias: "HARNESS_PORT",
            Validate: v => int.TryParse(v, out var p) && p is >= 1 and <= 65535
                ? null
                : "Port must be an integer between 1 and 65535.", Group: "Server", ManagedIn: null),
        new("Taskboard:BaseUrl", "http://127.0.0.1:47823", Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Server binding — change via HARNESS_URL or appsettings.json.",
            EnvAlias: "HARNESS_URL",
            Validate: v => Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme is "http" or HttpsScheme
                ? null
                : "BaseUrl must be an absolute http(s) URL.", Group: "Server", ManagedIn: null),
        new("AllowedHosts", "*", Editable: true, RequiresRestart: true, ReadOnlyReason: null,
            EnvAlias: null,
            Validate: v => string.IsNullOrWhiteSpace(v) ? "AllowedHosts cannot be empty." : null, Group: "Server", ManagedIn: null),
        new("Logging:LogLevel:Default", "Information", Editable: true, RequiresRestart: false, ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateLogLevel, Group: "Logging", ManagedIn: null),
        new("Logging:LogLevel:Microsoft.AspNetCore", "Warning", Editable: true, RequiresRestart: false, ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateLogLevel, Group: "Logging", ManagedIn: null),
        new("Taskboard:DataDir", ".data", Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Cannot be stored in the database it configures.",
            EnvAlias: "HARNESS_DATA_DIR", Validate: null, Group: "Connections", ManagedIn: null),
        new("Taskboard:Database:ConnectionStringName", "Taskboard", Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Cannot be stored in the database it configures.",
            EnvAlias: null, Validate: null, Group: "Connections", ManagedIn: null),
        new("ConnectionStrings:Taskboard", null, Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Cannot be stored in the database it configures.",
            EnvAlias: null, Validate: null, Group: "Connections", ManagedIn: null),
        new("Admin:Username", "admin", Editable: false, RequiresRestart: true,
            ReadOnlyReason: "Managed by the admin account (admin.json).",
            EnvAlias: "HARNESS_ADMIN_USERNAME", Validate: null, Group: "Server", ManagedIn: null),
        new("Taskboard:Skills:Repository", "afonsoft/skills", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_SKILLS_REPO", Validate: ValidateSkillsRepository, Group: "Connections", ManagedIn: "/settings?tab=mcp-skills"),
        new("Taskboard:ApiKey", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_API_KEY", Validate: ValidateApiKey, Group: "Security", ManagedIn: null),
        new("Taskboard:Rag:ServerName", "knowledge", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_RAG_NAME", Validate: ValidateRagServerName, Group: "Connections", ManagedIn: "/settings?tab=integrations"),
        new("Taskboard:Rag:Url", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_RAG_URL", Validate: ValidateRagUrl, Group: "Connections", ManagedIn: "/settings?tab=integrations"),
        new("Taskboard:Rag:ApiKey", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_RAG_API_KEY", Validate: ValidateRagApiKey, Group: "Connections", ManagedIn: "/settings?tab=integrations"),
        new("Taskboard:Terminal:Enabled", "true", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_TERMINAL_ENABLED", Validate: ValidateBoolean, Group: "Server", ManagedIn: "/settings?tab=general"),
        // SPEC-20260929-ai-chat-view-first RF-003: liga os endpoints de
        // agente interativo (prompt/queue/retry/cancel/events). Lida por
        // request, sem restart. SPEC-20260929-webcli-toggle-finops-active-
        // sessions RF-001: default on (toggle visível em Settings → Features).
        new("Taskboard:WebCliAgent:Enabled", "true", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_WEB_CLI_AGENT_ENABLED", Validate: ValidateBoolean, Group: "Server", ManagedIn: "/settings?tab=general"),
        new("Taskboard:Agents:DefaultPrompt", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_DEFAULT_PROMPT", Validate: ValidateDefaultPrompt, Group: "Chat", ManagedIn: "/agents?tab=prompt"),
        // SPEC-20260929-ai-code-provider-chat: provider chat (Settings → Chat).
        new("Taskboard:Chat:Tools:Enabled", "true", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_TOOLS_ENABLED", Validate: ValidateBoolean, Group: "Chat", ManagedIn: null),
        new("Taskboard:Chat:MaxToolIterations", "8", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidatePositiveInt, Group: "Chat", ManagedIn: null),
        new("Taskboard:Chat:SearchBackend", "none", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_SEARCH_BACKEND", Validate: ValidateSearchBackend, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        new("Taskboard:Chat:SearchUrl", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_SEARCH_URL", Validate: ValidateSearchUrl, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        new("Taskboard:Chat:SearchApiKey", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_SEARCH_API_KEY", Validate: null, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        new("Taskboard:Chat:DefaultChatModel", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: null, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        new("Taskboard:Chat:DefaultCodeModel", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: null, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        new("Taskboard:Chat:DefaultImageModel", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: null, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        // SPEC-20261001-chat-default-mode: auto = chat when configured, else agent.
        new("Taskboard:AiChat:DefaultMode", "auto", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_AICHAT_DEFAULT_MODE", Validate: ValidateAiChatDefaultMode, Group: "Chat", ManagedIn: null),
        // SPEC-20261001-chat-capability-registry: masters + granular toggles.
        new("Taskboard:Chat:Skills:Enabled", "true", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_SKILLS_ENABLED", Validate: ValidateBoolean, Group: "Chat", ManagedIn: null),
        new("Taskboard:Chat:AgentDelegation:Enabled", "true", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_AGENT_DELEGATION_ENABLED", Validate: ValidateBoolean, Group: "Chat", ManagedIn: null),
        new("Taskboard:Chat:Mcp:Enabled", "false", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_MCP_ENABLED", Validate: ValidateBoolean, Group: "Chat", ManagedIn: "/settings?tab=mcp-skills"),
        // SPEC-20261001-chat-mcp-client: servers JSON + per-call timeout.
        new("Taskboard:Chat:Mcp:Servers", "[]", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_MCP_SERVERS", Validate: ValidateJsonArray, Group: "Chat", ManagedIn: "/settings?tab=mcp-skills"),
        new("Taskboard:Chat:Mcp:CallTimeoutSeconds", "30", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateIntRange5To300, Group: "Chat", ManagedIn: "/settings?tab=mcp-skills"),
        // SPEC-20261010-mcp-skills-hub RF-002: AI Code also consumes the MCP
        // servers installed globally under ~/.agents.
        new("Taskboard:Chat:Mcp:IncludeGlobalAgents", "true", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_MCP_INCLUDE_GLOBAL_AGENTS", Validate: ValidateBoolean, Group: "Chat", ManagedIn: "/settings?tab=mcp-skills"),
        new("Taskboard:Chat:Capabilities:Disabled", "[]", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateJsonStringArray, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        // SPEC-20261005-chat-background-resume RF-002: detached chat runs —
        // global concurrency cap, checkpoint cadence, run-row retention.
        new("Taskboard:Chat:Runs:MaxConcurrent", "4", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: "HARNESS_CHAT_RUNS_MAX_CONCURRENT", Validate: ValidatePositiveInt, Group: "Chat", ManagedIn: null),
        new("Taskboard:Chat:Runs:CheckpointMs", "750", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateNonNegativeInt, Group: "Chat", ManagedIn: null),
        new("Taskboard:Chat:Runs:RetentionDays", "30", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateNonNegativeInt, Group: "Chat", ManagedIn: null),
        // SPEC-20261005 RF-008: run-completion notifications — in-app toast,
        // Notification API (opt-in, triggers requestPermission) e o master
        // switch de Web Push (Fase 3). Per-browser override mora em
        // localStorage["harness.chat.notify.*"] no client.
        new("Taskboard:Chat:Notify:Done:InApp", "true", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateBoolean, Group: "Chat", ManagedIn: null),
        new("Taskboard:Chat:Notify:Done:Browser", "false", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateBoolean, Group: "Chat", ManagedIn: null),
        new("Taskboard:Chat:Notify:Done:Push", "false", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateBoolean, Group: "Chat", ManagedIn: null),
        // SPEC-20261005-chat-tool-approval RF-004/RF-008: permission policy —
        // global gate switch, default preset, wait timeout and the opt-in
        // approval push. Per-tool overrides live under
        // Taskboard:Chat:Approval:ToolPolicy:{toolName} (not in the catalog —
        // keyed by tool name like Capabilities:Disabled rows).
        new("Taskboard:Chat:Approval:Enabled", "true", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateBoolean, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        new("Taskboard:Chat:Approval:Preset", "ask", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidatePermissionPreset, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        new("Taskboard:Chat:Approval:TimeoutSeconds", "120", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateNonNegativeInt, Group: "Chat", ManagedIn: "/settings?tab=chat"),
        new("Taskboard:Chat:Notify:Approval:Push", "false", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateBoolean, Group: "Chat", ManagedIn: null),
        // SPEC-20261005 RF-009: VAPID identity for Web Push. Empty keys are
        // auto-generated once by VapidKeyService on first subscribe and
        // persisted here (private key stays masked like every secret).
        new("Taskboard:Push:Vapid:PublicKey", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateVapidKey, Group: "Chat", ManagedIn: null),
        new("Taskboard:Push:Vapid:PrivateKey", null, Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateVapidKey, Group: "Chat", ManagedIn: null),
        new("Taskboard:Push:Vapid:Subject", "mailto:admin@localhost", Editable: true, RequiresRestart: false,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateVapidSubject, Group: "Chat", ManagedIn: null),
        // SPEC-20261004-redis-hybrid-cache RF-002: HybridCache L1+L2 — DI wiring
        // only happens at boot, so all four keys require restart. The generic
        // HARNESS__* env mapper already covers them (no dedicated EnvAlias).
        new("Taskboard:Cache:DefaultExpiration", "00:05:00", Editable: true, RequiresRestart: true,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateTimeSpan, Group: "Connections", ManagedIn: null),
        new("Taskboard:Cache:LocalCacheExpiration", "00:01:00", Editable: true, RequiresRestart: true,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateTimeSpan, Group: "Connections", ManagedIn: null),
        new("Taskboard:Cache:Redis:ConnectionString", null, Editable: true, RequiresRestart: true,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateRedisConnectionString, Group: "Connections", ManagedIn: null),
        new("Taskboard:Cache:Redis:InstanceName", "harness:", Editable: true, RequiresRestart: true,
            ReadOnlyReason: null,
            EnvAlias: null, Validate: ValidateRedisInstanceName, Group: "Connections", ManagedIn: null),
    ];

    private readonly IConfiguration _configuration;
    private readonly IRepository<ConfigurationOverride> _overrides;

    public RuntimeConfigurationService(IConfiguration configuration, IRepository<ConfigurationOverride> overrides)
    {
        _configuration = configuration;
        _overrides = overrides;
    }

    /// <summary>Returns every catalog key with effective value, source and editability.</summary>
    public IReadOnlyList<ConfigurationEntryDto> GetEntries()
    {
        return Catalog.Select(entry =>
        {
            var effective = ResolveValue(entry);
            var masked = IsSecret(entry.Key);
            return new ConfigurationEntryDto(
                entry.Key,
                masked ? Mask(effective) : effective,
                ResolveSource(entry),
                entry.Editable,
                entry.RequiresRestart,
                masked,
                entry.ReadOnlyReason,
                entry.Group,
                entry.ManagedIn);
        }).ToList();
    }

    /// <summary>
    /// SPEC-20261010-settings-configuration-tab RF-003: provider/cache summary
    /// for the Configuration tab header — resolved on <b>unmasked</b> values so
    /// detection survives secret masking.
    /// </summary>
    public ConnectionInfoDto GetConnectionInfo()
    {
        var raw = Catalog.Select(entry => new ConfigurationEntryDto(
            entry.Key,
            ResolveValue(entry),
            ResolveSource(entry),
            entry.Editable,
            entry.RequiresRestart,
            Masked: false,
            entry.ReadOnlyReason,
            entry.Group,
            entry.ManagedIn)).ToList();
        return ConnectionInfoResolver.Resolve(raw);
    }

    /// <summary>Validates and persists a database override for <paramref name="key"/>.</summary>
    public async Task<ConfigurationWriteResult> SetOverrideAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        var entry = FindEntry(key);
        if (entry is null)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.Validation, "Unknown configuration key.");
        }

        if (!entry.Editable)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.ReadOnly, "This key cannot be overridden.");
        }

        var validationError = entry.Validate?.Invoke(value ?? string.Empty);
        if (validationError is not null)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.Validation, validationError);
        }

        var existing = await _overrides.Query
            .FirstOrDefaultAsync(o => o.Key == entry.Key, cancellationToken);
        if (existing is null)
        {
            await _overrides.AddAsync(
                new ConfigurationOverride(Guid.NewGuid())
                {
                    Key = entry.Key,
                    Value = value ?? string.Empty,
                    UpdatedAt = DateTime.UtcNow,
                },
                cancellationToken);
        }
        else
        {
            existing.Value = value ?? string.Empty;
            existing.UpdatedAt = DateTime.UtcNow;
            await _overrides.UpdateAsync(existing, cancellationToken);
        }

        await _overrides.SaveChangesAsync(cancellationToken);
        return ConfigurationWriteResult.Ok;
    }

    /// <summary>Removes the database override for <paramref name="key"/>.</summary>
    public async Task<ConfigurationWriteResult> DeleteOverrideAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = FindEntry(key);
        if (entry is null)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.Validation, "Unknown configuration key.");
        }

        if (!entry.Editable)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.ReadOnly, "This key cannot be overridden.");
        }

        var existing = await _overrides.Query
            .FirstOrDefaultAsync(o => o.Key == entry.Key, cancellationToken);
        if (existing is null)
        {
            return ConfigurationWriteResult.Fail(ConfigurationWriteError.NotFound, "No override exists for this key.");
        }

        await _overrides.DeleteAsync(existing, cancellationToken);
        await _overrides.SaveChangesAsync(cancellationToken);
        return ConfigurationWriteResult.Ok;
    }

    /// <summary>
    /// B-22: resolves a catalog key with the effective precedence
    /// (db override > env alias > generic env > appsettings > catalog default).
    /// Endpoint/feature gates must use this instead of raw
    /// <c>IConfiguration.GetValue</c>, which ignores the env alias and the
    /// catalog default.
    /// </summary>
    public string? GetEffectiveValue(string key)
    {
        var entry = FindEntry(key);
        return entry is null ? _configuration[key] : ResolveValue(entry);
    }

    /// <summary>Effective boolean value of a catalog key (B-22).</summary>
    public bool GetEffectiveBool(string key, bool fallback = false)
    {
        var value = GetEffectiveValue(key);
        if (value is null)
        {
            return fallback;
        }

        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static CatalogEntry? FindEntry(string key)
    {
        var entry = Catalog.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            return entry;
        }

        // SPEC-20261005-chat-tool-approval RF-008: per-tool overrides —
        // Taskboard:Chat:Approval:ToolPolicy:{toolName} = ask|never|allow.
        // Arbitrary tool names can't live in the static catalog, so the
        // prefix mints an editable synthetic entry with an enum validator.
        const string prefix = "Taskboard:Chat:Approval:ToolPolicy:";
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && key.Length > prefix.Length
            ? new CatalogEntry(key, null, Editable: true, RequiresRestart: false,
                ReadOnlyReason: null, EnvAlias: null,
                Validate: ValidateToolPolicy, Group: "Chat", ManagedIn: "/settings?tab=chat")
            : null;
    }

    private bool HasDatabaseOverride(string key) =>
        _configuration is IConfigurationRoot root
        && root.Providers.OfType<IOverrideConfigurationProvider>()
            .Any(p => p.TryGetOverride(key, out _));

    private string? ResolveValue(CatalogEntry entry)
    {
        // A DB override is already visible through configuration (the provider is
        // registered last). Otherwise a dedicated env alias (e.g. HARNESS_PORT)
        // beats Taskboard__* env vars and appsettings.
        if (!HasDatabaseOverride(entry.Key)
            && entry.EnvAlias is not null
            && HarnessEnv.Get(entry.EnvAlias) is { Length: > 0 } envValue)
        {
            return envValue;
        }

        return _configuration[entry.Key] ?? entry.DefaultValue;
    }

    private string ResolveSource(CatalogEntry entry)
    {
        // Mirror the effective precedence: db > env alias > Taskboard__* env > appsettings > default.
        if (HasDatabaseOverride(entry.Key))
        {
            return "db";
        }

        if (entry.EnvAlias is not null && HarnessEnv.IsSet(entry.EnvAlias))
        {
            return "env";
        }

        if (_configuration is IConfigurationRoot configRoot)
        {
            foreach (var provider in configRoot.Providers.Reverse())
            {
                if (provider.TryGet(entry.Key, out _))
                {
                    return provider.GetType().Name.Contains("EnvironmentVariables", StringComparison.Ordinal)
                        ? "env"
                        : "appsettings";
                }
            }
        }

        return _configuration[entry.Key] is not null ? "appsettings" : "default";
    }

    private static bool IsSecret(string key) =>
        key.Contains("Password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("Token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
        || key.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase)
        || key.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase);

    private static string? Mask(string? value)
    {
        if (value is null)
        {
            return null;
        }
        var tail = value.Length > 4 ? value[^4..] : string.Empty;
        return $"••••{tail}";
    }

    private static string? ValidateApiKey(string value) =>
        value.Trim().Length is 0 or >= 16
            ? null
            : "API key must be at least 16 characters, or empty to disable.";

    private static string? ValidateRagServerName(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^[a-z0-9][a-z0-9-]{0,63}$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1))
            ? null
            : "Server name must match ^[a-z0-9][a-z0-9-]{0,63}$.";

    private static string? ValidateRagApiKey(string value) =>
        value.Trim().Length is 0 or >= 8
            ? null
            : "RAG API key must be at least 8 characters, or empty for an unauthenticated server.";

    private static string? ValidateRagUrl(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null; // empty disables provisioning
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or HttpsScheme
            ? null
            : "RAG URL must be an absolute http(s) URL, or empty to disable.";
    }

    private static string? ValidateVapidKey(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null; // empty = auto-generate on first subscribe
        }

        return trimmed.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_')
            ? null
            : "VAPID keys must be base64url-encoded (A-Z a-z 0-9 - _), or empty to auto-generate.";
    }

    private static string? ValidateVapidSubject(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is HttpsScheme)
            ? null
            : "VAPID subject must be a mailto: address or an absolute https URL.";
    }

    private static string? ValidateBoolean(string value) =>
        bool.TryParse(value.Trim(), out _)
            ? null
            : "Value must be 'true' or 'false'.";

    private static string? ValidateDefaultPrompt(string value) =>
        value.Length <= Taskboard.Application.Contracts.Agents.AgentPromptTemplate.MaxLength
            ? null
            : $"Prompt template must be at most {Taskboard.Application.Contracts.Agents.AgentPromptTemplate.MaxLength} characters.";

    // SPEC-20260929-ai-code-provider-chat.
    private static string? ValidatePositiveInt(string value) =>
        int.TryParse(value, out var n) && n is >= 1 and <= 64
            ? null
            : "Value must be an integer between 1 and 64.";

    private static string? ValidateNonNegativeInt(string value) =>
        int.TryParse(value, out var n) && n is >= 0 and <= 1_000_000
            ? null
            : "Value must be an integer between 0 and 1000000.";

    private static string? ValidateSearchBackend(string value) =>
        value is "none" or "searxng" or "tavily" or "brave"
            ? null
            : "Search backend must be one of: none, searxng, tavily, brave.";

    private static string? ValidateAiChatDefaultMode(string value) =>
        value is "auto" or "chat" or "agent"
            ? null
            : "Default mode must be one of: auto, chat, agent.";

    private static string? ValidatePermissionPreset(string value) =>
        Taskboard.ValueObjects.ChatPermissionPresets.IsValid(value)
            ? null
            : "Permission preset must be one of: chat, ask, full.";

    private static string? ValidateToolPolicy(string value) =>
        value is "ask" or "never" or "allow"
            ? null
            : "Tool policy must be one of: ask, never, allow.";

    private static string? ValidateJsonStringArray(string value)
    {
        try
        {
            var ids = System.Text.Json.JsonSerializer.Deserialize<List<string>>(value);
            return ids is null ? "Value must be a JSON array of strings." : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return "Value must be a JSON array of strings.";
        }
    }

    private static string? ValidateJsonArray(string value)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                ? null
                : "Value must be a JSON array.";
        }
        catch (System.Text.Json.JsonException)
        {
            return "Value must be a JSON array.";
        }
    }

    private static string? ValidateIntRange5To300(string value) =>
        int.TryParse(value, out var seconds) && seconds is >= 5 and <= 300
            ? null
            : "Timeout must be an integer between 5 and 300 seconds.";

    private static string? ValidateSearchUrl(string value) =>
        string.IsNullOrWhiteSpace(value)
            || (Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme is "http" or HttpsScheme)
            ? null
            : "Search URL must be an absolute http(s) URL.";

    // SPEC-20261004-redis-hybrid-cache RF-002.
    private static string? ValidateTimeSpan(string value) =>
        TimeSpan.TryParse(value, out var parsed) && parsed > TimeSpan.Zero
            ? null
            : "Value must be a positive TimeSpan (e.g. 00:05:00).";

    // Empty disables L2; whitespace-only is neither — reject.
    private static string? ValidateRedisConnectionString(string value) =>
        value.Length == 0 || !string.IsNullOrWhiteSpace(value)
            ? null
            : "Connection string must be empty (L1-only) or non-blank.";

    private static string? ValidateRedisInstanceName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && !value.Any(char.IsWhiteSpace)
            ? null
            : "Instance name must be non-empty, at most 64 chars, no whitespace.";

    private static string? ValidateLogLevel(string value) =>
        LogLevels.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"Log level must be one of: {string.Join(", ", LogLevels)}.";

    private static string? ValidateSkillsRepository(string value)
    {
        var trimmed = value.Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            return null;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Scheme is HttpsScheme or "http" or "file")
        {
            return null;
        }

        return "Repository must be 'owner/repo' or an absolute git URL.";
    }

    private sealed record CatalogEntry(
        string Key,
        string? DefaultValue,
        bool Editable,
        bool RequiresRestart,
        string? ReadOnlyReason,
        string? EnvAlias,
        Func<string, string?>? Validate,
        string Group,
        string? ManagedIn);
}

public enum ConfigurationWriteError
{
    None,
    Validation,
    ReadOnly,
    NotFound
}

public sealed record ConfigurationWriteResult(ConfigurationWriteError Error, string? Message)
{
    public static readonly ConfigurationWriteResult Ok = new(ConfigurationWriteError.None, null);

    public static ConfigurationWriteResult Fail(ConfigurationWriteError error, string message) => new(error, message);
}
