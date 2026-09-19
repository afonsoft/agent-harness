# SPEC-20260918 — AI Chat: refletir estado `Failed` da thread ao vivo via SSE

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `aichat-thread-failed-state` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WebAssembly / SSE` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260918-aichat-thread-failed-state` |
| Ticket | `GAP-implementation-aichat-failed-thread-state` (gap-analysis-20260918) |
| Status | `Done` — entregue e mergeada (2026-09-18) |

Origin: gap-analysis-20260918 — `SPEC-20260918-ai-chat-threads` AC5
("erro de provider/SSE mostra estado de erro na thread sem travar a página")
não se cumpre ao vivo: quando um run falha, o servidor marca a thread como
`Failed` e publica `ai_chat.run`, mas a UI só atualiza `_running` — o badge
de falha nunca aparece na sessão.

## 1. User Story

**As a** Harness user watching an AI Chat thread,
**I want** the thread's failed state to surface immediately when a provider
run errors,
**so that** I can tell the run failed without re-selecting the thread or
reloading the page.

## 2. Scope

### In scope

- In `AiChat.razor`'s `OnSseEvent` `ai_chat.run` handler, propagate the
  terminal run status to the thread state: on `failed` (or equivalent
  terminal error status), set `_activeThread.Status` to `failed` and update
  the matching entry in the sidebar thread list so both badges reflect it.
- Keep composer re-enable behavior (`_running = false`) unchanged — retry
  must stay possible.

### Out of scope

- Backend changes (server already sets `Failed` and publishes the event).
- Retry/resume UX beyond what exists.
- `.github/workflows/**`.

## 3. Technical Context

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — `OnSseEvent` handler
  (~line 267-286): `ai_chat.run` currently only sets
  `_running = run.Status == "running"`. The header renders a "Failed" badge
  gated on `_activeThread.Status == "failed"`; the sidebar lists threads with
  `thread.Status` badges.
- `src/Taskboard.Application/AiChat/AiChatService.cs` — catch block marks
  `thread.SetStatus(AiChatThreadStatus.Failed)` (~line 199) and publishes the
  terminal `ai_chat.run` event consumed by the same SSE channel.
- `src/Taskboard.Domain/Entities/AiChatThread.cs` — status enum values used
  by the DTO (`idle`/`running`/`failed` string forms in the payload).

## 4. Functional Requirements

| ID | Requirement |
|---|---|
| RF-001 | When an `ai_chat.run` SSE event carries a terminal failed status, `_activeThread.Status` and the corresponding sidebar list entry reflect `failed` without reload or re-selection. |
| RF-002 | Composer re-enables on any terminal status (`completed`/`failed`) exactly as today. |
| RF-003 | The header "Failed" badge (existing markup gated on `_activeThread.Status == "failed"`) becomes visible on failure; the sidebar badge updates consistently. |
| RF-004 | A subsequent successful run clears the failed state back to the thread's normal status. |

## 5. Acceptance Criteria

- **AC-1** *Given* an open thread with a running run, *when* the provider
  fails and the terminal `ai_chat.run` event arrives, *then* the thread shows
  the failed state (header + sidebar badge) in the same session.
- **AC-2** *Given* the failed state, *when* the user sends a new message and
  a new run completes, *then* the thread returns to its normal status.
- **AC-3** *Given* any failure, *when* it occurs, *then* the page remains
  interactive and the composer is re-enabled.

## 6. DoD

- [x] RF-001..004 implemented.
- [x] Unit/component-level coverage where practical (or documented manual check).
- [x] `dotnet build` + suites green.
- [x] SPEC → `Status: Done`; PR merged; `taskboard-server` redeployed.
