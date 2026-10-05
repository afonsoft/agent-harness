# SPEC-20261005-chat-background-resume: Detached chat runs, resume, archive & notifications

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Chat background runs — detach, resume, archive, notify |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Domain + Application(.Contracts) + EntityFrameworkCore + Integrations/Server + Blazor WASM + JS interop |
| Change type | Feature / Architecture |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261005-chat-background-resume` |
| Depends on | SPEC-20260929-ai-code-provider-chat (conversations/messages), SPEC-20261001-pr-review-backlog-fixes B-01 (`ChatRunCoordinator`), SPEC-20261003-ai-code-agent-chat (`ChatAgentContext`) |

## 1. Executive Summary

### Problem

The AI Code chat (`/ai-chat`, `ProviderChat`) dies with the browser tab:

1. `POST /api/local/chat/conversations/{id}/messages` streams SSE tied to the
   HTTP request. `ChatRunCoordinator.BeginAsync(conversationId, requestAborted)`
   links the run's cancellation token to `requestAborted` — closing the tab,
   losing the network, or the WASM client disposing the request **cancels the
   turn mid-tool-call**. Only what was already persisted per iteration survives.
2. There is no way to *watch* a running turn again: reopening the conversation
   shows stale persisted messages with no live indicator and no re-attach path.
3. History UX is fragmented: three history affordances — the page-header
   `ClockHistory` button (opens the legacy `ThreadRail` of `AiChatThread`s), the
   `ProviderChat` sidebar ("Histórico" label + `☰` collapse toggle next to
   "+ New chat"), and a **duplicate** `☰ title="Histórico"` inside the
   `ProviderChat` header (shown when the sidebar is collapsed). The only
   per-conversation action is ✕ = hard delete — no archive.
4. No notification on completion — the user must keep the tab open and watch.

The legacy CLI threads already have the durable model the provider chat lacks:
`AiChatRun` + `AiChatEvent` rows record a run's lifecycle and replayable events.
This spec brings the same durability to `ChatConversation`s and builds the
requested UX on top.

### Solution

**Decouple the turn's lifetime from the HTTP request.** A new `ChatRun`
aggregate records each turn; a hosted `ChatRunDispatcherService`
(`BackgroundService`, same pattern as `DelegationDispatcherService`) executes
runs in its own DI scope — no `requestAborted` in the run's token chain.
Clients attach to a run's stream over a replayable SSE endpoint: persisted
messages + a throttled partial-content checkpoint + the live tail. Closing the
browser leaves the run untouched; reopening the conversation re-attaches and
the transcript "continues from where it stopped" — completed turns are
`ChatMessage` rows, the in-flight text is the checkpoint, live events resume.

On top of detached runs:

- **Archive** (`ChatConversation.ArchivedAt`) replaces delete as the default
  list action; archived items are hidden behind an "Arquivadas" filter with
  Restore + permanent Delete.
- **Single history surface**: the `ProviderChat` sidebar is the one history
  (search + active/archived + running badges); the duplicate `☰` in the chat
  header is removed; the page-header history button drives the same drawer.
- **Notifications** on run completion: in-app toast + browser Notification API
  (opt-in, permission requested), configurable in Settings; a new lightweight
  `ChatRunHub` (SignalR) carries `run.*` events to any open page. Web Push for
  a fully-closed browser is a documented Phase 3.

### Scope

In scope: provider/agent-mode chat conversations (`ChatConversation`,
`ChatMessage`), the send/stream/stop endpoints, `ProviderChat.razor` and the
`AiChat.razor` header/rail, runtime config catalog, EF migrations, WASM client
services (`TaskboardClient`, notification JS interop), tests.

Out of scope: legacy `AiChatThread` execution engine (kept as-is; its rail is
folded into the unified drawer as a collapsed "Legacy threads" section),
email/webhook notification channels, multi-user notification routing (single
admin app today), mobile push.

## 2. Requisitos

### RF-001 — `ChatRun` aggregate + migration

New entity `Taskboard.Domain/Entities/Chat/ChatRun.cs` —
`AggregateRoot<ChatRunId>`:

| Member | Type | Notes |
|---|---|---|
| `ConversationId` | `ChatConversationId` | owner |
| `TriggerMessageId` | `ChatMessageId` | the user message that opened the run |
| `Status` | `ChatRunStatus` (value object in Domain.Shared) | `Queued` `Running` `Completed` `Failed` `Stopped` `Interrupted` |
| `PartialContent` | `string?` | throttled checkpoint of the in-flight assistant text |
| `PartialReasoning` | `string?` | same for reasoning_content |
| `Error` | `string?` | provider/tool failure, or "stopped by user" |
| `TokensIn`/`TokensOut` | `int?` | accumulated usage (today's `TurnState`) |
| `CreatedAt`/`StartedAt`/`FinishedAt` | `DateTime`/`DateTime?` | existing `DateTime` convention |

Methods: `Create`, `Start(now)`, `Checkpoint(content, reasoning)`,
`Complete(tokensIn, tokensOut, now)`, `Fail(error, now)`, `Stop(now)`,
`Interrupt(now)`. EF config in `ChatConfigurations` (+ `ChatRunConfiguration`),
migration `AddChatRunsAndConversationArchive` (also adds `ArchivedAt` below).
Runs older than `Taskboard:Chat:Runs:RetentionDays` (default 30) are swept by
the existing cleanup managed job.

### RF-002 — `ChatRunDispatcherService` (hosted executor)

`src/Taskboard.Integrations/Chat/ChatRunDispatcherService.cs` —
`BackgroundService` (delegation dispatcher precedent: `IServiceScopeFactory`,
`ILogger`, `TimeProvider`):

- Run queue: `Channel<string>` of runIds (unbounded, single reader) +
  on-boot `UPDATE ChatRuns SET Status=Interrupted WHERE Status IN (Queued,Running)`
  (server restart orphans).
- Per-conversation serialization: `ConcurrentDictionary<string, SemaphoreSlim>`
  — max **one** `Running` run per conversation; a send during a run persists
  its user message and enqueues a `Queued` run (FIFO) — this is the
  follow-up-queue behavior real chat UIs have. Global cap
  `Taskboard:Chat:Runs:MaxConcurrent` (default 4) across conversations.
- Execution = today's `ChatService.StreamTurnAsync` refactored to
  `RunDetachedAsync(conversationId, triggerMessageId, run, broadcaster, ct)`
  inside a dispatcher-owned scope: same tool loop, same per-iteration
  `ChatMessage` persistence, same `InlineToolCallMarkup` filtering — but the
  token chain contains **only** the run CTS (`ChatRunCoordinator`), never
  `requestAborted`.
- Checkpointing: `PartialContent`/`PartialReasoning` flushed at most every
  `Taskboard:Chat:Runs:CheckpointMs` (default 750 ms) and always on
  `tool_call`/`tool_result`/`done`; cleared on terminal status.
- Fan-out: every emitted `ChatStreamEvent` also goes to
  `ChatRunBroadcaster` (below) and final status to `ChatRunHub`.

### RF-003 — `ChatRunBroadcaster` + attach stream (reconnectable)

`ChatRunBroadcaster` (singleton, Application): per-run
`Channel<ChatStreamEvent>` fan-out (TryWrite, N live subscribers) +
`GetSnapshot(runId)` → `{ messages, partialContent, partialReasoning, status }`.

`GET /api/local/chat/conversations/{id}/runs/{runId}/stream` — SSE:

```
event: chat.sync       → { messages: ChatMessageDto[], run: ChatRunDto,
                           partial: string?, reasoning: string? }
event: chat.delta | chat.reasoning | chat.tool_call | chat.tool_result |
       chat.status   → live events (same payloads as today)
event: chat.done       → { run: ChatRunDto }
```

Replay = durable state (persisted messages + checkpoint), so attach is
idempotent and needs no `Last-Event-ID` cursor. Attaching to a finished run
returns `chat.sync` + `chat.done` and closes. Client reconnects with
exponential backoff while `run.Status ∈ {Queued, Running}`.

### RF-004 — Endpoints (messages becomes enqueue)

| Endpoint | Change |
|---|---|
| `POST /conversations/{id}/messages` | now returns **202** `{ run: ChatRunDto }`; persists the user `ChatMessage`, creates `ChatRun(Queued)`, signals the dispatcher. No SSE body. |
| `GET /conversations/{id}` | `ChatConversationDetailDto` gains `ActiveRun: ChatRunDto?` + `LastRun: ChatRunDto?` |
| `GET /conversations?q=&archived=` | `archived` filter (default false); `ChatConversationDto` gains `ArchivedAt: DateTime?` and `ActiveRunStatus: string?` (for the running badge) — appended optional fields like `Agent` |
| `POST /conversations/{id}/archive` | sets `ArchivedAt`; 404-safe |
| `POST /conversations/{id}/unarchive` | clears `ArchivedAt` |
| `DELETE /conversations/{id}` | unchanged (hard delete; cancels a live run first via `ChatRunCoordinator`) |
| `POST /conversations/{id}/stop` | cancels the active run through `ChatRunCoordinator` (unchanged contract); `Queued` runs are cancelled before dispatch |

`SendChatMessageRequest` unchanged. `ChatRunDto`:
`{ id, conversationId, status, triggerMessageId, error, tokensIn, tokensOut,
createdAt, startedAt, finishedAt }`.

### RF-005 — Client: send → attach → auto-resume

`ProviderChat.razor` + `TaskboardClient`:

- `SendAsync` → `EnqueueChatMessageAsync` (202 + run) → `AttachToRunAsync`:
  opens the attach SSE, handles `chat.sync` (replace `_messages`, seed
  `_streaming`/`_reasoning` from checkpoint) then live events; on transport
  loss while the run is `Queued|Running` → re-attach with backoff (already
  the precedent from hub reconnects).
- `OpenConversationAsync`: `detail.ActiveRun != null` → show the run state and
  auto-attach — **"continues from where it stopped"** = persisted messages +
  checkpoint + live tail. A `Stopped`/`Failed`/`Interrupted` run shows a small
  inline notice ("turn interrupted — send again to continue").
- `_running` is driven by `ActiveRunStatus`, not local stream state — a second
  browser/tab opening the same conversation sees the run live too.
- Composer during a run stays enabled: the send enqueues (RF-002 FIFO); the
  queued user bubble renders immediately with a `queued` chip (it is already
  persisted — same display order wart as ChatGPT: assistant reply to msg N
  lands after queued msg N+1).
- Stop button → `POST /stop` (same); partial text persists as an assistant
  message (existing `StoppedByUser` path, now under the detached executor).

### RF-006 — Archive (soft-delete) UX

- `ChatConversation.ArchivedAt: DateTime?` + `Archive(now)`/`Unarchive(now)`.
- Sidebar item actions: **archive icon** (`IconName.Archive`) replaces ✕ as
  the primary action — "arquivar a um histórico"; the item leaves the active
  list immediately.
- Sidebar gains a segmented filter **Ativas | Arquivadas** (next to search).
  Archived items show **restore** (`IconName.ArrowCounterclockwise`) and
  **delete** (✕, permanent) actions. Conversations with an active run cannot
  be archived while running (409 → toast "stop the run first") — or archive
  allowed and the run keeps going in background; pick the latter: archive is
  a view flag, not a state change.
- Archived conversations still open read-only (banner "archived"), send
  blocked until restored — keeps "history" truthful.

### RF-007 — Single history surface; remove duplicate header button

- Delete the `☰ title="Histórico"` block in `ProviderChat`'s chat header
  (`ProviderChat.razor` ≈ lines 46–49) — the duplicate the user flagged.
- Collapsed sidebar keeps a **slim rail** (~44 px) with expand `☰` and `+`
  icons, so reopening does not depend on the removed header button.
- `AiChat.razor` page header: in provider/agent chat modes the `ClockHistory`
  button toggles the ProviderChat sidebar (via a `SidebarOpen` two-way bind or
  a small `IChatHistoryDrawer` service) and `PlusSquare` calls
  `StartNewChatAsync` — one history entry, one new-chat entry. The legacy
  `ThreadRail` (AiChatThreads from the removed Assistant mode) moves to a
  collapsed **"Legacy threads"** section inside that same drawer.
- Running badge: list items show a live spinner/`running` chip when
  `ActiveRunStatus` is `Running`/`Queued` — visible even after a fresh login.

### RF-008 — Notifications on run completion

`ChatRunHub` (new SignalR hub `/chat-run-hub`, auth-required like
`HarnessCockpitHub`): the dispatcher publishes `run.completed`
`{ conversationId, title, status, error? }` (and optionally `run.started`).
Server-side publisher behind `IChatRunNotifier` so tests substitute it.

Client `ChatNotificationsService` (Blazor singleton): connects the hub (same
`HubConnectionBuilder` pattern as `Cockpit.razor`), applies prefs, on
`run.completed`:

- **In-app toast** (`ToastService`) — always available while the app is open.
- **Browser Notification API** via new `wwwroot/js/chatNotifications.js`:
  `requestPermission()`, `show(title, body, conversationId)`; click focuses
  the tab and navigates to `/ai-chat` opening the conversation. Fires when
  `document.hidden` (or always, per pref).

Preferences (Settings → AI Chat section, runtime catalog entries, all
`Editable: true, RequiresRestart: false`):

| Key | Default | Meaning |
|---|---|---|
| `Taskboard:Chat:Notify:Done:InApp` | `true` | toast on run completion |
| `Taskboard:Chat:Notify:Done:Browser` | `false` | Notification API — opt-in triggers `requestPermission()` |
| `Taskboard:Chat:Notify:Done:Push` | `false` | Phase 3 Web Push master switch |

Per-browser override in `localStorage` (`harness.chat.notify.*`, same pattern
as `harness.locale`) so a shared workstation can mute without touching global
config.

### RF-009 — Phase 3 (documented, optional): Web Push

For "site fully closed" delivery: `service-worker.js` + minimal manifest,
`ChatPushSubscription` table (`endpoint`, `p256dh`, `auth`, `userAgent`,
`createdAt`), `POST|DELETE /api/local/push/subscriptions`, VAPID via
`Taskboard:Push:Vapid:{PublicKey,PrivateKey,Subject}` (private key as a
secret, never in the repo). Dispatcher → `IChatRunNotifier` → Web Push
protocol POST. Deferred because the app has no SW/PWA today; Phase 2 covers
tab-open and background-tab cases already.

## 3. Arquitetura

```
ProviderChat (WASM)                Server
  SendAsync ──POST /messages──▶  ChatService.EnqueueAsync
                                    │ persists ChatMessage + ChatRun(Queued)
                                    ▼ signals channel
                            ChatRunDispatcherService (hosted, own scope)
                                    │ one Running/conversation, FIFO queue
                                    ▼
                            ChatService.RunDetachedAsync  (today's StreamTurnAsync,
                                    │   minus requestAborted)
                                    ├─▶ ChatMessage rows (per iteration, as today)
                                    ├─▶ ChatRun.Checkpoint (750 ms throttle)
                                    └─▶ ChatRunBroadcaster ──▶ SSE attach
                                      (GET .../runs/{runId}/stream)
                                    └─▶ ChatRunHub ──▶ ChatNotificationsService
                                                      → toast / Notification API
  OpenConversation ──GET /conv──▶ detail {messages, activeRun}
       │ ActiveRun? → AttachToRunAsync (SSE)  ◀── replay + live tail
```

`ChatRunCoordinator` keeps its name/API (`Begin`→register run CTS,
`Stop`, `End`) but the CTS no longer links to `requestAborted`; it gains a
`TryGetActive(conversationId)` for badges/`detail.ActiveRun`.

Files (new): `Domain/Entities/Chat/ChatRun.cs`,
`Domain.Shared/ValueObjects/{ChatRunId,ChatRunStatus}.cs`,
`Application/Chat/ChatRunBroadcaster.cs`,
`Integrations/Chat/ChatRunDispatcherService.cs`,
`Server/Hubs/ChatRunHub.cs`, `Blazor/Services/ChatNotificationsService.cs`,
`Client/wwwroot/js/chatNotifications.js`,
`EntityFrameworkCore/Configurations/ChatRunConfiguration.cs` + migration.
Touched: `ChatService` (split enqueue/execute), `ChatRunCoordinator`,
`ChatConversation` (+`ArchivedAt`), `ChatConfigurations`, `ChatDtos`,
`Program.cs` (endpoint swap + hub map), `TaskboardClient`,
`ProviderChat.razor(+css)`, `AiChat.razor`, `Settings.razor`,
`RuntimeConfigurationService` (3 keys), cleanup job (run retention).

## 4. Config

| Key | Default | Editable | Restart |
|---|---|---|---|
| `Taskboard:Chat:Runs:MaxConcurrent` | `4` | ✓ | ✗ |
| `Taskboard:Chat:Runs:CheckpointMs` | `750` | ✓ | ✗ |
| `Taskboard:Chat:Runs:RetentionDays` | `30` | ✓ | ✗ |
| `Taskboard:Chat:Notify:Done:InApp` | `true` | ✓ | ✗ |
| `Taskboard:Chat:Notify:Done:Browser` | `false` | ✓ | ✗ |
| `Taskboard:Chat:Notify:Done:Push` | `false` | ✓ | ✗ |
| `Taskboard:Push:Vapid:PublicKey` / `:PrivateKey` / `:Subject` | — | ✓ | ✓ |

## 5. Segurança

- All new endpoints sit under the existing `api/local/chat` group → same auth
  requirement (`RequireAuthorization` on the API group). Attach stream is
  GET-with-auth — SSE through `HttpClient` keeps the bearer header.
- `ChatRunHub` mapped with `.RequireAuthorization()` like `TerminalHub`.
- No secrets logged: `PartialContent` is user-visible data, fine to persist —
  provider API keys never enter run rows.
- Notification payloads carry title + status only (no message bodies) — content
  stays behind auth.
- VAPID private key: config/secret store only; never committed.
- Web Push subscriptions are single-user (admin) today — no per-user routing
  needed; revisit when multi-user lands.

## 6. Testes

Unit (`Taskboard.Tests.Unit`, pt-BR `Dado_Quando_Entao`):

- `ChatRunDispatcherServiceTests`: enqueue → executes; second send same
  conversation → `Queued`, runs FIFO after; stop mid-run → `Stopped`, partial
  assistant message persisted; boot sweep marks leftovers `Interrupted`;
  `MaxConcurrent` cap respected.
- `ChatRunTests` (domain): state machine transitions legal/illegal;
  `Checkpoint` updates fields only.
- `ChatServiceEnqueueTests`: `messages` POST → 202 + `ChatRunDto`, user
  message persisted; conversation missing → 404; `DELETE` during run → run
  cancelled.
- `ChatConversationArchiveTests`: archive/unarchive flags + UpdatedAt bump;
  list `archived=` filter both ways.
- `ChatRunBroadcasterTests`: snapshot = messages + checkpoint; late
  subscriber on finished run gets `sync`+`done`.
- `ChatNotificationsServiceTests`/JS interop mocked: pref off → no
  `show()`; permission denied → toast fallback.

Integration (`Taskboard.Tests.Integration`, `WebApplicationFactory`):

- `ChatDetachedRunEndpointsTests`: POST messages → 202; attach stream emits
  `chat.sync`+`chat.done`; **disconnect mid-run → GET detail still shows
  `ActiveRun`, second attach replays persisted state**; archive → hidden from
  default list, visible with `?archived=true`, restore works; stop → 202.
- Reuse the existing fake-provider SSE pattern from chat endpoint tests.

Razor-source guard (existing `MobileResponsiveTests` style): assert no
`title="Histórico"` button remains inside `provider-chat-header`.

## 7. Fases

- **P1 — detached runs + resume + archive + history cleanup**: RF-001..007.
  The headline: close the browser mid-turn, reopen, watch it finish.
- **P2 — notifications**: RF-008 (hub + toast + Notification API + settings).
- **P3 — Web Push (optional)**: RF-009 — requires SW/PWA groundwork; only
  needed for notifications with the browser *process* closed (execution
  itself already works closed after P1).

## Acceptance criteria

- Send a message, close the tab mid-run → run completes server-side; reopen
  the conversation → full transcript present, `chat.done` state visible.
- Send, minimize/reload mid-run → on reload the live stream re-attaches at the
  checkpoint and continues rendering to `chat.done`.
- Two tabs on the same conversation both mirror the run (read-only attach).
- Archive icon on a conversation → it leaves "Ativas", appears under
  "Arquivadas"; restore returns it; delete removes permanently.
- Exactly one history entry point in chat mode; no `☰` inside the chat header.
- With `Notify:Done:Browser` on and permission granted → a browser
  notification appears on completion with the tab unfocused; click opens the
  conversation. In-app toast fires regardless.
- `POST /messages` during a run → message queues; it is the next turn's
  trigger — no second concurrent run on the conversation.

## Open questions

1. Queued-send cap per conversation — unlimited FIFO (proposed) vs. replace?
   Propose unlimited; revisit if abuse shows.
2. Archived conversations restore point — keep `UpdatedAt` bump on archive
   (proposed: no — archive shouldn't reorder history; bump only on unarchive).
3. Notification-per-run vs per-conversation coalescing when a queue drains —
   P2 implements per-run; coalesce later if noisy.
4. Web Push needs HTTPS + a stable base URL (VAPID `subject:` mailto/site);
   local self-host may stay on Phase 2 notifications only.
