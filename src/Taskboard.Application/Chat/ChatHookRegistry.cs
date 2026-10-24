using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Application.Chat;

/// <summary>
/// SPEC-20261024-p1-agent-reliability RF-E01/E03: the chat hook engine —
/// resolves registered <see cref="IChatHook"/>s by lifecycle event + regex
/// matcher on the event subject, runs them in registration order and folds
/// verdicts (block &gt; warn &gt; allow). A block short-circuits the remaining
/// hooks. Hook exceptions honor <see cref="IChatHook.BlockOnFailure"/>
/// (fail-closed) and otherwise fail open with a warning. Inert when
/// <see cref="EnabledKey"/> is <c>false</c> (default on).
/// </summary>
public sealed class ChatHookRegistry(
    IEnumerable<IChatHook> hooks,
    IConfiguration configuration,
    ILogger<ChatHookRegistry>? logger = null) : IChatHookRegistry
{
    /// <summary><c>Taskboard:Chat:Hooks:Enabled</c> — engine master switch (default on).</summary>
    public const string EnabledKey = "Taskboard:Chat:Hooks:Enabled";

    private static readonly RegexOptions MatchOptions =
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private readonly IReadOnlyList<IChatHook> _hooks = hooks.ToList();
    private readonly ConcurrentDictionary<IChatHook, Regex?> _matchers = new();

    public bool Enabled =>
        !bool.TryParse(configuration[EnabledKey], out var enabled) || enabled;

    public IReadOnlyList<IChatHook> Resolve(string eventName, string subject)
    {
        var matched = new List<IChatHook>();
        foreach (var hook in _hooks)
        {
            if (!string.Equals(hook.Event, eventName, StringComparison.Ordinal))
            {
                continue;
            }

            var matcher = _matchers.GetOrAdd(hook, static h => CompileMatcher(h.Matcher));
            if (matcher is null)
            {
                continue;
            }

            try
            {
                if (matcher.IsMatch(subject))
                {
                    matched.Add(hook);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Fail-closed à la SensitivePathDenylist: a pathological
                // matcher never matches instead of stalling the pipeline.
            }
        }

        return matched;
    }

    public async Task<HookRunResult> RunAsync(HookContext context, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return HookRunResult.Allow;
        }

        var verdicts = new List<HookVerdict>();
        foreach (var hook in Resolve(context.Event, context.Subject))
        {
            HookVerdict verdict;
            try
            {
                verdict = await hook.RunAsync(context, cancellationToken).ConfigureAwait(false)
                    ?? HookVerdict.Allow;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "chat hook {Hook} on {Event} failed", hook.Name, context.Event);
                if (!hook.BlockOnFailure)
                {
                    continue;
                }

                verdict = HookVerdict.Block($"hook '{hook.Name}' failed: {ex.Message}");
            }

            if (verdict.Kind == HookVerdictKind.Allow)
            {
                continue;
            }

            verdicts.Add(verdict with { Hook = verdict.Hook ?? hook.Name });
            if (verdict.Kind == HookVerdictKind.Block)
            {
                break;
            }
        }

        return new HookRunResult(
            verdicts.Any(v => v.Kind == HookVerdictKind.Block)
                ? HookVerdictKind.Block
                : verdicts.Count > 0 ? HookVerdictKind.Warn : HookVerdictKind.Allow,
            verdicts);
    }

    private static Regex? CompileMatcher(string pattern)
    {
        try
        {
            return new Regex(pattern, MatchOptions, MatchTimeout);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
