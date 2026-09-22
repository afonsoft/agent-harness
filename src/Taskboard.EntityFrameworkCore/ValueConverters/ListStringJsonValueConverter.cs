using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Taskboard.EntityFrameworkCore.ValueConverters;

public sealed class ListStringJsonValueConverter : ValueConverter<List<string>, string>
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ListStringJsonValueConverter()
        : base(
            v => JsonSerializer.Serialize(v, Options),
            v => DeserializeOrEmpty(v))
    {
    }

    // Rows written before a column existed (e.g. NOT NULL DEFAULT '') reach the
    // converter as empty strings; treat empty/invalid payloads as an empty list
    // instead of throwing a JsonException during query materialization.
    internal static List<string> DeserializeOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
