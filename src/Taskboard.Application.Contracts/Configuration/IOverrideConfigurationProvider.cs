namespace Taskboard.Application.Contracts.Configuration;

/// <summary>
/// Marks a configuration provider whose values are database-stored overrides.
/// Consumers that bypass <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// (e.g. dedicated environment variables) can still honor DB precedence by checking
/// this interface on the root's providers.
/// </summary>
public interface IOverrideConfigurationProvider
{
    /// <summary>Returns the database override for <paramref name="key"/>, if one exists.</summary>
    bool TryGetOverride(string key, out string? value);
}
