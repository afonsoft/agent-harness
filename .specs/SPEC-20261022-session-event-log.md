# SPEC-20261022-session-event-log

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `session-event-log` |
| Type | `Feature` |
| Stack | `.NET` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feat/session-event-log` |
| Status | `Implemented (P1 — JSONL event log; P2 projection seam pendente)` |
| Origin | ADE gap analysis 2026-10-22 — DeepSeek Harness durable `SessionEvent` log (generation-versioned JSONL + migration chain + projection seam) |

## 1. User Story

**As a** Harness maintainer
**I want** agent/session history modeled as an append-only, generation-versioned event log with a typed projection seam
**So that** session state survives restarts with a stable on-disk format, historical formats migrate forward instead of breaking, and consumers (feed, resume, telemetry, transcripts) read one consistent projected state instead of re-deriving ad hoc.

**Background guarantee:** durable events are the source of truth — after a server restart the same state is reprojected; no consumer depends on in-memory-only history.

## 2. Scope

**In scope:**
- Append-only `SessionEvent` record set (turn/start, user/assistant/tool events, step boundaries, terminal outcomes) persisted per run/session — file (JSONL under `~/.agent-harness/sessions/`) or SQLite table; decision in P1.
- Generation marker in the persisted header; readers translate supported historical generations through adjacent `vN → vN+1` migration steps; unknown-future versions are refused, never silently parsed.
- `ISessionProjectionRegistry` — plugins/services register incremental projections; host consumers read `StateOf<T>(sessionId)` snapshots (turn boundary state first).
- Migration of the existing `ChatMessage`/session-history read paths to the projected state where applicable.

**Out of scope:**
- Rewriting existing EF session stores wholesale — the event log augments; cut-over per consumer.
- Compression/encryption of the log (later).
- Live `agent/assistant-stream`-style transient frames — only durable settlements are logged.

## 3. Technical Context

- Session-history parity work (prior phases) — `AgentSessionIndex`, `ChatMessage`, `ChatRun` entities.
- `AgentHibernationService` — resume already reconstructs argv/cwd; event log would become its durable source.
- dsh `docs/architecture.md` session-log section as reference design (generation selection + adjacent migrations + `ctx.sessionProjections`).

## 4. Requirements

### RF-001: Durable append-only events
Every model-visible/session fact is recorded as a typed `SessionEvent` appended under the run's session directory; no in-place mutation; interrupted tails are detectable on open.

### RF-002: Versioned format + migrations
Persisted events carry a generation tag. `open` selects the newest supported generation; each migration step `vN→vN+1` lives in exactly one place and is verified (decode → migrate → re-encode → exclusive publish alongside source). Refuse unknown-future versions.

### RF-003: Projection seam
`ISessionProjectionRegistry`: contributors fold events incrementally into typed state; `StateOf<T>` returns the snapshot; `ISessionEventReader` supplies detached readers with the same projection definitions (no silent defaults).

### RF-004: Restart fidelity
After a server restart, projected turn-boundary state for an interrupted run equals the pre-restart state; hibernation/resume reads the log rather than bespoke tables.

## 5. API Contract

Internal contracts only (`ISessionEventLog`, `ISessionEventReader`, `ISessionProjectionRegistry`, `SessionEvent` union). No REST changes in P1.

## 6. Acceptance Criteria

- [ ] Events for a completed run replay to identical projected state after restart.
- [ ] A `v0` fixture migrates to current generation on open; `vFuture` is refused.
- [ ] A truncated tail is flagged (not silently truncated).
- [ ] At least one existing consumer (turn-boundary or feed) reads via `StateOf`.

## 7. Task Plan

- [x] **P1** — `SessionEvent` vocabulary + JSONL writer/reader + generation tag + refuse-future; unit tests incl. fixtures.
- [ ] **P2** — projection registry + turn-boundary projection; wire one consumer.
- [ ] **P3** — migration chain mechanism + hibernation/resume read-path cut-over.

## 8. Organization Guardrails

- Do not break existing session-history readers until their cut-over lands.
- Event payloads carry no secrets (reuse `SecretScrubber` where content is stored).
- Portuguese BDD test names; English code/docs.

## 9. Definition of Done

- [ ] P1–P2 merged with tests; P3 gated on prior stability.
- [ ] SPEC Status → `Done` when at least RF-001..RF-003 ship.

## Open Questions

- JSONL-per-session vs a single SQLite `SessionEvents` table — SQLite fits the existing store; JSONL fits dsh parity and external tooling. Decide in P1 with a small spike.
