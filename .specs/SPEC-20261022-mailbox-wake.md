# SPEC-20261022-mailbox-wake

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `mailbox-wake` |
| Type | `Bugfix` |
| Stack | `.NET` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feat/mailbox-wake` |
| Status | `Draft` |
| Origin | ADE gap analysis 2026-10-22 — DeepSeek Harness Agent Teams (inactive teammate cold-resumes on mailbox delivery) |

## 1. User Story

**As a** Harness orchestrator
**I want** a mailbox message addressed to a hibernated agent session to auto-resume it (when the user's prefs allow)
**So that** inter-agent mail never lands in a dead inbox — today a hibernated session can only wake on UI focus, so messages sent while no browser is open sit unread forever.

**Background guarantee:** the wake happens server-side at delivery time — consistent with "everything keeps running with the web closed".

## 2. Scope

**In scope:**
- On `mailbox_send`/delivery, resolve whether the recipient maps to a hibernated `AgentSessionIndex` row; if so, call `IAgentHibernationService.ResumeAsync(..., auto: true)` honoring the user's `AutoResumeOnFocus`-style preference (new pref `AutoResumeOnMailbox`, default on).
- Broadcast/`@all` delivery: only sessions addressed by an explicit recipient token wake — broadcast alone never mass-resumes.
- Rate limiting: at most one auto-resume per session per N seconds (default 60s) to avoid flapping.
- Feed event `AgentFeedKind.SessionResumed` already exists — reuse; reason field marks `mailbox`.

**Out of scope:**
- Queueing mailbox delivery semantics (deliveries/acks already handled).
- Waking on task-bound dispatches — covered by the dispatcher's own launch path.
- UI changes beyond the user-pref toggle.

## 3. Technical Context

- `src/Taskboard.Application/Agents/AgentHibernationService.cs` — `ResumeAsync(userId, sessionKey, auto, ct)` + `AutoResumeOnFocus` pref pattern.
- `src/Taskboard.Application/Delegation/DelegationService.cs` — `CheckDeliveriesAsync`, `_mailbox.AssignDeliveryAsync`, recipient computation (`DelegationToolSupport.RecipientsAsync`).
- `AgentSessionIndexService` — sessionKey ↔ user/cli mapping.
- SPEC hibernation + mailbox suites for behavior conventions.

## 4. Requirements

### RF-001: Wake on addressed delivery
When a mailbox message is persisted for a recipient that resolves to a hibernated session row, schedule `ResumeAsync` (auto) — the agent respawns and its first inbox read sees the message. Unresolvable/human recipients are skipped silently.

### RF-002: Preference + throttle
`AgentHibernationPrefs` gains `AutoResumeOnMailbox` (default `true`). A per-session `LastAutoResumeUtc` guard suppresses re-resume storms (≥60s apart); failed resumes record a reason and do not retry until the next message or the throttle window passes.

### RF-003: No wake on broadcast-only
`@all`/`@idle` fan-out does not resume hibernated sessions; only explicit recipient addresses (`<cli>`, `<taskId>`, `@worktree:<runId>` bound to a session) trigger it.

## 5. API Contract

No REST changes. Feed entry reuse: `SessionResumed` with `reason: "mailbox"`.

## 6. Acceptance Criteria

- [ ] Hibernated session + addressed `mailbox_send` → session resumes and the message is delivered.
- [ ] Broadcast-only mail → no resume.
- [ ] Two messages inside the throttle window → one resume.
- [ ] `AutoResumeOnMailbox = false` → delivery recorded, session stays hibernated.
- [ ] Resume failure → feed reason logged; no crash-loop.

## 7. Task Plan

- [ ] **P1** — recipient→hibernated-session resolution + `ResumeAsync(auto)` + pref + throttle; unit tests.
- [ ] **P2** — prefs UI toggle + feed reason surfacing.

## 8. Organization Guardrails

- Resume path reuses existing `AgentHibernationService` — no duplicate respawn logic.
- Never log mailbox content in wake logs (privacy boundary).
- Portuguese BDD test names.

## 9. Definition of Done

- [ ] RFs implemented; tests green; SPEC → `Done`; PR merged.

## Open Questions

- Should `@worktree:<runId>` wake a hibernated session bound to that worktree? Default yes — the address is explicit.
