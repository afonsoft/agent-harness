using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Taskboard.Server.Auth;

/// <summary>
/// SPEC-20260915-api-authorization-hardening RF-003: `X-Api-Key` scheme for
/// non-browser clients (taskctl, MCP server). The effective key is read from
/// configuration (<c>Taskboard:ApiKey</c> / <c>TASKBOARD_API_KEY</c>) per
/// request so database overrides apply without restart. Comparison is
/// constant-time; the key is never logged. Missing header → NoResult so the
/// cookie scheme still applies; invalid header → Fail.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string ConfigurationKey = "Taskboard:ApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(presented))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var expected = Context.RequestServices
            .GetRequiredService<IConfiguration>()[ConfigurationKey];
        if (string.IsNullOrEmpty(expected)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(expected)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "api-key-client"),
            new Claim(ClaimTypes.Role, "Admin")
        ], Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
