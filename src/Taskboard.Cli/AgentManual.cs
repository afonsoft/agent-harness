using System.Reflection;
using System.Text.Json.Nodes;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;

namespace Taskboard.Cli;

/// <summary>
/// SPEC-20261104-cli-agent-manual: embedded agent manual + machine-readable
/// manifest. The manual ships inside the binary (always version-matched);
/// <see cref="RegisteredCommands"/> is captured from the real
/// <see cref="Program.ConfigureCommands"/> registration so the manifest and
/// drift checks share one source of truth.
/// </summary>
internal static class AgentManual
{
    private const string ResourceName = "Taskboard.Cli.Resources.agent-manual.md";

    /// <summary>Exit code for invalid --skill usage.</summary>
    internal const int UsageError = 2;

    /// <summary>Reads the embedded manual text.</summary>
    internal static string Render()
    {
        var assembly = typeof(AgentManual).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Names of every command registered in the command tree — captured by
    /// running <see cref="Program.ConfigureCommands"/> against a recording
    /// configurator. Ordered as registered.
    /// </summary>
    internal static IReadOnlyList<string> RegisteredCommands()
    {
        var capture = new CommandCatalogConfigurator();
        Program.ConfigureCommands(capture);
        return capture.Names;
    }

    /// <summary>JSON manifest for <c>--skill --json</c>.</summary>
    internal static JsonObject Manifest()
    {
        var version = typeof(AgentManual).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        return new JsonObject
        {
            ["version"] = version,
            ["commands"] = new JsonArray(RegisteredCommands().Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            // Exit-code categories: usage=2, server=1 (other HTTP status),
            // transport/timeout=3, auth=4, conflict=5, cancelled=130.
            ["errorKinds"] = new JsonArray("usage", "server", "transport", "timeout", "auth", "conflict", "cancelled"),
        };
    }

    /// <summary>Handles <c>taskctl --skill [--json]</c> / <c>taskctl --agent-manual</c>.</summary>
    internal static int Run(IReadOnlyList<string> args)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var unknown = args.FirstOrDefault(a => a is not ("--skill" or "--agent-manual" or "--json"));
        if (unknown is not null)
        {
            Console.Error.WriteLine($"error: opção desconhecida '{unknown}' para --skill.");
            return UsageError;
        }

        Console.Out.WriteLine(json ? Manifest().ToJsonString() : Render());
        return 0;
    }

    /// <summary>True when <paramref name="args"/> is a --skill invocation.</summary>
    internal static bool IsSkillInvocation(IReadOnlyList<string> args) =>
        args.Count > 0 && args.All(a => a is "--skill" or "--agent-manual" or "--json")
        && args.Any(a => a is "--skill" or "--agent-manual");

    /// <summary>
    /// IConfigurator that only records registered command names — used by the
    /// manifest and by drift tests so both share the real registration table.
    /// The command tree has no branches today; anything beyond AddCommand
    /// throws so a future branch/delegate registers loudly instead of being
    /// silently cataloged wrong.
    /// </summary>
    private sealed class CommandCatalogConfigurator : IConfigurator
    {
        internal readonly List<string> Names = [];

        public ICommandAppSettings Settings => throw new NotSupportedException();

        public ICommandConfigurator AddCommand<T>(string name) where T : class, ICommand
        {
            Names.Add(name);
            return new RecordedCommandConfigurator();
        }

        public ICommandConfigurator AddDelegate<TSettings>(string name, Func<CommandContext, TSettings, CancellationToken, int> func)
            where TSettings : CommandSettings
            => throw new NotSupportedException("delegates not cataloged");

        public ICommandConfigurator AddAsyncDelegate<TSettings>(string name, Func<CommandContext, TSettings, CancellationToken, Task<int>> func)
            where TSettings : CommandSettings
            => throw new NotSupportedException("delegates not cataloged");

        public IBranchConfigurator AddBranch<TSettings>(string name, Action<IConfigurator<TSettings>> action)
            where TSettings : CommandSettings
            => throw new NotSupportedException("branches not cataloged");

        public IConfigurator AddExample(params string[] args) => this;

        public IConfigurator SetHelpProvider(IHelpProvider helpProvider) => this;

        public IConfigurator SetHelpProvider<T>() where T : IHelpProvider => this;

        private sealed class RecordedCommandConfigurator : ICommandConfigurator
        {
            public ICommandConfigurator IsHidden() => this;
            public ICommandConfigurator WithAlias(string alias) => this;
            public ICommandConfigurator WithData(object data) => this;
            public ICommandConfigurator WithDescription(string description) => this;
            public ICommandConfigurator WithExample(params string[] args) => this;
        }
    }
}
