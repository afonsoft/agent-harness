# SPEC-20260922-ai-chat-command-bar

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-chat-command-bar` |
| Type | `Feature` + `Bugfix` (Frontend + API) |
| Stack | `Blazor WASM / .NET 10 / ASP.NET Core Minimal APIs / SSE` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260922-ai-chat-command-bar` |
| Ticket | [#329](https://github.com/afonsoft/agent-harness/issues/329) |
| Status | `Done — delivered in PR #332` |
| Referência | SPEC-20260918-ai-chat-threads, SPEC-20260919-web-cli-agent, SPEC-20260921-ai-chat-cli-backend, SPEC-20260921-ai-code-chat-ux, SPEC-20260921-ai-code-thread-config |

## 1. User Story

**As a** Harness operator using AI Code
**I want** a chat-first screen — history behind an icon, configuration on a
bottom command bar that locks once the conversation starts — and a console
free of 400/404/502 errors and a11y warnings
**So that** the screen looks and behaves like a modern agent chat instead of
a form + sidebar, and failures tell me *why* instead of logging opaque HTTP
errors.

**Problem context — observed console errors and their root causes:**

| Console error | Root cause (verified in code) |
| --- | --- |
| `400 Bad Request` on thread create | `POST /api/local/ai/threads` throws `DomainException` (invalid `AgentType`, invalid `ModelTier`, agent-mode repo without a local clone under `~/repos` — `AiChatService.CreateThreadAsync`). `TaskboardClient.CreateAiChatThreadAsync` returns `null` and drops the problem-details `detail`, so the toast says only "Failed to create the thread". |
| `404` on `api/local/ai/threads/{id}` | There is **no `GET threads/{id}` endpoint** — the only call on that URL is `DELETE`. A delete on a stale row (thread deleted in another tab/session, or double-click) returns 404 and the UI reports failure even though the desired end state (thread gone) is already true. |
| `502 badgateway` on `threads/{id}/events` | The SSE handler writes with the request-aborted `CancellationToken`. When the browser's `EventSource` disconnects (page nav, proxy idle timeout), `OperationCanceledException` propagates out of the handler → Kestrel aborts the connection mid-response → nginx sees an upstream reset → **502**. The stream also has no heartbeat, so `proxy_read_timeout` (default 60s) kills idle connections behind the nginx/Cloudflare front the app already honours via `UseForwardedHeaders`. |
| `Blocked aria-hidden … focused element` | Blazor.Bootstrap `Modal.HideAsync()` applies `aria-hidden="true"` + `display:none` while the clicked `btn-primary` (e.g. "Create thread") still holds focus → Chrome a11y warning. Focus must leave the modal before it hides. |

## 2. Scope

**In scope:**

- SSE `/events` resilience: clean close on client disconnect, heartbeat
  comments, `404` for unknown threads (RF-001).
- Thread endpoint hardening: `GET /api/local/ai/threads/{id}` and idempotent
  delete semantics (RF-002).
- Client error surfacing: `TaskboardClient` reads `problem+json` `detail`
  and toasts it (RF-003).
- Focus/a11y fix for every modal close path (RF-004).
- New `/ai-chat` layout: icon-driven history + New button, chat-only main
  area, bottom command bar with Mode / Agent CLI / Modo / repo / workspace /
  model / sandbox controls (RF-005–RF-007).
- Lazy thread creation on first send + auto-title; config lock while a
  conversation is active (RF-007).
- History popup with select + delete (RF-008).
- Tests (RF-009).

**Out of scope:**

- nginx/ingress configuration files — the heartbeat makes the stream robust
  regardless; a docs note records the recommended `proxy_buffering off` for
  SSE locations.
- Changing the agent-run pipeline, permission flow, or tool-call renderers.
- Thread rename/edit UI; pagination/search of the history list (list is
  small today — revisit when it isn't).
- Removing `NewThreadDialog`/`RunAgentDialog` files outright — they are
  superseded by the command bar; deletion is allowed if no caller remains.

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Server/Program.cs` — `GET local/ai/threads/{id}/events`
  (RF-001), new `GET local/ai/threads/{id}` + delete semantics (RF-002).
- `src/Taskboard.Blazor/Services/TaskboardClient.cs` — problem-details
  parsing helper shared by the AI-chat calls (RF-003).
- `src/Taskboard.Client/wwwroot/js/taskboard.js` — `blurActiveElement`
  helper (RF-004); no SSE changes needed (EventSource auto-reconnect is
  kept).
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — layout rework
  (RF-005–RF-008). Sidebar markup is removed; thread list moves into the
  history modal.
- `src/Taskboard.Blazor/Components/Pages/NewThreadDialog.razor` — replaced
  by the command bar; keep or delete once unreferenced.
- `src/Taskboard.Client/wwwroot/css/site.css` — `.ai-chat-*` styles:
  sidebar rules removed, new `.ai-chat-commandbar` rules.
- `src/Taskboard.Application/AiChat/AiChatService.cs` — `GetThreadAsync`
  already exists and backs the new GET; `DeleteThreadAsync` unchanged.
- `src/Taskboard.Blazor/Components/RepositoryCombobox.razor` — reused
  inside the repo popup.
- `AgentControlService.GetScopeStateAsync` (`thread` scope) already returns
  `Ok` with `sessionInfoJson` — unchanged; the ACP `Modo`/`Model`
  quick-switch keeps using `POST /api/agents/control` (`set_mode`,
  `set_config`).

**Existing pieces reused as-is:**

- `AiChatThreadDto` already carries `Mode`, `AgentType`, `WorkspacePath`,
  `RepositoryFullName`, `Model`, `ModelTier`, `Sandbox` — enough to render
  the locked bar from a selected thread without new fields.
- `Sandbox` VO: `read-only | workspace-write | danger-full-access`.
- `IWorkspacePathResolver.ResolveCardWorkdir(repo)` already resolves
  `~/repos/<name>` and the create path already returns 400 when the clone is
  missing — the new UI surfaces that message instead of hiding it.
- Ordered-by-`UpdatedAt` thread list, delete-with-inline-confirm, SSE
  connect/dedupe logic — lifted from the current sidebar into the popup.

## 4. Requirements

### RF-001: SSE `/events` resilience

- **Description:** the stream must survive client disconnects and proxy
  idle timeouts without producing upstream aborts (502) — and must not hang
  on threads that do not exist.
- **Rules:**
  - Look the thread up first; unknown id → `404` `THREAD_NOT_FOUND` (both
    JSON-snapshot and SSE modes).
  - Wrap the write loop: `OperationCanceledException` /
    `HttpContext.RequestAborted` ends the handler **normally** (return
    `Results.Empty`), never as a thrown mid-response abort.
  - Emit a heartbeat comment `: hb\n\n` every `SseHeartbeatSeconds`
    (default `15`, config `Taskboard:AiChat:SseHeartbeatSeconds`) so idle
    streams outlive proxy read timeouts.
  - Keep backlog replay + live events + `Accept: application/json`
    snapshot mode unchanged.
- **Input → Output:** EventSource connect/disconnect → clean stream with
  no console 502s; unknown thread → 404.

### RF-002: Thread read/delete endpoints

- **Description:** complete the resource surface used by the new UI.
- **Rules:**
  - `GET /api/local/ai/threads/{id}` → `200 { thread }` or `404`
    `THREAD_NOT_FOUND` (backs history-popup refresh and thread reload).
  - `DELETE /api/local/ai/threads/{id}` becomes idempotent: missing thread
    → `204` (documented change; UI no longer sees 404 noise on stale rows).
- **Input → Output:** GET existing → dto; DELETE missing → 204.

### RF-003: Error surfacing in `TaskboardClient`

- **Description:** non-2xx responses carry `application/problem+json`
  (`detail`, `code`) — the client must read it and the UI must show it.
- **Rules:**
  - Add a shared `ReadErrorAsync(HttpResponseMessage)` helper that extracts
    `detail` (fallback: `code`, then status text).
  - AI-chat calls that currently return `null`/`false` on failure
    (`CreateAiChatThreadAsync`, `PostAiChatEventAsync`,
    `QueueAgentThreadPromptAsync`, `ForkAiChatThreadAsync`, `StartAiChatRunAsync`)
    return a small result record `(T? Value, string? Error)` — or an
    out-param pattern consistent with the file's existing style.
  - `AiChat.razor` toasts the server `detail` (e.g. *"Repository 'x/y' has
    no local workspace — clone it under ~/repos"*).
- **Input → Output:** `400/422` create → toast with the real reason instead
  of "Failed to create the thread".

### RF-004: Focus out before modal hide (aria-hidden)

- **Description:** no modal may gain `aria-hidden`/`display:none` while a
  descendant holds focus.
- **Rules:**
  - `taskboard.js` gains `blurActiveElement()` —
    `document.activeElement?.blur()` guarded for non-element targets.
  - Every `Modal.HideAsync()` call site in `AiChat.razor` (and the dialogs
    it hosts) blurs first — or uses a small `HideModalAsync(Modal)`
    wrapper in the page that blurs then hides.
  - Applies to: history popup, repo popup, model popup, sandbox popup, and
    the legacy `_runAgentModal`.
- **Input → Output:** closing any modal produces no `Blocked aria-hidden`
  console warning.

### RF-005: Layout — header, chat, command bar

- **Description:** replace the sidebar layout with a chat-first page.
- **Rules:**
  - Header row: `AI Code` title + a **history icon button**
    (`IconName.ClockHistory`, tooltip "Threads") + a **New icon button**
    (`IconName.PlusSquare`/`PencilSquare`, tooltip "New conversation") —
    New sits next to History.
  - Main area: the existing message stream, unchanged renderers
    (tool cards, permissions, queued badges, markdown, typing indicator).
  - Bottom command bar sits above the composer row and shows, left to
    right: `Mode`, `Agent CLI`, `Modo`, repo icon, workspace, model icon,
    sandbox icon (details in RF-006).
  - Composer (textarea + Send/Retry/Stop/Run-agent buttons) stays as the
    last row; queued/steer behaviour unchanged.
- **Input → Output:** page renders without `.ai-chat-sidebar`; empty state
  = unlocked command bar + composer.

### RF-006: Command bar controls

| Control | Unlocked state | Locked (active conversation) |
| --- | --- | --- |
| **Mode** | `<select>` `Assistant` \| `Agent` | read-only chip with thread mode |
| **Agent CLI** | `<select>` showing **agent name only**; non-`Available` agents disabled | read-only chip with the bound CLI name |
| **Modo** | ACP session modes select — visible only in `Agent` mode; populated from `sessionInfoJson.modes` when the session is live | stays **enabled only while an agent session is live** (it calls `session/set_mode` — a session control, not thread config); otherwise read-only chip |
| **Repo icon** | `IconName.Folder` button → popup with `RepositoryCombobox` + free text | read-only chip `owner/repo` (or `—`) |
| **Workspace** | read-only label, fixed `~/repos` (repo resolves to `~/repos/<name>` server-side; no editable path field) | same read-only label |
| **Model icon** | `IconName.Cpu` button → popup with `Model tier` (`Auto`/`Lite`/`Normal`/`Ultra`) + `Model` dropdown filtered by selected CLI (same data as today's dialog) | read-only chip with effective model; when a live agent session advertises `configOptions` `category:"model"` the chip opens the model popup in **session mode** (`set_config` on the live session) instead of being inert |
| **Sandbox icon** | `IconName.ShieldLock` button → popup with the three sandbox values; visible only in `Agent` mode | read-only chip |

- Assistant mode hides `Modo` and `Sandbox` (not applicable) and keeps Repo
  optional.
- A running agent thread still shows the context meter — it moves into the
  command bar's right edge (`ms-auto`).

### RF-007: Lazy create, auto-title, locking

- **Description:** there is no create form — the first `Send` creates the
  thread with the bar's configuration.
- **Rules:**
  - `Send` with `_activeThread is null` → `CreateAiChatThreadAsync` with
    `Title` auto-derived from the prompt (first 60 chars, trimmed at a word
    boundary, ellipsis when truncated) → then post the message (assistant:
    event + run; agent: queue prompt).
  - On successful create the bar **locks** and the thread appears in
    history.
  - **New** button: disconnects SSE, clears events/permissions, unlocks the
    bar (keeping the last-used selections as defaults), focuses the
    composer. The previous thread is preserved in history.
  - Selecting a thread from history locks the bar populated with that
    thread's `Mode`/`AgentType`/`RepositoryFullName`/`Model`/`Sandbox`.
  - Create failure → toast with server detail (RF-003); bar stays unlocked.
- **Input → Output:** type + Send → thread created, bar locked; New →
  blank composer, bar unlocked.

### RF-008: History popup

- **Description:** all threads behind one icon.
- **Rules:**
  - Modal lists every thread ordered by `UpdatedAt` desc: title (truncated),
    model badge, `running`/`failed` badges, relative updated time, delete
    affordance with inline confirm (same pattern as today).
  - Click a thread → `SelectThreadAsync` + close (with RF-004 blur).
  - Delete → `DELETE` (idempotent per RF-002); deleting the active thread
    returns to the empty unlocked state.
  - Reuses `GetAiChatThreadsAsync`; the popup refreshes its list on open.
- **Input → Output:** history icon → modal with full list; select → load.

### RF-009: Tests

- **Rules:**
  - Unit (BDD pt-BR): auto-title derivation; bar lock/unlock projection;
    `ReadErrorAsync` problem+json parsing.
  - Integration: `GET threads/{id}` 200/404; `DELETE` missing → 204; SSE
    endpoint on unknown thread → 404; SSE disconnect completes without
    throwing (handler-level test); heartbeat emits within the interval.
  - Existing `AiChatThreadEndpointsTests`/`AiChatAgentQueueEndpointsTests`
    updated for the DELETE semantics change.

## 5. API Contract

**Delta only** — routes unchanged otherwise:

```http
GET    /api/local/ai/threads/{id}          → 200 { thread } | 404 THREAD_NOT_FOUND   [new]
DELETE /api/local/ai/threads/{id}          → 204 (idempotent — was 404 on missing)   [changed]
GET    /api/local/ai/threads/{id}/events   → 404 THREAD_NOT_FOUND on unknown thread  [changed]
                                           SSE: + `: hb` comment every 15s,
                                           clean close on disconnect (was abort→502)
```

`POST /api/local/ai/threads` unchanged — `problem+json` `detail` is now
consumed by the client instead of dropped.

## 6. Acceptance Criteria

- [x] **Given** an agent-mode thread create for a repo with no local clone
  **when** Send creates the thread **then** the toast shows the server
  `detail` (workspace/clone message), not a generic failure.
- [x] **Given** a thread selected with live SSE **when** the browser closes
  the connection **then** the server completes the response normally — no
  mid-stream abort, no upstream 502 on reconnect.
- [x] **Given** an idle SSE stream behind a proxy **when** no events flow
  for >60s **then** heartbeat comments keep the connection alive.
- [x] **Given** `GET`/`events`/`DELETE` on an unknown thread id **then**
  404 / 404 / 204 respectively.
- [x] **Given** the new page **when** no thread is active **then** the
  command bar is unlocked and the first Send creates the thread with the
  chosen Mode/CLI/repo/model/sandbox and locks the bar.
- [x] **Given** an active conversation **when** the user clicks New **then**
  composer clears, bar unlocks, previous thread remains in history.
- [x] **Given** the history icon **when** clicked **then** a modal lists all
  threads newest-first with delete; selecting one loads it and locks the
  bar.
- [x] **Given** any modal close **then** no `aria-hidden` warning appears
  in the console.
- [x] **Given** an agent thread with a live session advertising modes
  **then** the `Modo` control remains usable mid-conversation
  (`session/set_mode`).

## 7. Task Plan

- [x] **T1 — Red tests:** endpoint tests (GET 404→200, DELETE idempotent,
  SSE unknown→404, disconnect clean close) + client `ReadErrorAsync`.
- [x] **T2 — Server:** SSE heartbeat + graceful disconnect + thread check;
  `GET threads/{id}`; DELETE idempotent.
- [x] **T3 — Client:** `ReadErrorAsync` + result records; `taskboard.js`
  `blurActiveElement`; modal-hide wrapper.
- [x] **T4 — UI:** AiChat.razor rework — header icons, history modal,
  command bar, lock state, lazy create + auto-title; CSS.
- [x] **T5 — Docs:** `docs/api*.md` (new GET, delete semantics, SSE
  heartbeat), `docs/features*.md` (AI Code section); ops note for nginx
  `proxy_buffering off` on SSE.
- [x] **T6 — Validation:** build (warnings-as-errors), tests, coverage ≥
  77% ratchet, update SPEC, open PR.

## 8. Guardrails

- No new SSE event types; backlog replay contract unchanged.
- No changes to agent-session lifecycle, permission gates, or queueing.
- `problem+json` shape unchanged — only consumed properly client-side.
- Threads deleted stay deleted — idempotent DELETE does not resurrect.

## 9. Definition of Done

- [x] All RFs implemented; acceptance criteria green.
- [x] Console clean on the repro path (create-fail, delete-stale,
  SSE disconnect, modal close).
- [x] `dotnet build` clean, `dotnet test` green, coverage ≥ ratchet.
- [x] Docs updated en + pt-br; SPEC status bumped on PR.

## Open Questions

- **"Modo" semantics:** treated as the ACP session-mode control (stays
  usable on live agent sessions even while the bar is locked). If it should
  hard-lock like the rest, it's a one-line change.
- **Model popup in locked state:** proposal above keeps it as a live
  session model switch when the peer advertises `configOptions`; hard-lock
  alternative is trivial.
- **Auto-title language:** titles derive from the prompt verbatim — no
  LLM-generated title (would need a run; out of scope).
