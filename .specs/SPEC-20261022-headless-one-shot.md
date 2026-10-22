# SPEC-20261022-headless-one-shot

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `headless-one-shot` |
| Type | `Feature` |
| Stack | `.NET` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feat/headless-one-shot` |
| Status | `Draft` |
| Origin | ADE gap analysis 2026-10-22 — DeepSeek Harness (`dsh --profile headless`) + Orca |

## 1. User Story

**As a** Harness user/script
**I want** `taskctl run "<task>"` to execute one agent task to completion and print the final answer — with no browser, no interactive UI, and an exit code scripts can branch on
**So that** the server (already running in background) does the work while scripts, cron entries and CI pipelines consume Harness like `dsh --profile headless` or `orca run --on-shot`.

**Background guarantee:** all execution stays server-side. The CLI is a thin client — closing it (or the web) mid-run does not kill the task; `--follow <taskId>` re-attaches.

## 2. Scope

**In scope:**
- `taskctl run [task]` — submit one prompt to the server, stream the run, print the final answer, exit 0/1.
- Task text from the positional arg, or stdin when omitted / `-` (pipe-friendly).
- `--json` — newline-delimited run events on stdout (step boundaries, tool calls, terminal status).
- `--cli <name>` — pick the agent CLI; default = conversation default / `opencode`.
- `--worktree` / `--repo <path>` — run inside a fresh git worktree (existing `UseWorktree` path).
- `--follow <taskId>` — attach to an already-running/finished task and stream to its end (reattach after CLI/network drop).
- `--timeout <dur>` — CLI-side give-up (server task keeps running; message points at `--follow`).
- REST: `POST /api/harness/runs:oneshot` returning the run id immediately + `GET /api/harness/runs/{id}/events` SSE stream (reuse existing dispatch + feed machinery).

**Out of scope:**
- Embedding the agent runtime *inside* `taskctl` (dsh's no-server headless profile). Our agent execution is server-owned by design; an embeddable host is a separate, larger spec.
- Interactive follow-up turns — one task per invocation, as dsh's boundary.
- New agent-loop semantics — reuses `delegate_task` + dispatch.

## 3. Technical Context

- `src/Taskboard.Cli/` — `taskctl` commands (Spectre.Console.Cli), API client.
- `src/Taskboard.Server/Program.cs` — endpoint surface; `local/delegation/tasks` create path reused by `delegate_task`.
- `ChatRunDispatcherService` / `DelegationDispatcherService` — server-side run lifecycle (already background).
- SignalR `HarnessCockpitHub`/feed events — source for the SSE projection.
- `docs/CAPABILITY-MAP.md`, prior PRs #609–615 for the worktree/session plumbing to reuse.

## 4. Requirements

### RF-001: One-shot endpoint
`POST /api/harness/runs:oneshot { prompt, cli?, repositoryPath?, useWorktree? }` → `{ taskId, runId }` (202). Creates a delegated task through the existing `IDelegationService.CreateTaskAsync` + `ProvisionAndAttachAsync` path; dispatcher picks it up normally.

### RF-002: Run-events stream
`GET /api/harness/runs/{taskId}/events` — SSE of run lifecycle events (queued → dispatched → step/output chunks → terminal `ok|failed|canceled`) ending the stream on terminal state. Must not require the Blazor client.

### RF-003: `taskctl run`
`taskctl run "<task>" [--repo p] [--worktree] [--cli c] [--json] [--timeout d] [--follow id]`.
- Positional task or stdin; empty input → usage error before any HTTP call.
- Prints the final answer text on stdout; `--json` switches stdout to NDJSON events.
- Exit 0 on completed-with-answer, 1 on failed/canceled/timeout — timeout prints `taskctl run --follow <taskId>` hint.
- `--follow` attaches to an existing task id (reattach; server-side run untouched).

### RF-004: No new privilege surface
The endpoint uses the same auth/scope rules as other `/api/harness/*` routes; no capability bypass.

## 5. API Contract

```text
POST /api/harness/runs:oneshot
  body  { "prompt": "…", "cli": "opencode", "repositoryPath": "…", "useWorktree": true }
  202   { "taskId": "…", "runId": "…" }
  400   { "error": "prompt is required" }

GET  /api/harness/runs/{taskId}/events   (text/event-stream)
  data: {"type":"status","state":"dispatched"}
  data: {"type":"output","text":"…"}
  data: {"type":"terminal","state":"ok","answer":"…","exitCode":0}
```

## 6. Acceptance Criteria

- [ ] `taskctl run "say hi"` prints an answer and exits 0 while no browser is open.
- [ ] `{ echo x; } | taskctl run` consumes stdin; empty stdin fails fast.
- [ ] `--json` emits parseable NDJSON; `--follow` reattaches after `kill` of the CLI process and the task continues.
- [ ] A failed task exits 1 with the failure reason on stderr.
- [ ] Server-side task survives CLI close (verified by `--follow`).

## 7. Task Plan

- [ ] **P1** — `runs:oneshot` endpoint + SSE events stream; `taskctl run` basic (answer + exit code); unit+integration tests.
- [ ] **P2** — `--json`, `--follow`, `--timeout`, stdin; Spectre help/README snippet.
- [ ] **P3** — optional `dsh`-style in-process headless profile (embeddable host) — separate spec if pursued.

## 8. Organization Guardrails

- Server remains the single execution owner — no business logic in `taskctl`.
- SSE stream must not leak other users'/scopes' runs — scope check identical to existing endpoints.
- English code/commits; Portuguese BDD test names.

## 9. Definition of Done

- [ ] All RFs implemented with tests.
- [ ] `taskctl run` documented in README (en + pt-br).
- [ ] SPEC Status → `Done`; PR merged.

## Open Questions

- Whether the events stream should reuse the SignalR hub contract (client already knows it) or a leaner REST/SSE shape — decide in P1; SSE keeps `curl`/`taskctl` dependency-free.
