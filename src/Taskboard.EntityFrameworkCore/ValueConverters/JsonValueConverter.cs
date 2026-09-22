using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Taskboard.EntityFrameworkCore.ValueConverters;

public sealed class JsonValueConverter<T> : ValueConverter<T, string>
    where T : class
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public JsonValueConverter()
        : base(
            v => JsonSerializer.Serialize(v, Options),
            v => DeserializeOrDefault(v)!)
    {
    }

    private static T? DeserializeOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(value, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
