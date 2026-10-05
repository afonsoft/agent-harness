# SPEC-20261009 — Delegation coordinator, promote winner, issue→DAG, event feed

Status: `Done` — entregue via [PR #472](https://github.com/afonsoft/agent-harness/pull/472) (merged 2026-10-04)
Owner: devin
Parent: SPEC-20261005 (DAG/mailbox), SPEC-20261006 (dashboard/sessions/checkpoints), SPEC-20261007 (reply/compare/status)

## Context

orca's loop is: coordinator LLM decomposes a goal into a task DAG → workers run
in worktrees → human compares diffs and picks a winner → winner becomes a
branch/PR. P2–P4 gave us the DAG, mailbox, dispatcher, fan-out and the compare
endpoint+modal. This wave closes the loop:

- A dedicated **coordinator task kind**: a DAG task that runs a CLI to produce
  a plan, then materializes the plan as child tasks.
- **Promote winner**: push a fan-out leg's worktree branch to `origin`.
- **Issue → DAG**: one-click delegate a board issue into the delegation DAG
  (worktree-isolated) — distinct from the existing pipeline-run path.
- **Event feed**: a merged, time-ordered activity stream for the dashboard.

## Requirements

### RF-001 Coordinator task kind

- `DelegationTask.Kind` (`"task"` default | `"coordinate"`) — EF column,
  migration, exposed on `DelegationTaskDto`.
- `delegate_coordinate` chat tool (`RequiresConfirmation = true`):
  `{goal, cli?, max_tasks? (default 6, hard max 10), repository_path?}` →
  creates a Kind=coordinate task in the conversation scope. The tool validates
  `repository_path` is a git checkout (same helper as `delegate_task`) and
  stores it on the task so children can inherit worktree isolation.
- Dispatcher routes `Kind == "coordinate"` to a coordinator executor:
  - builtin CLI → orchestration queue (existing eligibility checks); on
    `Succeeded` the plan text is read from `GetLogsAsync(issueId)`.
  - custom def → inline exec via `CustomCliRunner`; plan text from stdout.
- The planning prompt asks for STRICT JSON: `{"tasks":[{"prompt","cli","deps":[],"use_worktree":false}]}`.
  `deps` are indices of earlier entries (same contract as `delegate_plan`).
  Extraction: last JSON object containing a `"tasks"` array in the output —
  tolerate code fences/prose around it.
- Materialization reuses the same validation as `delegate_plan` (count cap,
  deps point backwards, prompt non-empty). Children get `Kind="task"` and the
  coordinator's `Scope`; children with `use_worktree` inherit the coordinator's
  `RepositoryPath` and get a worktree created + attached immediately.
- On success the coordinator task finishes `done` with a summary and posts a
  mailbox `text` message to `@all` (`coordinator plan: N tasks`). On unparseable
  output or child-create failure → `failed` with the parse/create error.
- A coordinator task cannot create another coordinator task (the planner JSON
  has no kind field; children are always `task`).

### RF-002 Promote fan-out winner

- `POST /api/local/delegation/tasks/{id}/promote` → task must have a
  `WorktreeRunId` → `IWorkspaceIsolationService.CommitAsync(runId,
  "delegated task {id} — {cli}", "Harness <harness@local>")` then
  `PushAsync(runId)` → `{taskId, branch, commitSha}`. 404 unknown task, 409 no
  worktree. DomainException → 400.
- Compare modal (Agents page) gains a "Use this leg" button per leg with a
  worktree → `TaskboardClient.PromoteDelegationTaskAsync` → success toast with
  the pushed branch name.

### RF-003 Issue → DAG

- `POST /api/local/delegation/tasks/from-issue`
  `{repository_full_name, issue_number, title, body?, cli?}` →
  `IRepositoryProvisioningService.EnsureCloneAsync(fullName)` →
  `CreateTaskAsync(prompt = issue template, cli or "opencode", scope="harness",
  use_worktree=true, repository_path=clone)` → create + attach worktree
  (same flow as `delegate_task`) → `{taskId, worktreeRunId}`.
- TaskDetailDialog: "Delegate to worktree" button near the agent section →
  small pick of the conversation-independent CLI list (uses the existing
  agents endpoint the page already calls for badges — `OrchestrationService.
  GetAvailableAgentsAsync`) → POST from-issue → toast + the task appears in
  the delegation dashboard.

### RF-004 Event feed

- `GET /api/local/delegation/events?scope=&take=` → merged stream:
  `task_created` / `task_finished` (with status) from `IDelegationTaskRepository
  .ListByScopeAsync` + mailbox `text|worker_done|heartbeat|escalation|decision`
  from `IAgentMailboxRepository`. `DelegationEventDto(Timestamp, Kind, Text,
  TaskId?)`, sorted desc, `take` capped at 100.
- Agents dashboard: "Activity" feed section under the four columns.

## Non-goals

- No PTY/chat-over-live-CLI session (orca native-chat) — exec delegation stays.
- No GitHub PR creation on promote — push only; PR creation stays manual/via
  cockpit (existing `PushAsync` precedent).
- No remote/SSH agents, no coordinator→coordinator recursion.

## Testing

- Unit: `Kind` roundtrip on entity/DTO; plan-JSON extractor (fences, prose,
  nested objects, malformed → null); coordinator prompt builder; events merge
  ordering + take cap.
- Unit: dispatcher routes coordinate → coordinator executor (mocked CLI run
  returning plan JSON creates N tasks incl. dep wiring + worktree attach calls).
- Integration: `promote` (404/409/200 with fake isolation), `from-issue`
  (clone+task+worktree), `events` endpoint shape.
- Razor test: dashboard activity section renders feed rows.

## Files (expected)

- `src/Taskboard.Domain/Entities/Delegation/DelegationTask.cs` — `Kind`
- `src/Taskboard.Application.Contracts/Delegation/DelegationDtos.cs` — Kind on
  dto, `CreateDelegationTaskRequest.Kind`, `DelegationEventDto`, promote/from-issue requests
- `src/Taskboard.EntityFrameworkCore` — config + migration
- `src/Taskboard.Integrations/Delegation/DelegationDispatcherService.cs` — coordinate branch
- `src/Taskboard.Integrations/Delegation/CoordinatorPlanExtractor.cs` (new)
- `src/Taskboard.Integrations/Delegation/DelegationPlanCreator.cs` (new, shared with DelegatePlanTool)
- `src/Taskboard.Integrations/Chat/Tools/Delegation/DelegateCoordinatorTool.cs` (new)
- `src/Taskboard.Server/Program.cs` — promote + from-issue + events endpoints
- `src/Taskboard.Blazor/Components/Pages/Agents.razor` — promote button + activity feed
- `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` — delegate button
- `src/Taskboard.Blazor/Services/TaskboardClient.cs` — new calls
- tests in `tests/Taskboard.Tests.Unit` + `tests/Taskboard.Tests.Integration`
