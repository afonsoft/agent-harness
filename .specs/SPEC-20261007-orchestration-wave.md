# SPEC-20261007 — Orchestration wave: mailbox reply, coordinator plan, builtin model probes, fan-out compare, busy liveness

- Status: `Done` — entregue via [PR #469](https://github.com/afonsoft/agent-harness/pull/469) (merged 2026-10-04)
- Author: devin (requested by afonsoft, orca-parity follow-ups approved in chat)
- Base: `main` @ 291199b (P1–P3 + #460/#461/#467/#468 merged)

## Context

Orca-parity analysis (`.claude/memory/process-analysis-20261004.md` §10 +
orca-analysis §10) left five approved gaps. This SPEC implements them inside
the delegation/orchestration surface, improving `AgentOrchestrationService`
and the delegation layer around it. No `.github/workflows/**` changes.

## Requirements

### RF-001 — Human mailbox reply path

Today the mailbox is write-only for agents and read-only for humans: Needs You
items (escalation/decision) cannot be answered — the decision gate is a dead
end. Add:

- `IAgentMailboxRepository.GetAsync(id)` + EF impl.
- `IDelegationService.ReplyMailboxAsync(scope, messageId, fromAgent, body)`:
  looks up the original message in scope; posts a `text` message addressed to
  the original `FromAgent` with payload `body`; marks the original read.
  Returns the posted dto, or null when the message does not exist.
- `IDelegationService.DismissMailboxAsync(scope, ids)`: marks ids read
  (resolve-without-reply).
- Endpoints (RequireAuthorization, same as sibling GETs):
  - `POST /api/local/delegation/mailbox/{id}/reply` `{body, from?}` → 200
    `{reply}` / 404 `mailbox-message-not-found`.
  - `POST /api/local/delegation/mailbox/{id}/dismiss` → 204 / 404.
- Agents page: Needs You items with `Kind == "mailbox"` get a reply box
  (textarea + Reply + Dismiss). Reply sends and dismisses the item from the
  column (it was unread-gated).

### RF-002 — `delegate_plan` coordinator tool

Orca's coordinator decomposes a goal into a task DAG in one shot; our chat is
the coordinator but `delegate_task` creates one node per call. Add tool
`delegate_plan`:

```json
{"tasks":[{"prompt":"…","cli":"codex","deps":[0,2],"use_worktree":true}],
 "workspace_path":"…","repository_path":"…"}
```

- `deps` are **indices into this same array** (validated: in-range, not self).
- Entries validated up-front (prompt non-empty, cli non-empty/defaulted to
  `context.DefaultAgentCli`, dep indices valid); then created in array order
  with dep indices resolved to the created ids.
- A mid-plan creation failure cancels the already-created tasks of this plan
  (reason `plan-aborted`) and returns the error.
- Returns `{created:[{index,id,status}], scope}`.

### RF-003 — Model probe for builtin CLIs

Custom defs got `ModelListArgs` probing in P1 (RF-008); builtins still use the
static table. Add `IReadOnlyList<string>? ModelListArgs` to `AgentCliSpec`,
populated only where a documented list command exists: OpenCode `models`,
Grok `models`. Endpoint `GET /api/agents/builtin/{agentType}/models` resolves
type→CliKind→spec; `ModelListArgs == null` or probe failure → `{models: []}`;
otherwise reuses `CliProbeSnapshotService.GetDefModelsAsync` with cache key
`builtin:{kind}` (same 10s bound / ANSI strip / TTL).

### RF-004 — Fan-out compare endpoint + dashboard action

`delegate_compare` exists only as tool text. Add
`GET /api/local/delegation/fanout/{groupId}/compare?scope=` returning
`{legs:[{taskId, cliName, status, worktreeRunId, files:{changed,insertions,
deletions,paths[]}|null, patch(truncated 8k)|null}]}` — same
`IWorkspaceIsolationService.GetDiffAsync` composition the tool uses; legs
without a worktree get `files: null`.
`DashboardItemDto` gains `string? Group = null` (appended optional member,
populated with `FanoutGroupId` for task items). Agents page: dashboard items
with a group show a "compare" affordance opening a modal with the legs table
(cli, status, ±ins/del, files) — winner selection stays manual (documented).

### RF-005 — Busy-agent liveness

`GetAvailableAgentsAsync` marks Busy but says nothing about what the agent is
doing. `RunningJob` gains `StartedAtUtc`; `AgentInfo` gains optional
`ActiveIssueId`, `ActiveElapsedSeconds` — populated for busy agents so the
UI/API can tell a 2-minute run from a wedged one.

### RF-006 — `delegation-coordinator` skill

`.claude/skills/delegation-coordinator/SKILL.md` — coordinator playbook for
the chat assistant (decompose goal → `delegate_plan` → monitor via
`agent_inbox`/heartbeat → `delegate_compare` on fan-outs → `agent_decide` for
blocking choices). Mirroring orca's coordinator-as-prompt model; surfaced via
the existing skill discovery + slash palette.

## Non-goals

- OS-level liveness hooks (OSC/process watchers on live TUI sessions) —
  excluded with chat-over-PTY.
- Auto-merge of fan-out winners.
- Coordinator as an autonomous background agent — the chat is the
  coordinator; this SPEC gives it the primitives.

## Tests

- Unit: `ReplyMailboxAsync` (reply posts to original sender + marks read;
  missing id → null), `DismissMailboxAsync`, `delegate_plan` (dep-index
  resolution, validation failures, abort-on-failure), builtin probe endpoint
  behavior via service-level probe cache key, `AgentInfo` busy liveness fields.
- Integration: reply/dismiss/compare/builtin-models endpoints (401 unauth /
  200 shape).
- Existing suites must stay green.
