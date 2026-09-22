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
    /// <summary>v2: <c>capabilities.session.mcp.stdio</c> (v1 assumes stdio unconditionally).</summary>
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

    public static AcpPeerInfo FromInitialize(JsonElement result)
    {
        var info = new AcpPeerInfo();

        if (result.TryGetProperty("protocolVersion", out var pv) && pv.ValueKind == JsonValueKind.Number)
        {
            info.ProtocolVersion = pv.GetInt32();
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

        if (result.TryGetProperty("authMethods", out var methods) && methods.ValueKind == JsonValueKind.Array)
        {
            var list = new List<AcpAuthMethod>();
            foreach (var m in methods.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
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

            info.AuthMethods = list;
        }

        return info;
    }

    /// <summary>
    /// ACP v2 <c>initialize</c> result (SPEC-20260921-acp-v2-readiness RF-205):
    /// <c>info</c>/<c>capabilities</c> replace <c>agentInfo</c>/<c>agentCapabilities</c>;
    /// <c>capabilities.session</c> presence enables the baseline
    /// resume/close/list; auth methods carry <c>methodId</c> + required
    /// <c>type</c> and imply both <c>auth/login</c> and <c>auth/logout</c>.
    /// There is no <c>loadSession</c> in v2 — resume + replayFrom covers it.
    /// </summary>
    public static AcpPeerInfo FromInitializeV2(JsonElement result)
    {
        var info = new AcpPeerInfo { ProtocolVersion = 2 };

        if (result.TryGetProperty("protocolVersion", out var pv) && pv.ValueKind == JsonValueKind.Number)
        {
            info.ProtocolVersion = pv.GetInt32();
        }

        if (result.TryGetProperty("info", out var ai) && ai.ValueKind == JsonValueKind.Object)
        {
            info.AgentName = ai.TryGetProperty("name", out var n) ? n.GetString() : null;
            info.AgentVersion = ai.TryGetProperty("version", out var v) ? v.GetString() : null;
        }

        if (result.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object
            && caps.TryGetProperty("session", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            // Baseline in v2 once the agent advertises the session group.
            info.SessionResume = true;
            info.SessionClose = true;
            info.SessionList = true;
            info.SessionDelete = HasObject(s, "delete");
            info.AdditionalDirectories = HasObject(s, "additionalDirectories");

            if (s.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                info.PromptImage = HasObject(p, "image");
                info.PromptAudio = HasObject(p, "audio");
                info.PromptEmbeddedContext = HasObject(p, "embeddedContext");
            }

            if (s.TryGetProperty("mcp", out var m) && m.ValueKind == JsonValueKind.Object)
            {
                info.McpStdio = HasObject(m, "stdio");
                info.McpHttp = HasObject(m, "http");
            }
        }

        if (result.TryGetProperty("authMethods", out var methods) && methods.ValueKind == JsonValueKind.Array)
        {
            var list = new List<AcpAuthMethod>();
            foreach (var m in methods.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = m.TryGetProperty("methodId", out var idEl) ? idEl.GetString() : null;
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

            info.AuthMethods = list;
            // v2: advertising methods requires both auth/login and auth/logout.
            info.AuthLogout = list.Count > 0;
        }

        return info;
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
