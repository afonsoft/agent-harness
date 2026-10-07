# SPEC-20261013-chat-risk-approvals: Risk-tiered tool approval (`auto` preset)

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Risk-tiered approvals — per-call risk classifier + `auto` preset |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Application (gate, classifier) + Contracts + Server + Blazor |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261013-chat-risk-approvals` |
| Status | `Draft` |
| Depends on | SPEC-20261005-chat-tool-approval |
| Reference | OpenHands security analyzer (`LOW/MEDIUM/HIGH` + confirmation mode); COMPARISON §3 G-A2 |

## 1. Executive Summary

### Problem

Approval policies are per-*tool* (`ask|never|allow` + presets): `shell_exec`
either always asks or never does. True autonomy needs per-*call* judgment —
`ls -la` is not `rm -rf ~`. Without a sandbox (host execution, owner
decision), this classifier is the safety layer.

### Solution

1. **`IChatToolRiskClassifier`** evaluates `(tool, arguments,
   ChatToolContext)` → `{risk: low|medium|high, reason}` via static rules —
   deterministic, no LLM call.
2. **New policy value `auto`** on `ChatApprovalPolicy` (per-tool + preset):
   `low` → allow silently; `medium` → allow + emit a `risk.notice` so the
   tool card shows what ran elevated; `high` → ask (approval card carries
   the reason).
3. **Risk shown to the user**: approval card + tool call card get a
   `low/medium/high` badge with the reason (e.g. `writes outside workspace`,
   `destructive command`, `network egress`).

### Scope

In scope: classifier, rules table, `auto` policy semantics, UI badges,
tests. Out of scope: ML/LLM judge, sandboxing, audit log retention beyond
existing approval rows.

## 2. Requisitos

### Funcionais

- RF-001 `ChatToolRisk` enum (`low|medium|high`) + `ChatToolRiskVerdict`
  `{Risk, Reason}` contract in Application.Contracts.
- RF-002 `IChatToolRiskClassifier.Classify(IChatTool tool, JsonElement args,
  ChatToolContext ctx)`. Default `StaticChatToolRiskClassifier` rules:
  - **high**: shell commands matching destructive patterns (`rm -rf`, `mkfs`,
    `dd of=`, `:(){`, `shutdown|reboot`, `kill -9 -1`, `chmod -R 777 /`,
    `> /dev/sd`, curl|wget piped to sh); file writes/deletes outside the
    workspace jail; `worktree_remove`; `git push --force`; package/system
    mutations (`apt`, `npm i -g`); env/secret access patterns.
  - **medium**: file writes inside workspace/jail; `shell_exec` other
    mutating commands (`git commit`, `npm i`, `docker`); `delegate_*`
    creating runs; `fs_move`; `apply_patch`; network `fetch`/`web_search`
    POSTs; attachment upload/delete; job/schedule create.
  - **low**: everything else (reads, list, search, memory read, board read,
    todo list/write, datetime, calculator).
- RF-003 `auto` policy value added to `ChatApprovalPolicy`
  (`ToolPolicyPrefix` accepts `ask|never|allow|auto`); preset chip gains
  `auto`. `never` stays strictest (ask everything), `allow` unchanged.
- RF-004 Gate (`ChatApprovalCoordinator` path): policy `auto` → classify →
  `low` proceeds silently; `medium` proceeds and emits a `risk.notice`
  event rendered as a small badge on the tool card (`ran unreviewed —
  medium: <reason>`); `high` suspends with the normal approval flow, the
  card showing `risk: high — <reason>`.
- RF-005 `RequiresConfirmation` tools and mutating tools in plan mode keep
  their existing hard gates (risk never downgrades those).
- RF-006 Settings: Configuration tab lists the classifier rules summary +
  default preset unchanged (`ask` for mutating). Per-tool override accepts
  `auto`.

### Não-funcionais

- Classification is pure/instant (<1ms), no I/O, no LLM.
- Rules table is data (ordered `(pattern, risk, reason)` entries) —
  testable, extendable, no reflection.
- Every auto-elevated action is still logged via the existing
  approval/audit rows (`auto` decision recorded as `DecidedBy=auto:<risk>`).

## 3. Arquitetura

```mermaid
flowchart LR
  Call[tool call] --> Gate[ApprovalCoordinator]
  Gate --> Policy{policy}
  Policy -->|auto| Cls[RiskClassifier → low/med/high]
  Cls -->|low| Run[execute]
  Cls -->|medium| Run + Notice[tool card badge]
  Cls -->|high| Card[approval card + reason]
  Policy -->|ask/never/allow| Existing[existing paths]
```

- Classifier lives in `Taskboard.Application/Chat/`; rules table beside it
  (`ChatRiskRules.cs`) so tests enumerate them without reflection.
- `ChatCapabilityRegistry` surfaces `DefaultRisk` hint per tool (static
  table) so the UI can pre-label pending approvals before arguments parse.

## 4. Fases

- **P1** — classifier + rules + `auto` policy + gate wiring + audit mark.
- **P2** — UI badges (approval card + tool card), preset chip `auto`,
  Settings documentation row.

## 5. Testes

- Unit: rules table covers every built-in mutating tool; `rm -rf`/`git push
  -f`/`write outside jail` → high; reads → low; write inside → medium;
  `auto` never bypasses `RequiresConfirmation` or plan-mode deny.
- Integration: `auto` preset + `shell_exec rm -rf` → approval requested with
  reason; `ls` → silent; file write in workspace → medium badge in stream.
- Regression: `ask|never|allow` policies behave exactly as today.

## 6. Open questions

1. Classify MCP-adapter tools? Proposed: default `medium` (opaque args),
   `high` when the tool name matches mutating patterns — explicit allowlist
   later.
2. Config-driven custom rules (JSON in RuntimeConfiguration)? Proposed:
   later — code table first; user-defined patterns are a follow-up spec.
