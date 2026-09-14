# SPEC-20260914-blazor-feature-parity

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `blazor-feature-parity` |
| Type | `Feature` |
| Stack | `.NET 10 / Blazor Server` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/{AgentLLM}-20260914-blazor-feature-parity` |
| Ticket | `GAP-implementation-blazor-feature-parity` |
| Status | `Done` |

## 1. User Story

**As a** Taskboard user migrating from the legacy React SPA
**I want** the Blazor UI to cover the board's task-detail features (comments, attachments, filters, gantt, workflow visual)
**So that** the .NET UI is a real replacement, not a subset.

**Problem context:**
SPEC-008 (frontend) describes the TO-BE UI as "board Kanban, filtros, gantt, editor de tarefas, chat AI, workflow visual, comentários e anexos". The current `Taskboard.Blazor` surface covers: kanban boards (local + GitHub), AI chat, settings, skills, prompts. **Not present**: task comments UI, attachments UI, gantt/timeline view, task filters, workflow visual editor. Confirmed by gap-analysis 2026-09-14 (`grep -i "comment|attachment|gantt" src/Taskboard.Blazor` → only counts inside form fields; no dedicated views). The legacy React SPA is deprecated (SPEC-012), so these features have no UI surface at all.

## 2. Scope

**In scope (phased, each phase a vertical slice):**
- **Phase A — Task detail:** comments thread + attachments (upload/download/delete) inside `TaskDetailDialog` for local tasks; consumes `/api/tasks/{id}/comments` and `/api/attachments`; adds `GET /api/tasks/{id}/attachments` (contract addition: attachment list was missing server-side).
- **Phase B — Filters:** filter bar on `BoardView` (label, priority, assignee, text search) over already-loaded tasks.
- **Phase C — Gantt/timeline:** read-only timeline view of tasks by start/due date.
- **Phase D — Workflow visual:** board for `WorkflowWorkspace` graphs (`/api/device-workspaces`, `/api/workflow-capabilities`).

**Out of scope:**
- Editing GitHub issue comments via GitHub API (separate integration concern).
- Real-time collaborative editing.

## 3. Technical Context

**Where the change happens:** `src/Taskboard.Blazor/Components/` — new pages/components consuming `TaskboardClient`; backend endpoints exist in `Program.cs`; `GET /api/tasks/{id}/attachments` was added by this spec (missing from SPEC-002 surface).

**Files to read before implementing:**
- `.specs/SPEC-008-frontend.md` · `.specs/SPEC-012-legacy-react.md`
- `src/Taskboard.Blazor/Services/TaskboardClient.cs`
- `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor`
- `src/Taskboard.Server/Program.cs` (comments + attachments endpoints)

## 4. Requirements

### RF-001: Comments
- **Description:** List/add comments on a local task inside `TaskDetailDialog`.
- **Input → Output:** `GET/POST /api/tasks/{id}/comments` → rendered thread with author + timestamp.

### RF-002: Attachments
- **Description:** Upload, list, download, delete attachments on a local task.
- **Input → Output:** multipart upload → list with download links; delete removes row.

### RF-003: Filters
- **Description:** Client-side filtering of board columns by label/priority/text.
- **Input → Output:** filter bar input → columns re-render with matching tasks only.

### RF-004: Gantt (read-only)
- **Description:** Timeline bars per task between `StartDate`/`DueDate`; navigable to a new `/gantt` route.
- **Input → Output:** `/gantt` → horizontal timeline grouped by project.

### RF-005: Workflow visual
- **Description:** Visualize `WorkflowWorkspace` JSON as a node/edge graph on `/workflow` route (read-only v1).
- **Input → Output:** `/workflow` → rendered graph or explicit "no workflow configured" empty state.

## 6. Acceptance Criteria

- [x] **Given** a task with comments **when** the detail dialog opens **then** comments render chronologically and new comments POST successfully.
- [x] **Given** a task **when** a file is attached **then** it appears in the list and downloads correctly.
- [x] **Given** a board with mixed tasks **when** a filter is applied **then** non-matching cards are hidden without a server round-trip.
- [x] **Given** tasks with dates **when** `/gantt` is opened **then** a timeline renders.
- [x] **Given** a workspace **when** `/workflow` is opened **then** the graph or a designed empty state renders.
- [x] `dotnet build` + `dotnet test` green; each phase is independently shippable.

## 7. Task Plan

- [x] **T1 — Phase A spec-out:** detail comments/attachments component design; confirm DTOs.
- [x] **T2 — Phase A implement + tests.**
- [x] **T3 — Phase B implement + tests.**
- [x] **T4 — Phase C implement + tests.**
- [x] **T5 — Phase D implement + tests.**
- [x] **T6 — Done + PR per phase.**

## 9. Definition of Done

- [x] Each phase behind its own route/component with tests.
- [x] SPEC-008 parity table updated in docs.
