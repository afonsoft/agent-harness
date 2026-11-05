using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Taskboard.Application.Contracts.Agents;

namespace Taskboard.Integrations.Agents.Context;

/// <summary>
/// SPEC-20261104-agent-context-templates RF-001: HMAC-signed callback tokens —
/// no store lookup on validation. Signing key resolution order:
/// <c>Taskboard:AgentCallbackKey</c> → <c>Taskboard:ApiKey</c> → a random
/// per-process key (tokens die with the process — matching run lifetime).
/// </summary>
public sealed class AgentCallbackTokenService : IAgentCallbackTokenService
{
    private const string Audience = "agent-callback";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(12);

    private readonly byte[] _key;
    private readonly TimeSpan _ttl;

    public AgentCallbackTokenService(IConfiguration? configuration = null)
    {
        var configured = configuration?["Taskboard:AgentCallbackKey"];
        var fallback = configuration?["Taskboard:ApiKey"];
        _key = configured is { Length: > 0 } c ? SHA256.HashData(Encoding.UTF8.GetBytes(c))
            : fallback is { Length: > 0 } f ? SHA256.HashData(Encoding.UTF8.GetBytes(f))
            : RandomNumberGenerator.GetBytes(32);
        _ttl = configuration is not null
            && TimeSpan.TryParse(configuration["Taskboard:AgentCallbackTtl"], out var parsed)
            && parsed > TimeSpan.Zero
                ? parsed
                : DefaultTtl;
    }

    public string Mint(
        string sessionKey, string scope, string? taskId, string? dispatchId,
        string agentName, IReadOnlyList<string> capabilities)
    {
        var payload = new
        {
            aud = Audience,
            session = sessionKey,
            scope,
            taskId,
            dispatchId,
            agent = agentName,
            caps = capabilities,
            exp = DateTimeOffset.UtcNow.Add(_ttl).ToUnixTimeSeconds(),
        };
        var body = B64(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"v1.{body}.{B64(Sign(Encoding.UTF8.GetBytes($"v1.{body}")))}";
    }

    public AgentCallbackClaims? Validate(string token)
    {
        var parts = token?.Split('.') ?? [];
        if (parts.Length != 3 || parts[0] != "v1")
        {
            return null;
        }

        var expected = B64(Sign(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}")));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(UnB64(parts[1])));
            var root = doc.RootElement;
            if (root.GetProperty("aud").GetString() != Audience
                || root.GetProperty("exp").GetInt64() < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return null;
            }

            return new AgentCallbackClaims(
                root.GetProperty("session").GetString()!,
                root.GetProperty("scope").GetString()!,
                GetString(root, "taskId"),
                GetString(root, "dispatchId"),
                GetString(root, "agent") ?? "agent",
                root.TryGetProperty("caps", out var caps)
                    ? caps.EnumerateArray()
                        .Where(c => c.ValueKind == JsonValueKind.String)
                        .Select(c => c.GetString()!).ToArray()
                    : [],
                DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("exp").GetInt64()).UtcDateTime);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private byte[] Sign(byte[] data)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(data);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static string B64(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] UnB64(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
