using System.Text.Json;

namespace Taskboard.Integrations.Agents;

/// <summary>
/// One auth method advertised by the agent in the initialize response.
/// <see cref="Type"/> defaults to <c>"agent"</c> per ACP v1; <c>"terminal"</c>
/// means the client must run the configured agent program interactively.
/// </summary>
public sealed record AcpAuthMethod(
    string Id,
    string Type,
    string Name,
    string? Description,
    IReadOnlyList<string> Args);

/// <summary>
/// Peer state negotiated during the ACP v1 handshake and session creation
/// (SPEC-20260921-acp-v1-conformance RF-001/RF-002/RF-003). Captured from the
/// <c>initialize</c> and <c>session/new</c> responses so the control plane can
/// gate optional methods by capability instead of guessing.
/// </summary>
public sealed class AcpPeerInfo
{
    public int ProtocolVersion { get; set; } = 1;
    /// <summary>Agent-assigned session id once session/new or session/resume completes.</summary>
    public string? SessionId { get; set; }
    public string? AgentName { get; set; }
    public string? AgentVersion { get; set; }
    public bool LoadSession { get; set; }
    public bool SessionResume { get; set; }
    public bool SessionClose { get; set; }
    public bool SessionDelete { get; set; }
    public bool SessionList { get; set; }
    public bool AdditionalDirectories { get; set; }
    public bool McpHttp { get; set; }
    public bool McpSse { get; set; }

    /// <summary>v2 capabilities.session.mcp.stdio — the agent can spawn MCP subprocesses.</summary>
    public bool McpStdio { get; set; }
    public bool PromptImage { get; set; }
    public bool PromptAudio { get; set; }
    public bool PromptEmbeddedContext { get; set; }
    public bool AuthLogout { get; set; }
    public IReadOnlyList<AcpAuthMethod> AuthMethods { get; set; } = [];

    /// <summary>Raw <c>modes</c> object returned by <c>session/new</c>, if any.</summary>
    public JsonElement? Modes { get; set; }

    /// <summary>Raw <c>configOptions</c> array returned by <c>session/new</c>/<c>set_config_option</c>.</summary>
    public JsonElement? ConfigOptions { get; set; }

    public static AcpPeerInfo FromInitialize(JsonElement result) => FromInitialize(result, 1);

    /// <summary>
    /// SPEC-20260921-acp-v2-readiness RF-202: parses the initialize result for
    /// the negotiated version. v2 reorganized capabilities — a single
    /// <c>capabilities</c> object with session-scoped groups, object presence
    /// markers instead of booleans, and a baseline of session methods implied
    /// by <c>capabilities.session</c> itself.
    /// </summary>
    public static AcpPeerInfo FromInitialize(JsonElement result, int protocolVersion)
    {
        var info = new AcpPeerInfo();

        if (result.TryGetProperty("protocolVersion", out var pv) && pv.ValueKind == JsonValueKind.Number)
        {
            info.ProtocolVersion = pv.GetInt32();
        }

        if (protocolVersion >= 2)
        {
            ApplyInitializeV2(info, result);
            return info;
        }

        if (result.TryGetProperty("agentInfo", out var ai) && ai.ValueKind == JsonValueKind.Object)
        {
            info.AgentName = ai.TryGetProperty("name", out var n) ? n.GetString() : null;
            info.AgentVersion = ai.TryGetProperty("version", out var v) ? v.GetString() : null;
        }

        if (result.TryGetProperty("agentCapabilities", out var caps) && caps.ValueKind == JsonValueKind.Object)
        {
            info.LoadSession = GetBool(caps, "loadSession");

            if (caps.TryGetProperty("promptCapabilities", out var pc) && pc.ValueKind == JsonValueKind.Object)
            {
                info.PromptImage = GetBool(pc, "image");
                info.PromptAudio = GetBool(pc, "audio");
                info.PromptEmbeddedContext = GetBool(pc, "embeddedContext");
            }

            if (caps.TryGetProperty("mcpCapabilities", out var mc) && mc.ValueKind == JsonValueKind.Object)
            {
                info.McpHttp = GetBool(mc, "http");
                info.McpSse = GetBool(mc, "sse");
            }

            if (caps.TryGetProperty("sessionCapabilities", out var sc) && sc.ValueKind == JsonValueKind.Object)
            {
                info.SessionResume = HasObject(sc, "resume");
                info.SessionClose = HasObject(sc, "close");
                info.SessionDelete = HasObject(sc, "delete");
                info.SessionList = HasObject(sc, "list");
                info.AdditionalDirectories = HasObject(sc, "additionalDirectories");
            }

            if (caps.TryGetProperty("auth", out var auth) && auth.ValueKind == JsonValueKind.Object)
            {
                info.AuthLogout = HasObject(auth, "logout") || GetBool(auth, "logout");
            }
        }

        info.AuthMethods = ParseAuthMethods(result);
        return info;
    }

    /// <summary>
    /// v2 initialize result: <c>info</c>+<c>capabilities</c> are role-agnostic,
    /// <c>capabilities.session</c> implies the baseline session methods
    /// (new/resume/list/close/prompt/cancel), support markers are objects, and
    /// a non-empty <c>authMethods</c> implies both auth/login and auth/logout.
    /// </summary>
    private static void ApplyInitializeV2(AcpPeerInfo info, JsonElement result)
    {
        if (result.TryGetProperty("info", out var agentInfo) && agentInfo.ValueKind == JsonValueKind.Object)
        {
            info.AgentName = agentInfo.TryGetProperty("name", out var n) ? n.GetString() : null;
            info.AgentVersion = agentInfo.TryGetProperty("version", out var v) ? v.GetString() : null;
        }

        if (result.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object
            && caps.TryGetProperty("session", out var session) && session.ValueKind == JsonValueKind.Object)
        {
            // Advertising capabilities.session commits the agent to the
            // baseline methods — no individual list/resume/close markers.
            info.SessionResume = true;
            info.SessionClose = true;
            info.SessionList = true;
            info.SessionDelete = HasObject(session, "delete");
            info.AdditionalDirectories = HasObject(session, "additionalDirectories");

            if (session.TryGetProperty("prompt", out var prompt) && prompt.ValueKind == JsonValueKind.Object)
            {
                info.PromptImage = HasObject(prompt, "image");
                info.PromptAudio = HasObject(prompt, "audio");
                info.PromptEmbeddedContext = HasObject(prompt, "embeddedContext");
            }

            if (session.TryGetProperty("mcp", out var mcp) && mcp.ValueKind == JsonValueKind.Object)
            {
                info.McpHttp = HasObject(mcp, "http");
                info.McpStdio = HasObject(mcp, "stdio");
            }
        }

        var authMethods = ParseAuthMethods(result);
        info.AuthMethods = authMethods;
        // v2: non-empty authMethods advertises the whole auth surface —
        // auth/login AND auth/logout are both required, no logout marker.
        info.AuthLogout = authMethods.Count > 0;
    }

    /// <summary>Parses authMethods; accepts v1 <c>id</c> and v2 <c>methodId</c> descriptors.</summary>
    private static IReadOnlyList<AcpAuthMethod> ParseAuthMethods(JsonElement result)
    {
        if (!result.TryGetProperty("authMethods", out var methods) || methods.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<AcpAuthMethod>();
        foreach (var m in methods.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = m.TryGetProperty("methodId", out var mid) ? mid.GetString()
                : m.TryGetProperty("id", out var idEl) ? idEl.GetString()
                : null;
            if (id is null)
            {
                continue;
            }

            var args = new List<string>();
            if (m.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Array)
            {
                args.AddRange(argsEl.EnumerateArray()
                    .Select(a => a.GetString())
                    .Where(a => a is not null)!);
            }

            list.Add(new AcpAuthMethod(
                id,
                m.TryGetProperty("type", out var t) ? t.GetString() ?? "agent" : "agent",
                m.TryGetProperty("name", out var nm) ? nm.GetString() ?? id : id,
                m.TryGetProperty("description", out var d) ? d.GetString() : null,
                args));
        }

        return list;
    }

    /// <summary>Merges <c>modes</c>/<c>configOptions</c> from a session lifecycle response.</summary>
    public void ApplySessionResult(JsonElement result)
    {
        if (result.TryGetProperty("modes", out var modes) && modes.ValueKind == JsonValueKind.Object)
        {
            Modes = modes.Clone();
        }

        if (result.TryGetProperty("configOptions", out var opts) && opts.ValueKind == JsonValueKind.Array)
        {
            ConfigOptions = opts.Clone();
        }
    }

    private static bool GetBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    private static bool HasObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Object;
}
