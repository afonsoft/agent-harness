using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Taskboard.EntityFrameworkCore.ValueConverters;

internal static class JsonConverterOptions
{
    // Non-generic holder — a static field in a generic type is per-closed-type (S2743).
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

public sealed class NullableJsonValueConverter<T> : ValueConverter<T?, string?>
    where T : class
{
    public NullableJsonValueConverter()
        : base(ToProvider(), FromProvider())
    {
    }

    private static Expression<Func<T?, string?>> ToProvider()
        => v => v == null ? null : JsonSerializer.Serialize(v, JsonConverterOptions.Options);

    private static Expression<Func<string?, T?>> FromProvider()
        => v => DeserializeOrNull(v);

    private static T? DeserializeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonConverterOptions.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
