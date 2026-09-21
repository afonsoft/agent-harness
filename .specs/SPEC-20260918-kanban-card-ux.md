# SPEC-20260918 — Kanban Card UX: drag animations, markdown, agent config tab, move/close e prioridade

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `kanban-card-ux` |
| Type | `Feature` (Frontend + API) |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / Octokit / Blazor WASM / Markdig + HtmlSanitizer` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260918-kanban-card-ux` |
| Ticket | N/A |
| Status | `Done` |

## 1. User Story

**As a** Taskboard administrator managing GitHub issues on the kanban board,
**I want** richer, animated cards with markdown bodies, per-issue agent
configuration, in-popup move/close actions and editable priority,
**so that** the whole task lifecycle is operable from the board — on desktop
and on mobile — without opening GitHub.

Problem context: today the card shows little context, issue bodies render as
raw text, moving a task is drag-or-⋮-menu only (the ⋮ menu exists but the
detail popup offers no move/close actions), priority can only be set by
editing labels on GitHub, and running an agent from a card offers no
per-issue prompt.

## 2. Scope

### In scope

- Drag-and-drop animations on the kanban (dragstart lift, column highlight,
  drop placeholder/settle), CSS-only, respecting `prefers-reduced-motion`.
- Richer card layout: `#number` + title, up to 3 non-column/non-priority
  label chips, assignee initials, 2-line body excerpt, priority swatch,
  existing agent badge, relative "updated" time.
- Priority editing from the card `⋮` menu (None/Urgent/High/Medium/Low) via a
  new `PUT .../issues/{n}/priority` endpoint that swaps `priority:*` labels.
- `NewTaskDialog` gains **Escrever/Visualizar** tabs with a sanitized
  markdown preview.
- `TaskDetailDialog` → `Detalhes` renders `Issue.Body` as sanitized markdown
  and gains an **Editar** action (title + body, markdown editor with
  preview) via a new `PATCH .../issues/{n}` endpoint.
- `TaskDetailDialog` gains a **Config Agent** tab: eligible-agents dropdown,
  per-issue prompt textarea, "Executar" button → `POST /api/agents/executions`.
- `TaskDetailDialog` → `Detalhes` bottom: **Mover para…** button opening a
  popup with the open columns plus **Cancelar** (label `canceled` + close →
  Canceled column) and **Arquivar** (close, no column label → Archived) via
  a new `POST .../issues/{n}/close` endpoint.
- New NuGet packages: `Markdig` (render) + `HtmlSanitizer` (sanitize) —
  pinned versions ≥ 7 days old; justified in the PR.
- Tests (unit + integration) for the three new endpoints, the label-swap
  logic, markdown sanitization, and the agent-tab eligibility filtering.

### Out of scope

- The local-tasks board (`BoardView`/`TaskCard`) — this SPEC covers the
  GitHub issues kanban only.
- Issue reopen, comments, assignee editing, label management UI.
- Real-time card refresh via SignalR.
- Persisting the agent prompt server-side (it is per-run; last-used prompt
  is cached client-side per issue in `localStorage`).

## 3. Technical Context

Relevant existing code:

- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` — card markup,
  drag handlers (`OnDragStart`/`OnDragOver`/`OnDrop`), `⋮` "Mover para…"
  menu, agent badge, `GetPriorityClass`/`GetPrioritySwatchStyle`.
- `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` — `Tabs`:
  `Detalhes` + `Logs do Agente`; loads latest `AgentRun`.
- `src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor` — title,
  body `<textarea>`, initial column.
- `src/Taskboard.Application.Contracts/GitHub/GitHubBoardColumnExtensions.cs`
  — `LabelMap`, `ToLabel`, `FromLabel`, `ResolvePriority` (`priority:*`
  labels → `Urgent|High|Medium|Low|None`), `LabelBackedColumns`.
- `src/Taskboard.Application.Contracts/GitHub/GitHubBoardGrouper.cs` —
  `ResolveColumn`: closed + `canceled` → Canceled; closed otherwise →
  Archived; `IsVisible` keeps closed issues for 90 days.
- `src/Taskboard.Integrations/GitHub/GitHubService.cs` — Octokit wrappers:
  `CreateIssueAsync`, `UpdateIssueColumnAsync`, `AddLabelsToIssueAsync`,
  `EnsureLabelExistsAsync`. No close/edit-body/priority-swap today.
- `src/Taskboard.Server/Program.cs` — `github` group: `POST issues`,
  `PUT issues/{n}/column`, `POST issues/{n}/labels` (all
  `RequireAuthorization`); `agents` group with `POST executions`.
- `AgentExecutionRequest` already carries an `instructions` field
  (per-issue prompt) — the Config Agent tab reuses it.
- `IAgentOrchestrationService.GetAvailableAgentsAsync` — already returns
  only installed + authenticated + enabled agents.
- `src/Taskboard.Client/wwwroot/css/site.css` — `task-card`, `agent-badge`,
  priority swatches, `prefers-reduced-motion` block.

New files (expected):

- `src/Taskboard.Blazor/Components/GitHub/MoveIssueModal.razor` — shared
  move popup (open columns + Cancelar/Arquivar); reused by the card ⋮ menu
  and the Detalhes-tab button.
- `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` — dropdown +
  prompt + Executar.
- `src/Taskboard.Blazor/Services/MarkdownRenderer.cs` (or a small static
  helper in Blazor) — Markdig pipeline + HtmlSanitizer.
- Contract DTOs: `UpdateGitHubIssueRequest`, `SetIssuePriorityRequest`,
  `CloseGitHubIssueRequest`.
- Server: `PATCH issues/{n}`, `PUT issues/{n}/priority`,
  `POST issues/{n}/close` in the existing `github` group.
- `IGitHubService`: `UpdateIssueAsync(title, body)`,
  `SetIssuePriorityAsync`, `CloseIssueAsync(resolution)`.
- Tests: unit (label swap logic, priority parsing edge cases, sanitizer)
  and integration (401/404/422-ish paths + happy paths with a stubbed
  `IGitHubService`).

## 4. Requirements

| # | Requirement |
|---|---|
| RF-001 | Dragging a card lifts/scales it (`dragstart`), the target column highlights on `dragover`, a placeholder shows the landing slot, and the card settles on drop. All animation is CSS and is disabled under `prefers-reduced-motion`. |
| RF-002 | The card shows `#number`, title, up to 3 label chips (excluding column and `priority:*` labels), assignee initials when present, a 2-line body excerpt when present, priority swatch + name, agent badge and relative `UpdatedAt`. |
| RF-003 | Card `⋮` menu gains a **Prioridade** submenu (None/Urgent/High/Medium/Low). Selecting one calls `PUT /api/github/repos/{owner}/{repo}/issues/{n}/priority`; the server removes all existing `priority:*` labels and adds the new one (`None` = remove only). The card updates without a full board reload when the endpoint returns the updated issue. |
| RF-004 | `NewTaskDialog` has **Escrever** and **Visualizar** tabs; Visualizar renders the body as sanitized GitHub-flavored markdown. |
| RF-005 | `TaskDetailDialog` → `Detalhes` renders `Issue.Body` as sanitized markdown (headings, lists, code blocks, links, task lists). An **Editar** button switches to the markdown editor (title + body + preview); Salvar calls `PATCH /api/github/.../issues/{n}` and the view re-renders. |
| RF-006 | `TaskDetailDialog` gains a **Config Agent** tab: dropdown of `GET /api/agents` (eligible only), a prompt `<textarea>` (prefilled from `localStorage["agent-prompt:{issueId}"]`, saved on Executar), and **Executar** → `POST /api/agents/executions` with `instructions = prompt`. `202` → success toast + badge refresh; `422` → danger toast "agente inelegível". Empty agent list → empty state linking to `/settings`. |
| RF-007 | `Detalhes` bottom has **Mover para…** opening `MoveIssueModal`: radio list of open columns (current disabled), plus a divider and **Cancelar** / **Arquivar** close actions. `POST .../issues/{n}/close` `{ "resolution": "canceled" }` adds the `canceled` label and closes the issue; `{ "resolution": "archived" }` closes without a column label. The board reloads after any move/close. |
| RF-008 | New endpoints live in the existing authorized `github` group: `PATCH repos/{o}/{r}/issues/{n}` `{ title?, body }`, `PUT repos/{o}/{r}/issues/{n}/priority` `{ priority }`, `POST repos/{o}/{r}/issues/{n}/close` `{ resolution }`. Invalid enum values → `400`; unknown issue → `404` (Octokit `NotFound` mapped). |
| RF-009 | Rendered markdown is sanitized (`HtmlSanitizer`) — no raw HTML, no `javascript:` links, no injected scripts. Preview and detail share the same pipeline. |
| RF-010 | `MoveIssueModal` and the priority submenu meet the 44px touch-target rule; the move flow remains fully usable on touch (this SPEC removes the last drag-only path). |
| RF-011 | Board display/behavior for the local-tasks board is unchanged. |

## 5. API Contract

```http
PATCH /api/github/repos/{owner}/{repo}/issues/{number}
{ "title": "string|null", "body": "string" }        → 200 { issue } | 404

PUT   /api/github/repos/{owner}/{repo}/issues/{number}/priority
{ "priority": "none|urgent|high|medium|low" }       → 200 { issue } | 400 | 404

POST  /api/github/repos/{owner}/{repo}/issues/{number}/close
{ "resolution": "canceled|archived" }               → 200 { issue } | 400 | 404
```

`SetIssuePriorityAsync` removes every existing `priority:*` label (404 on a
missing label is ignored, matching `UpdateIssueColumnAsync`) and adds
`priority:{value}` when not `none`. `CloseIssueAsync("canceled")` applies
the `canceled` label before `IssueUpdate { State = ItemState.Closed }`;
`"archived"` closes without a column label.

`POST /api/agents/executions` is reused unchanged (eligible agents only,
`202`/`422`).

## 6. Acceptance Criteria

- **AC-1** *Given* a card being dragged over a column, *when* `dragover`
  fires, *then* the column shows the highlight + placeholder; on drop the
  card animates into place.
- **AC-2** *Given* `prefers-reduced-motion: reduce`, *when* dragging,
  *then* no transform/animation is applied.
- **AC-3** *Given* a card `⋮` → Prioridade → High, *when* the request
  succeeds, *then* the issue has `priority:high` and no other `priority:*`
  label, and the card swatch updates.
- **AC-4** *Given* Prioridade → None on an issue with `priority:low`,
  *when* it succeeds, *then* no `priority:*` label remains.
- **AC-5** *Given* a body containing `<script>alert(1)</script>` and
  `[x](javascript:alert(1))`, *when* rendered, *then* neither executes —
  script tags are stripped and unsafe links neutralized.
- **AC-6** *Given* the New task dialog, *when* I write `## Title` in
  Escrever and switch to Visualizar, *then* a heading renders.
- **AC-7** *Given* the Config Agent tab with `Codex` selected and prompt
  "fix the tests", *when* Executar is pressed, *then* `POST
  /api/agents/executions` carries `instructions="fix the tests"` and a
  success toast shows on `202`.
- **AC-8** *Given* no eligible agents, *when* the Config Agent tab opens,
  *then* the empty state links to `/settings`.
- **AC-9** *Given* Detalhes → Mover para → **Cancelar**, *when* it
  succeeds, *then* the issue is closed on GitHub, carries `canceled`, and
  appears in the Canceled column after reload.
- **AC-10** *Given* Detalhes → Mover para → **Arquivar**, *when* it
  succeeds, *then* the issue is closed without a column label and appears
  in Archived.
- **AC-11** *Given* anonymous requests to the three new endpoints, *when*
  called, *then* `401`.
- **AC-12** *Given* the edit action, *when* Salvar is pressed with an empty
  title, *then* `400`/client-side guard — title stays non-empty.
- **AC-13** *Given* a 375px viewport, *when* using the ⋮ menu, the priority
  submenu and the move popup, *then* every tappable target is ≥44px.

## 7. Task Plan

- **T1 — Contracts + GitHubService**: DTOs, `UpdateIssueAsync`,
  `SetIssuePriorityAsync` (label swap), `CloseIssueAsync`; unit tests for
  label-swap/priority parsing.
- **T2 — Server endpoints**: `PATCH issue`, `PUT priority`, `POST close` +
  integration tests (401/400/404/200 via stubbed `IGitHubService`).
- **T3 — Markdown pipeline**: Markdig + HtmlSanitizer helper + unit tests
  (AC-5 fixtures); vendored nothing — render in C#.
- **T4 — Client services**: `HttpGitHubService` methods for the three
  endpoints.
- **T5 — Card markup + animations**: richer card (RF-002), priority
  submenu (RF-003), drag/drop CSS (RF-001).
- **T6 — Dialogs**: `NewTaskDialog` Escrever/Visualizar; `TaskDetailDialog`
  markdown body + Editar + Mover para… + `MoveIssueModal`;
  `AgentConfigTab`.
- **T7 — CSS/mobile**: markdown styles, chips, excerpt clamp, touch
  targets, reduced-motion.
- **T8 — Validation**: `dotnet build -c Release`, full test suite.
- **T9 — Docs + delivery**: `docs/api*.md`, spec → Done, commit/push/PR,
  merge, host re-deploy (`dotnet publish` + `systemctl --user restart
  taskboard-server`).

## 8. Organization Guardrails

- Branch `feature/devin-20260918-kanban-card-ux` off `main`; PR → squash
  merge; never commit to `main` directly.
- No changes under `.github/workflows/`.
- New packages `Markdig` + `HtmlSanitizer` must be justified in the PR
  description and pinned via `Directory.Packages.props`.
- Rendered markdown **must** be sanitized — issue bodies are untrusted
  content (XSS surface). No raw `MarkupString` of unsanitized HTML.
- Issue IDs/labels never log secrets; GitHub token stays server-side.

## 9. Definition of Done

- [ ] All RF-001..RF-011 implemented.
- [ ] AC-1..AC-13 covered by unit/integration tests or manual verification
      notes.
- [ ] `dotnet build -c Release` clean; full suite green.
- [ ] `docs/api.md` + `docs/api.pt-br.md` document the new endpoints.
- [ ] Spec status → `Done`; PR merged; host service redeployed and
      smoke-checked (board loads, move/close/priority round-trip).
