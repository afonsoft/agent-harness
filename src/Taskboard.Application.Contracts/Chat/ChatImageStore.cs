namespace Taskboard.Application.Contracts.Chat;

/// <summary>
/// Persists generated images under <c><dataDir>/chat-images</c> and
/// resolves them for the authenticated image endpoint (RF-009).
/// </summary>
public sealed class ChatImageStore(string dataDir)
{
    public string? Save(string base64)
    {
        try
        {
            var dir = Path.Combine(dataDir, "chat-images");
            Directory.CreateDirectory(dir);
            var file = $"{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}.png";
            File.WriteAllBytes(Path.Combine(dir, file), Convert.FromBase64String(base64));
            return file;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Resolves a stored image path, rejecting traversal (RF-009).</summary>
    public string? Resolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/', StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal)
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var full = Path.Combine(dataDir, "chat-images", fileName);
        return File.Exists(full) ? full : null;
    }
}
