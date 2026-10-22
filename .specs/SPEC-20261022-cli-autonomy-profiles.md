# SPEC-20261022-cli-autonomy-profiles

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cli-autonomy-profiles` |
| Type | `Feature` |
| Stack | `.NET` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feat/cli-autonomy-profiles` |
| Status | `Implemented` |
| Origin | ADD/SDD review 2026-10-22 — "mais autonomia ao chamar os CLIs" |

## 1. User Story

**As a** Harness operator running autonomous task delegation
**I want** each agent CLI invocation to carry an explicit autonomy level — `plan` (read-only), `edit` (workspace-write), `full` (unrestricted inside a disposable worktree) — mapped to that CLI's real permission flags
**So that** a research task cannot write, an implementation task runs without stalling on TTY approvals, and `full` autonomy only ever runs inside an isolation session the harness can throw away.

Today `AgentCliInvocation.BuildArguments` **hardcodes maximum autonomy** for every builtin CLI (`claude --dangerously-skip-permissions`, `codex exec --approve-for-me`, `opencode run --auto`, `aider --yes-always`, `kiro --trust-all-tools`, `copilot --allow-all-tools`, `antigravity --dangerously-skip-permissions`, `devin --permission-mode dangerous`). That is correct for unattended execution — but it is not a dial: a read-only review task and a refactor task get the same write power. The `orchestrator` skill's T1/T2/T3 tiers need a concrete CLI-level expression; this feature provides it.

## 2. Scope

**In scope:**
- `AgentAutonomy` enum (Domain.Shared): `Plan` | `Edit` | `Full`. `Edit` = default.
- Per-CLI **autonomy profile table** (`AgentCliAutonomy`): for each `AgentType`, the argv that expresses each level — replacing today's hardcoded always-max flags:
  - `claude`: plan → `--permission-mode plan`; edit → `--permission-mode acceptEdits`; full → `--dangerously-skip-permissions`
  - `codex`: plan → `exec --sandbox read-only`; edit → `exec --approve-for-me --sandbox workspace-write`; full → `exec --sandbox danger-full-access --approve-for-me`
  - `opencode`: plan → `run` (permissions auto-deny, read-only tools only — verify); edit → `run --auto` with write-tools allowed (profile def); full → `run --auto` unrestricted
  - `devin`: plan → `--permission-mode auto`… (verify print-mode support); edit/full → `--permission-mode dangerous`
  - `antigravity`/`agy`: edit/full → `--dangerously-skip-permissions`; plan → omit + documented limitation
  - `aider`: edit → `--yes-always`; plan → `--yes-always --no-auto-commits` + read-only prompt contract; full → same as edit (no stronger flag — documented)
  - `copilot`: plan → `-p` (default prompts); edit → `--allow-all-tools`; full → `--allow-all-tools` (+ `--allow-all-paths` if supported — verify)
  - `kiro`: edit/full → `chat --no-interactive --trust-all-tools`
  - `kimi`/`grok`/`qwen`/`cline`/`cn`: documented current behavior — `yolo`/`--approval-mode yolo` for gemini-fork CLIs where verified; otherwise keep `-p` and mark the level advisory-only.
- `AgentExecutionRequest.Autonomy` + `DelegationTask.Autonomy` + `CreateDelegationTaskRequest.autonomy` + `delegate_task`/`board_delegate_issue`/`automation_create` `autonomy` param; `AgentCliInvocation.BuildArguments(agentType, prompt, …, autonomy)`.
- **Isolation gate**: `Full` is refused (`DomainException` `autonomy_requires_isolation`) unless the run executes inside a worktree/isolation session (`WorktreeRunId`/`ExistingWorktreeRunId` set, or `use_worktree=true`). `Plan`/`Edit` allowed anywhere.
- `ExtraAgentArgs` whitelist gains the per-CLI autonomy flags from the profile table (only those verified per CLI — the whitelist remains strict).
- The worker preamble (`BuildPreamble`) announces `autonomy` so the worker knows its own permission envelope.
- Config: `Taskboard:Agents:DefaultAutonomy` (`edit`), `Taskboard:Agents:AllowFullAutonomy` (`false` — must be opted in globally even inside worktrees).

**Out of scope:**
- Interactive (PTY) sessions — autonomy flags apply to headless/one-shot dispatch only; interactive CLI sessions keep user-driven permission prompts.
- Per-run dynamic downgrade mid-execution.
- Custom CLI defs: `autonomy` accepted only when the def declares `capability.autonomyFlags` in its profile (new optional field); otherwise `DomainException`.

## 3. Technical Context

- `BuildArguments` already splices `extraArgs` before the prompt — the autonomy argv rides the same splice point, **before** `extraArgs` (whitelist args may then further restrict but not escalate: validation rejects an extra-arg that contradicts the resolved level, e.g. `--dangerously-skip-permissions` passed while `plan`).
- `IAgentLaunchResolver` is the single funnel for model/effort resolution — autonomy resolves in the same pass (`LaunchResolution` gains `Autonomy` + resolved argv).
- Worktree binding facts are already on the task (`WorktreeRunId`, `RepositoryPath`) — the gate consults them at launch time, not create time, so a reused session still qualifies.
- The `sdd-worker-brief` spec ties `taskKind research|review` → recommended `plan` (orchestrator/tool descriptions should suggest it; enforcement stays opt-in).

## 4. Requirements

- **RF-001** `AgentCliAutonomy` static table: `(AgentType, level) → IReadOnlyList<string> argv`; unknown/absent level → the CLI's documented default, and the profile marks it `AdvisoryOnly`.
- **RF-002** `BuildArguments` signature gains `AgentAutonomy autonomy = Edit`; the level's argv replaces today's hardcoded always-max flags. Default keeps current behavior (`Edit` resolves to today's argv for each CLI — zero regression).
- **RF-003** Isolation gate at launch resolution: `Full` + no isolation session → refused `autonomy_requires_isolation`; `Full` + `AllowFullAutonomy=false` → refused `autonomy_full_disabled`.
- **RF-004** `DelegationTask.Autonomy` persisted (migration), surfaced on `delegate_status` DTO + run record.
- **RF-005** Tool/API param `autonomy` on `delegate_task`, `board_delegate_issue`, `automation_create` (+`CreateAutomationRequest.Autonomy` persisted on the automation row, applied per fire).
- **RF-006** `ExtraAgentArgs` whitelist: autonomy flags from the profile table accepted **only** for the CLI they belong to and only at-or-below the task's resolved level; contradiction → `invalid_extra_args`.
- **RF-007** Preamble `autonomy` field + worker brief tier section names the resolved level — the worker can plan within its envelope.
- **RF-008** Custom defs: `CliProfile.capability.autonomyFlags: { plan: [...], edit: [...], full: [...] }` optional; absent → requesting a non-default level on a custom CLI fails fast.
- **RF-009** Feed/diagnostics: run event records resolved autonomy + argv head (flags only, never the prompt).

## 5. API Contract

- `CreateDelegationTaskRequest`/`DelegateTask` tool: `"autonomy": "plan|edit|full"` (optional).
- `AgentExecutionRequest.Autonomy: AgentAutonomy` (default `Edit`).
- `AgentCliInvocation.BuildArguments(agentType, prompt, tier, modelName, omitModelFlag, extraArgs, autonomy)`.
- Config keys: `Taskboard:Agents:DefaultAutonomy` (default `edit`), `Taskboard:Agents:AllowFullAutonomy` (default `false`).
- Errors: `autonomy_requires_isolation` (400), `autonomy_full_disabled` (400), `autonomy_unsupported` (400).

## 6. Acceptance Criteria

- **AC-01** Given `claude` + `plan`, when argv is built, then `--permission-mode plan` is present and `--dangerously-skip-permissions` absent.
- **AC-02** Given `codex` + `edit`, when built, then argv contains `--sandbox workspace-write` and `--approve-for-me`.
- **AC-03** Given `full` without worktree isolation, when a task is dispatched, then the run fails fast with `autonomy_requires_isolation` and the delegation task shows the refusal reason.
- **AC-04** Given `full` + worktree + `AllowFullAutonomy=true`, when dispatched, then the CLI argv carries the full-autonomy flags.
- **AC-05** Given `ExtraAgentArgs` `--dangerously-skip-permissions` on a `plan` task, when saved, then rejected `invalid_extra_args`.
- **AC-06** Given no `autonomy` param anywhere, when any existing path dispatches, then argv equals today's output byte-for-byte (regression guard per CLI).
- **AC-07** Given a custom def without `autonomyFlags`, when `autonomy=full` requested, then `autonomy_unsupported`.
- **AC-08** Given the worker preamble, when dispatched, then `autonomy` names the resolved level.

## 7. Task Plan

- **P1** — `AgentAutonomy` enum + `AgentCliAutonomy` table (verified flags for claude/codex/opencode/devin/antigravity/aider/copilot/kiro) + `BuildArguments` param + isolation gate + migration + tests.
- **P2** — `autonomy` param through tools/API/automations + ExtraAgentArgs whitelist extension + preamble field.
- **P3** — custom-def `autonomyFlags` + run-event recording + UI chip showing task autonomy.

## 8. Organization Guardrails

- Security-sensitive: `AllowFullAutonomy` defaults `false`; isolation gate is enforced at launch (defense in depth, not just create-time).
- Flags must be verified against each CLI's current `--help`/docs at implementation time — the profile table cites the source per row (comment with doc URL/flag provenance).
- No prompt content ever enters the profile argv — flags only.

## 9. Definition of Done

- All AC green incl. the per-CLI byte-for-byte regression test (AC-06); migration applied; spec + docs updated; CI green.

## Open Questions

- Should `Edit` also require isolation? (Recommended: no — `edit` = today's default behavior; gating it would break the workspace flow. `Full` is the hardened tier.)
- Interactive PTY sessions: worth a `--permission-mode` default for `claude`/`devin` terminals too? (Deferred — out of scope.)
