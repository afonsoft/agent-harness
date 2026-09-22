using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Taskboard.EntityFrameworkCore.ValueConverters;

/// <summary><see cref="ListStringJsonValueConverter"/> for <c>IReadOnlyList&lt;string&gt;</c> properties.</summary>
public sealed class ReadOnlyListStringJsonValueConverter : ValueConverter<IReadOnlyList<string>, string>
{
    public ReadOnlyListStringJsonValueConverter()
        : base(
            v => JsonSerializer.Serialize(v, ListStringJsonValueConverter.Options),
            v => (IReadOnlyList<string>)ListStringJsonValueConverter.DeserializeOrEmpty(v))
    {
    }
}
