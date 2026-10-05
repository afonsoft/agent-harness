# SPEC-20261005-chat-tool-approval: Interactive tool approval & permission presets

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Chat tool approval seam + permission presets |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Domain + Application(.Contracts) + EntityFrameworkCore + Server + Blazor WASM |
| Change type | Feature / Architecture |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261006-chat-tool-approval` |
| Status | `Approved` — aprovado por afonsoft (2026-10-05) |
| Depends on | SPEC-20261005-chat-background-resume (detached `ChatRun`s, `ChatRunBroadcaster`, dispatcher) |
| Reference | COMPARISON-20261005-deepseek-harness §1/#1, §3.3 (fail-closed approval, presets) |

## 1. Executive Summary

### Problem

`IChatTool.RequiresConfirmation` exists but is only **rendered as a hint** in
Settings (`Settings.razor` shows a badge). Nothing gates execution: once a
capability is enabled, the model runs `shell_exec`, `write_file`,
`edit_file`, `run_tests`, `run_cli`, `code_interpreter`, `run_agent`,
`memory`, `todo` and every board/delegation mutator without human consent.
Detached runs (SPEC-20261005 P1) make this worse — the agent mutates the
workspace while no tab is open.

deepseek-harness solves this with an **approval seam**: `tools/pre-execute`
may emit `ask` → `ctx.approval` dispatches a one-shot prompt to an answerer
(UI card or ACP policy); the outcome is closed
(`allowed-once | rejected | cancelled | unavailable`) and **fail-closed** — an
unanswerable or missing answerer denies. `approval/asked` +
`approval/decided` audit events pair by a fresh `ApprovalRequestId`. A
per-session policy (`ask`/`never`) sits above interactive answerers, and
deployments ship **permission presets** that bundle tool-policy choices.

### Solution

Turn `RequiresConfirmation` into a real gate inside the run loop and give the
user per-conversation permission presets:

1. **`ChatApproval`** — a pending question bound to a tool call: id, run id,
   tool name, argument preview, policy source, state
   (`pending | allowed-once | rejected | cancelled | unavailable`), asked-at,
   decided-at. Persisted so a browser-closed run can surface the question on
   re-attach, and answered from any open tab.
2. **Loop gate** — in `ChatService` tool dispatch, when the resolved policy
   for `(conversation, tool)` is `ask` and `tool.RequiresConfirmation`, the
   loop suspends that tool call, persists `ChatApproval`, broadcasts an
   `approval.asked` SSE event (plus `ChatRunHub` push) and waits on a
   timeout-bounded `TaskCompletionSource`. Outcomes:
   - `allowed-once` → execute this call only; next ask re-prompts.
   - `rejected` → the tool result becomes a synthetic refusal
     (`"Tool call denied by the user: <reason?>"`) and the loop continues.
   - `cancelled` (stop/run cancellation) → same as rejected, marked
     `cancelled`.
   - `unavailable` (timeout with no answerer, or policy error) →
     **fail-closed**: refused, marked `unavailable`.
   Answers set `DecidedBy` (`session:<n>` of the browser session is out of
   scope — store `ui` vs `auto-timeout`) and are broadcast to all attached
   streams so the pending card resolves everywhere.
3. **Permission presets** — a new per-conversation `PermissionPreset`:
   `chat` (no mutating tools), `ask` (default; confirm mutating calls),
   `full` (never ask — current behavior), resolved
   `preset → policy(tool)`: `ChatCapabilityRegistry` disable wins over preset.
   Preset changes mid-run take effect on the next tool call; stored on
   `ChatConversation.PermissionPreset` + runtime override
   `Taskboard:Chat:Approval:Preset` as the global default for new
   conversations.
4. **UI** — an inline approval card in `ProviderChat` (tool name, args
   preview expandable, Allow once / Deny [+ optional reason] / Always allow
   for this tool in this conversation), badge on the conversation in the
   sidebar while a run is `waiting-approval`, preset picker in the chat
   header (chip: `Chat | Ask | Full`), Settings default + per-tool policy
   column (ask/never) next to the existing capability toggles.

### Scope

In scope: `ChatApproval` aggregate + migration, run-loop gate, SSE/hub
events, REST answer endpoint, presets (global default + per-conversation),
`ProviderChat` card + header chip + Settings, per-tool `ask`/`never` policy
keys, audit fields, tests.

Out of scope: multi-user role-based approval, rule/glob policies
("allow `git status` but not `rm`") — flagged as Phase 2 note —, approvals
for MCP-served tools (they inherit the generic gate by tool name), non-chat
executors (delegation tasks keep their own gating).

## 2. Requisitos

### Funcionais

- RF-001 `ChatApproval` persists every ask: `Id`, `ChatRunId`,
  `ConversationId`, `ToolName`, `ArgumentsPreview` (≤ 2000 chars, args JSON
  truncated), `Status`, `RequestedAt`, `DecidedAt`, `Decision` (payload:
  outcome + optional reason), `DecidedBy` (`ui` | `auto-timeout` |
  `auto-cancel`).
- RF-002 Run loop: before executing a tool call where
  `IChatTool.RequiresConfirmation` (or listed in the preset's ask-set) and
  resolved policy is `ask`, create `ChatApproval`, emit
  `approval.asked { approvalId, toolName, argsPreview }` on the run SSE and
  `run.approval` on `ChatRunHub`, suspend the call.
- RF-003 Answer endpoint `POST /api/local/chat/approvals/{id}/decide`
  `{ outcome, reason?, rememberTool? }`: atomic — only `pending` → decided
  (409 on race); broadcasts `approval.decided`; resumes the suspended call.
- RF-004 `rememberTool=true` writes per-conversation tool policy
  `allowed-list` so identical later calls in that conversation skip the ask
  (preset `ask` + per-tool allow override).
- RF-005 `auto-timeout` after `Taskboard:Chat:Approval:TimeoutSeconds` (default
  120, 0 = wait forever); timeout outcome = `unavailable` → synthetic deny
  result. `auto-cancel` on run stop.
- RF-006 Presets: `chat` disables all `MutatingTools`+`RequiresConfirmation`
  tools for the conversation (read-only — model sees the tools but calls are
  refused pre-execution), `ask` prompts per call, `full` never prompts.
  Preset is per-conversation, editable mid-run.
- RF-007 Sidebar run badge shows a distinct pending-approval state; opening
  the conversation shows the unresolved card (replayable from the approval
  row, not just live SSE).
- RF-008 Settings: `Taskboard:Chat:Approval:Preset` default + `Chat:Approval:
  TimeoutSeconds` + per-tool `ask`/`never` override list in the Capabilities
  section.
- RF-009 Global disable key `Taskboard:Chat:Approval:Enabled` (default true)
  — when false, `full` behavior for every conversation.

### Não-funcionais

- RNF-001 The suspended tool call must not hold a DB transaction or block the
  dispatcher thread (async wait; the dispatcher processes other
  conversations).
- RNF-002 Timeout default keeps unattended runs from hanging forever;
  `unavailable` is fail-closed (deny), never allow.
- RNF-003 Approval decisions are observable: stored with decided-at/by and
  shown in the run transcript as a system note row.
- RNF-004 Zero regression when `Enabled=false` or preset=`full`: identical
  behavior to today.

## 3. Arquitetura

```mermaid
flowchart LR
    LLM -->|tool_call| Gate[ApprovalGate]
    Gate -->|policy=full / allowed-list| Exec[IChatTool.ExecuteAsync]
    Gate -->|policy=ask| Ask[ChatApproval pending]
    Ask -->|approval.asked| SSE[ChatRunBroadcaster + ChatRunHub]
    Ask --> TCS[TaskCompletionSource + timeout]
    UI[POST approvals/{id}/decide] --> TCS
    TCS -->|allowed-once| Exec
    TCS -->|rejected/cancelled/unavailable| Deny[Synthetic tool result: denied]
```

- **Domain**: `ChatApproval` entity (new table `ChatApprovals`, FK
  `ChatRunId`, index `(ConversationId, Status)`). Value object
  `ChatApprovalStatus`. `ChatConversation.PermissionPreset` column
  (`nvarchar(16)`, default `"ask"` via migration backfill).
- **Application**: `ChatApprovalGate` service — resolves
  `Conversation.Preset → tool → policy`; `AskAsync(call)` → row + broadcast +
  `TaskCompletionSource` registered in `ChatRunCoordinator`-adjacent
  `ConcurrentDictionary<ApprovalId, TCS>`; `DecideAsync(id, outcome)`
  completes it. Registered on the dispatcher scope.
- **Server**: `POST /api/local/chat/approvals/{id}/decide` + list
  `GET /api/local/chat/conversations/{id}/approvals?status=pending` (for
  re-attach replay); hub event `run.approval`.
- **Client**: `TaskboardClient` + `ProviderChat` pending-card state merges
  replayed pending approvals with live `approval.asked` events; header
  preset chip.
- Preset resolution order: capability disabled > per-tool `never` >
  per-conversation allowed-list > preset ask-set > preset.

## 4. Fases

- **P1** — `ChatApproval` + gate + decide endpoint + SSE/hub events +
  ProviderChat card + timeout + audit note. Presets `ask`/`full` only.
- **P2** — `chat` preset + header chip + Settings defaults + per-tool
  `never`/`allowed-list` overrides.

## 5. Testes

- Unit: gate resolution matrix (preset × tool flag × override), timeout →
  `unavailable`, race-safe decide (double decide → 409), synthetic denial
  result content.
- Integration: run a mutating tool under `ask` → stream shows
  `approval.asked` → POST decide `allowed-once` → run completes; `rejected`
  → tool result says denied and run continues; stop during pending →
  `cancelled`.
- Blazor guard: pending card renders from replayed pending approvals without
  live SSE; preset chip writes `PermissionPreset`.

## 6. Open questions

1. Rule/glob policies (`allow "dotnet test*"`) — defer to a follow-up spec
   or fold the `allowed-list` into it?
2. Should `allowed-list` entries expire (per-run vs per-conversation)?
   Proposed: per-conversation, cleared by preset change.
3. Notify via `run.completed`-style push when a run parks on
   `waiting-approval`? Proposed: reuse `IChatRunNotifier` with a new
  `approval.asked` event — P2.
