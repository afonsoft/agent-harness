using Taskboard.Domain.Shared.Http;

namespace Taskboard.Cli.Services;

/// <summary>
/// taskctl flavour of the shared API client: translates
/// <see cref="TaskboardApiException"/> into <see cref="CliException"/> with the
/// exit codes taskctl has always used (SPEC-20261003-ops-hardening RF-002).
/// </summary>
public sealed class CliTaskboardApiClient : Taskboard.Domain.Shared.Http.TaskboardApiClient
{
    public CliTaskboardApiClient(string baseUrl, string? apiKey = null, HttpMessageHandler? handler = null)
        : base(baseUrl, apiKey, handler)
    {
    }

    protected override Exception MapError(TaskboardApiException error) => error.Kind switch
    {
        TaskboardApiErrorKind.HttpStatus => new CliException(
            ExitCodeFor(error.StatusCode),
            error.ServerMessage ?? $"Erro {error.StatusCode}: {error.ResponseBody}"),
        TaskboardApiErrorKind.Timeout => new CliException(
            3, $"Timeout ao conectar em {BaseAddress}: {error.InnerException?.Message ?? error.Message}"),
        _ => new CliException(
            3, $"Servidor indisponível em {BaseAddress}: {error.InnerException?.Message ?? error.Message}"),
    };

    private static int ExitCodeFor(int? statusCode) => statusCode switch
    {
        401 or 403 => 4,
        409 => 5,
        400 or 422 => 2,
        _ => 1,
    };
}

public sealed class CliException : Exception
{
    public int ExitCode { get; }

    public CliException(int exitCode, string message)
        : base(message)
    {
        ExitCode = exitCode;
    }
}
