using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace Taskboard.Server.Api;

/// <summary>
/// SPEC-20260915-blazor-wasm-migration: mirror of <c>wwwroot/_framework</c> so
/// corporate proxies that block downloads by extension (.dat/.wasm/...) or
/// sniff binary payloads cannot break the Blazor WASM boot. Anonymous — same
/// exposure as <c>MapStaticAssets</c>; strictly read-only, validated names
/// rooted under <c>_framework</c>.
/// </summary>
public static partial class FrameworkAssetsEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex ValidName();

    [GeneratedRegex("^[a-z0-9]{1,10}$")]
    private static partial Regex ValidExt();

    public static RouteGroupBuilder MapFrameworkAssetsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/framework-assets").AllowAnonymous();

        // The blocked suffix must not appear in the request URL — the client
        // sends stem/ext as two segments. ?enc=b64 returns the payload base64
        // as text/plain, defeating extension filters and binary
        // content-sniffing; the client verifies SHA-256 against the boot
        // integrity hash before handing the bytes to Blazor.
        group.MapGet("/{stem}/{ext}", (string stem, string ext, HttpContext http, IWebHostEnvironment env) =>
        {
            if (stem.Contains("..", StringComparison.Ordinal)
                || !ValidName().IsMatch(stem)
                || !ValidExt().IsMatch(ext))
            {
                return Results.NotFound();
            }

            var fileName = $"{stem}.{ext}";
            var root = env.WebRootFileProvider;
            var file = root.GetFileInfo($"_framework/{fileName}");
            if (!file.Exists || file.IsDirectory)
            {
                return Results.NotFound();
            }

            http.Response.Headers.CacheControl = "public,max-age=31536000,immutable";

            var enc = (string?)http.Request.Query["enc"];
            if (string.Equals(enc, "b64", StringComparison.Ordinal))
            {
                using var raw = file.CreateReadStream();
                using var buffer = new MemoryStream();
                raw.CopyTo(buffer);
                return Results.Text(
                    Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length),
                    "text/plain");
            }

            http.Response.Headers.Vary = "Accept-Encoding";
            var compressed = TryCompressed(root, fileName, http.Request.Headers.AcceptEncoding.ToString(), out var encoding);
            if (compressed is not null)
            {
                http.Response.Headers.ContentEncoding = encoding;
                return Results.File(compressed.CreateReadStream(), ContentType(ext));
            }
            return Results.File(file.CreateReadStream(), ContentType(ext));
        });

        return group;
    }

    private static string ContentType(string ext) => ext switch
    {
        "wasm" => "application/wasm",
        "json" => "application/json",
        "js" => "text/javascript",
        _ => "application/octet-stream"
    };

    private static IFileInfo? TryCompressed(
        IFileProvider root, string fileName, string acceptEncoding, out string encoding)
    {
        foreach (var (suffix, enc) in new[] { (".br", "br"), (".gz", "gzip") })
        {
            if (!acceptEncoding.Contains(enc, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sibling = root.GetFileInfo($"_framework/{fileName}{suffix}");
            if (sibling.Exists && !sibling.IsDirectory)
            {
                encoding = enc;
                return sibling;
            }
        }

        encoding = "";
        return null;
    }
}
