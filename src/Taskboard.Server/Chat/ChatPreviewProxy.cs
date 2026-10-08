using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Server.Chat;

/// <summary>
/// SPEC-20261015-chat-preview-panel RF-001: loopback-only preview proxy —
/// <c>/preview/{port}/{**path}</c> forwards to <c>127.0.0.1:{port}</c> so the
/// running app renders same-origin inside the chat iframe (the element
/// picker needs DOM access). Hop-by-hop headers are stripped both ways;
/// <c>X-Frame-Options</c> and CSP <c>frame-ancestors</c> are dropped so the
/// frame is allowed; absolute loopback <c>Location</c> redirects are
/// rewritten back through the proxy.
/// </summary>
public static class ChatPreviewProxy
{
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Trailers", "Transfer-Encoding", "Upgrade",
    };

    /// <summary>Builds the upstream URL; false when the port is out of range.</summary>
    public static bool TryCreateTarget(
        int port, string? path, string? queryString, out Uri target)
    {
        target = default!;
        if (port is < ChatPreviewUrl.MinPort or > ChatPreviewUrl.MaxPort)
        {
            return false;
        }

        var suffix = path is null ? "/" : $"/{path.TrimStart('/')}";
        target = new Uri($"http://127.0.0.1:{port}{suffix}{queryString}", UriKind.Absolute);
        return true;
    }

    /// <summary>
    /// Rewrites an absolute loopback <c>Location</c> to go back through the
    /// proxy (e.g. <c>http://localhost:5021/login</c> →
    /// <c>/preview/5021/login</c>). Relative and non-loopback values pass
    /// through untouched.
    /// </summary>
    public static string? RewriteLocation(string? location)
    {
        if (string.IsNullOrEmpty(location)
            || !Uri.TryCreate(location, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !IsLoopback(uri.Host))
        {
            return location;
        }

        return $"/preview/{uri.Port}{uri.PathAndQuery}";
    }

    /// <summary>Removes the <c>frame-ancestors</c> directive from a CSP value.</summary>
    public static string? StripFrameAncestors(string? csp)
    {
        if (string.IsNullOrEmpty(csp))
        {
            return csp;
        }

        var kept = csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(directive => !directive.StartsWith("frame-ancestors", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return kept.Length == 0 ? null : string.Join("; ", kept);
    }

    /// <summary>Streams the upstream response into <paramref name="context"/>.</summary>
    public static async Task ForwardAsync(
        HttpContext context, HttpClient client, int port, string? path)
    {
        if (!TryCreateTarget(port, path, context.Request.QueryString.Value, out var target))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(
                $"preview port must be {ChatPreviewUrl.MinPort}–{ChatPreviewUrl.MaxPort}.",
                context.RequestAborted);
            return;
        }

        using var upstream = new HttpRequestMessage(
            new HttpMethod(context.Request.Method), target);
        foreach (var (name, values) in context.Request.Headers)
        {
            if (HopByHop.Contains(name) || name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            upstream.Headers.TryAddWithoutValidation(name, values.ToArray());
        }

        HttpResponseMessage? response;
        try
        {
            response = await client.SendAsync(
                upstream, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // RF-007: a dead port surfaces as a clean 502 — never a stack.
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync(
                $"app não respondeu na porta {port}", context.RequestAborted);
            return;
        }
        catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            return;
        }

        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;

            foreach (var (name, values) in response.Headers)
            {
                CopyResponseHeader(context, name, values);
            }

            foreach (var (name, values) in response.Content.Headers)
            {
                CopyResponseHeader(context, name, values);
            }

            if (context.Response.Headers.ContentLength is null)
            {
                context.Response.Headers.Remove("Content-Length");
            }

            if (response.Content is not null)
            {
                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted)
                    .ConfigureAwait(false);
            }
        }
    }

    private static void CopyResponseHeader(
        HttpContext context, string name, IEnumerable<string> values)
    {
        if (HopByHop.Contains(name)
            || name.Equals("X-Frame-Options", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Content-Security-Policy-Report-Only", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (name.Equals("Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
        {
            var filtered = values
                .Select(StripFrameAncestors)
                .Where(v => !string.IsNullOrEmpty(v))
                .ToArray();
            if (filtered.Length == 0)
            {
                return;
            }

            context.Response.Headers[name] = new StringValues(filtered!);
            return;
        }

        if (name.Equals("Location", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers[name] = new StringValues(
                values.Select(v => RewriteLocation(v) ?? v).ToArray());
            return;
        }

        context.Response.Headers[name] = new StringValues(values.ToArray());
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host is "127.0.0.1" or "[::1]" or "::1";
}
