using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Server.Chat;

/// <summary>
/// SPEC-20261016-chat-browser-tool RF-001/RF-005: headless-Chromium sessions —
/// one persistent context per conversation (profile dir under
/// <c>&lt;dataDir&gt;/browser/{convId}</c>, cookies survive runs), at most
/// <see cref="MaxSessions"/> live (LRU evict) and idle sessions reaped after
/// <see cref="IdleTimeout"/>.
/// </summary>
public sealed class BrowserSessionPool(
    string dataDir,
    ILogger<BrowserSessionPool> logger,
    Func<Task<IPlaywright>> playwrightFactory) : IBrowserSessionPool, IAsyncDisposable
{
    /// <summary>Live contexts the pool keeps hot (LRU beyond that).</summary>
    public const int MaxSessions = 3;

    /// <summary>RF-005: idle context lifetime before the sweep closes it.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    /// <summary>RF-006: per-action bound.</summary>
    public static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SelectorWait = TimeSpan.FromSeconds(10);
    private const float SettleTimeoutMs = 3000;

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private IPlaywright? _playwright;
    private bool _launchFailed;

    /// <summary>DI entry point — real Playwright lazily on first call.</summary>
    public BrowserSessionPool(string dataDir, ILogger<BrowserSessionPool> logger)
        : this(dataDir, logger, () => Playwright.CreateAsync())
    {
    }

    public async Task<BrowserActionResult> ExecuteAsync(
        string conversationId, BrowserAction action, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return new BrowserActionResult(
                false, null, null,
                Error: "browser unavailable — install with: dotnet playwright install chromium "
                    + "(or pwsh bin/<cfg>/net10.0/playwright.ps1 install chromium)");
        }

        // One action at a time per session — Playwright is not thread-safe.
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ActionTimeout);
            var result = await RunActionAsync(session, action).ConfigureAwait(false);
            session.Touch();
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new BrowserActionResult(
                false, null, null,
                Error: $"action '{action.Action}' timed out after {ActionTimeout.TotalSeconds}s");
        }
        catch (PlaywrightException ex)
        {
            return new BrowserActionResult(false, null, null, Error: ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "browser_use {Action} failed for {ConversationId}", action.Action, conversationId);
            return new BrowserActionResult(false, null, null, Error: ex.Message);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private async Task<SessionEntry?> GetSessionAsync(
        string conversationId, CancellationToken cancellationToken)
    {
        // Idle sweep — cheap enough on the call path; no background timer.
        foreach (var (id, entry) in _sessions.ToArray())
        {
            if (entry.IdleFor > IdleTimeout && _sessions.TryRemove(id, out var stale))
            {
                await stale.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (_launchFailed && !_sessions.ContainsKey(conversationId))
        {
            return null;
        }

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessions.TryGetValue(conversationId, out var existing))
            {
                return existing;
            }

            if (_launchFailed)
            {
                return null;
            }

            // LRU evict down to MaxSessions-1 so the new one fits.
            foreach (var id in BrowserSessionLru.Victims(
                _sessions.ToArray().Select(kv => (kv.Key, kv.Value.LastTouched)),
                MaxSessions - 1))
            {
                if (_sessions.TryRemove(id, out var evicted))
                {
                    await evicted.DisposeAsync().ConfigureAwait(false);
                }
            }

            _playwright ??= await playwrightFactory().ConfigureAwait(false);
            var userDataDir = Path.Join(dataDir, "browser", conversationId);
            Directory.CreateDirectory(userDataDir);
            var context = await _playwright.Chromium.LaunchPersistentContextAsync(
                userDataDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = true,
                    ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
                }).ConfigureAwait(false);
            var page = context.Pages.Count > 0
                ? context.Pages[0]
                : await context.NewPageAsync().ConfigureAwait(false);
            var session = new SessionEntry(context, page);
            _sessions[conversationId] = session;
            return session;
        }
        catch (Exception ex)
        {
            _launchFailed = true;
            logger.LogWarning(ex, "browser session launch failed");
            return null;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private static async Task<BrowserActionResult> RunActionAsync(
        SessionEntry session, BrowserAction action)
    {
        var page = session.Page;
        BrowserActionResult result;
        switch (action.Action)
        {
            case "navigate":
                {
                    var response = await page.GotoAsync(
                        action.Url!, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded })
                        .ConfigureAwait(false);
                    await SettleAsync(page, action.WaitMs).ConfigureAwait(false);
                    result = new BrowserActionResult(
                        response?.Ok ?? true, page.Url, await page.TitleAsync().ConfigureAwait(false),
                        ShotPng: await ShotAsync(page, action.FullPage).ConfigureAwait(false));
                    break;
                }
            case "click":
                await page.WaitForSelectorAsync(
                    action.Selector!, new() { Timeout = (float)SelectorWait.TotalMilliseconds }).ConfigureAwait(false);
                await page.ClickAsync(action.Selector!).ConfigureAwait(false);
                await SettleAsync(page, action.WaitMs).ConfigureAwait(false);
                result = await ResultWithShotAsync(page, action).ConfigureAwait(false);
                break;
            case "type":
                await page.WaitForSelectorAsync(
                    action.Selector!, new() { Timeout = (float)SelectorWait.TotalMilliseconds }).ConfigureAwait(false);
                await page.FillAsync(action.Selector!, action.Text ?? string.Empty).ConfigureAwait(false);
                await SettleAsync(page, action.WaitMs).ConfigureAwait(false);
                result = await ResultWithShotAsync(page, action).ConfigureAwait(false);
                break;
            case "scroll":
                {
                    var (dx, dy) = action.Direction switch
                    {
                        "up" => (0f, -600f),
                        "left" => (-600f, 0f),
                        "right" => (600f, 0f),
                        _ => (0f, 600f),
                    };
                    await page.Mouse.WheelAsync(dx, dy).ConfigureAwait(false);
                    await SettleAsync(page, action.WaitMs).ConfigureAwait(false);
                    result = await ResultWithShotAsync(page, action).ConfigureAwait(false);
                    break;
                }
            case "screenshot":
                result = await ResultWithShotAsync(page, action).ConfigureAwait(false);
                break;
            case "extract_text":
                {
                    var text = await page.InnerTextAsync("body").ConfigureAwait(false);
                    result = new BrowserActionResult(
                        true, page.Url, await page.TitleAsync().ConfigureAwait(false),
                        Text: Trim(text, 8000));
                    break;
                }
            case "extract_html":
                {
                    var html = await page.ContentAsync().ConfigureAwait(false);
                    result = new BrowserActionResult(
                        true, page.Url, await page.TitleAsync().ConfigureAwait(false),
                        Text: Trim(html, 20000));
                    break;
                }
            case "eval_js":
                {
                    var value = await page.EvaluateAsync<object>(action.Script!).ConfigureAwait(false);
                    result = new BrowserActionResult(
                        true, page.Url, await page.TitleAsync().ConfigureAwait(false),
                        Text: Trim(System.Text.Json.JsonSerializer.Serialize(value), 8000));
                    break;
                }
            default:
                result = new BrowserActionResult(false, null, null, Error: $"unknown action '{action.Action}'");
                break;
        }

        // RF-006: recent page console errors ride the observation when asked.
        if (result.Ok && action.IncludeConsole && session.ConsoleErrors.Count > 0)
        {
            var errors = string.Join('\n', session.ConsoleErrors.TakeLast(10));
            session.ConsoleErrors.Clear();
            result = result with
            {
                Text = (result.Text is null ? string.Empty : result.Text + "\n")
                    + $"[console errors]\n{errors}",
            };
        }

        return result;
    }

    private static async Task<BrowserActionResult> ResultWithShotAsync(IPage page, BrowserAction action)
        => new(true, page.Url, await page.TitleAsync().ConfigureAwait(false),
               ShotPng: await ShotAsync(page, action.FullPage).ConfigureAwait(false));

    private static Task<byte[]> ShotAsync(IPage page, bool fullPage)
        => page.ScreenshotAsync(new PageScreenshotOptions { FullPage = fullPage });

    private static async Task SettleAsync(IPage page, int waitMs)
    {
        if (waitMs > 0)
        {
            await page.WaitForTimeoutAsync(waitMs).ConfigureAwait(false);
            return;
        }

        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = SettleTimeoutMs })
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Long-polling pages never idle — settle is best-effort.
        }
    }

    private static string Trim(string text, int max)
        => text.Length <= max ? text : text[..max] + "…[truncated]";

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _sessions.Values)
        {
            await entry.DisposeAsync().ConfigureAwait(false);
        }

        _sessions.Clear();
        _playwright?.Dispose();
    }

    /// <summary>One live context: single page, serialized via <see cref="Gate"/>.</summary>
    private sealed class SessionEntry : IAsyncDisposable
    {
        public SessionEntry(IBrowserContext context, IPage page)
        {
            Context = context;
            Page = page;
            Page.Console += (_, msg) =>
            {
                if (msg.Type == "error" && ConsoleErrors.Count < 50)
                {
                    ConsoleErrors.Add(msg.Text);
                }
            };
        }

        public IBrowserContext Context { get; }
        public IPage Page { get; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public List<string> ConsoleErrors { get; } = [];
        public DateTime LastTouched { get; private set; } = DateTime.UtcNow;

        public TimeSpan IdleFor => DateTime.UtcNow - LastTouched;

        public void Touch() => LastTouched = DateTime.UtcNow;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Context.CloseAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                // Already closed.
            }
        }
    }
}

/// <summary>RF-005/RNF: pure LRU bookkeeping — unit-testable without a browser.</summary>
public static class BrowserSessionLru
{
    /// <summary>
    /// Oldest-touched ids to evict so <paramref name="max"/> stay live
    /// (input is (id, lastTouched) pairs of the sessions currently open).
    /// </summary>
    public static IEnumerable<string> Victims(
        IEnumerable<(string Id, DateTime LastTouched)> live, int max)
    {
        var ordered = live.OrderBy(e => e.LastTouched).ToList();
        return ordered.Count > max ? ordered.Take(ordered.Count - max).Select(e => e.Id) : [];
    }
}
