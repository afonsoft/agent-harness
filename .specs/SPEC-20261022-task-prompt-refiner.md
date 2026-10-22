# SPEC-20261022-task-prompt-refiner

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `task-prompt-refiner` |
| Type | `Feature` |
| Stack | `.NET` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feat/task-prompt-refiner` |
| Status | `Draft` |
| Origin | ADD/SDD review 2026-10-22 — "sempre melhorar os prompts da tarefa" |

## 1. User Story

**As a** Harness operator running AI-Driven Development
**I want** every task prompt refined into a self-contained worker brief before it reaches the agent CLI — spec anchors, context, output contract and guardrails included — with the original preserved for audit
**So that** headless workers start with maximum signal instead of a one-line instruction, retry attempts carry fix-forward context, and the system keeps compounding prompt quality without human rewriting.

The `afonsoft/skills` catalog shows the target: `write-specs` produces the oracle; `execute-specs` maps every test to `RF-xxx`/`AC-xx`; `orchestrator` tiers autonomy. Today a delegated task prompt reaches the CLI verbatim — no spec anchor, no context, no report contract. This feature closes that gap at the dispatch seam.

## 2. Scope

**In scope:**
- `ITaskPromptRefiner` seam (Application.Contracts) + `TaskPromptRefiner` (Application) invoked at **every** dispatch path: `delegate_task`, `board_delegate_issue`/`from-issue`, `automation_run` fire, coordinator child materialization, headless `taskctl run`.
- Two refinement modes:
  - `template` (default) — deterministic brief assembly: goal, spec anchors, context block, output contract, guardrails. No LLM call; always fast, never fails.
  - `llm` (opt-in) — one bounded chat-completion call through the configured provider that rewrites the raw prompt into the brief; falls back to `template` output on any failure/timeout/refusal.
- Audit trail on `DelegationTask`: `PromptRaw` (as submitted), `PromptRefined` (what the worker received), `PromptRefinement` (`off|template|llm|llm-fallback`), `RefinedAt`.
- Retry-aware refinement: `retry_of` chains append the previous run's failure summary + exit code as fix-forward context (attempts already exist — the refiner consumes them).
- Toggle `Taskboard:Delegation:PromptRefinement` = `off|template|llm` (default `template`).

**Out of scope:**
- User-visible prompt editing UI (P2 candidate).
- Prompt caching across tasks.
- LLM-judge scoring of prompt quality.

## 3. Technical Context

- `DelegationTask.Prompt` is the single field every path writes; the refiner intercepts **before persist** so `Prompt` stays the submitted text and `PromptRefined` is what the dispatcher enqueues — no behavior change for callers that skip refinement.
- Dispatch seams: `DelegationService.CreateAsync` (used by `delegate_task`, coordinator children, `from-issue`), `AutomationRunner.FireAsync`, headless run endpoint.
- Spec anchoring: scan `.specs/SPEC-*.md` (repo workdir when bound, else `~/repos/<repo>`) for issue-number / keyword overlap (`#123`, title tokens); inject matched spec path + its RF/AC headings only (≤ 2 KB excerpt) — never the whole file.
- Context block sources (all existing): `AGENTS.md`/`CLAUDE.md` first heading block, `worktree.yaml` setup state (already on the task), repository path, base branch.
- LLM mode reuses the chat provider abstraction (`IChatProvider`/`ChatService` request path) with a fixed system prompt and `maxTokens` cap (~1200); timeout 20 s.

## 4. Requirements

- **RF-001** `ITaskPromptRefiner.RefineAsync(TaskPromptRefineRequest, ct)` → `TaskPromptRefineResult { Raw, Refined, Mode, SpecAnchors, AppliedRules }`. Pure service — no side effects besides the optional LLM call.
- **RF-002** Template brief shape (stable sections, omitted when empty):

  ```text
  ## Task
  <raw prompt>

  ## Spec anchors      ← matched SPEC path + RF/AC headings
  ## Context           ← repo, workdir, branch, setup state, conventions excerpt
  ## Attempt context   ← retry_of chain: prior failure summary (attempt N>1 only)
  ## Output contract   ← worker_done report: summary, files changed, checks run, blockers
  ## Guardrails        ← untrusted-input rule, no invented requirements, scope limits
  ```
- **RF-003** `llm` mode asks the provider to produce the same section shape; the result must contain the raw prompt verbatim (validated) or falls back to `template`.
- **RF-004** Every create path persists `PromptRefined`; dispatch uses it; `delegate_status`/DTO exposes both fields + refinement mode.
- **RF-005** Retry (`retry_of`) appends `## Attempt context` with the previous terminal detail (truncated ≤ 1 KB) — fix-forward instead of blind re-run.
- **RF-006** `off` preserves today's behavior exactly (single code path — refiner returns raw unchanged and mode `off`).
- **RF-007** Refinement never leaks secrets: `SecretScrubber` runs on context/spec excerpts before injection.
- **RF-008** Observability: `refined_prompt` run event (kind on the dispatch/session event) records mode, anchors found, byte delta — feeds the feed `AutomationRun`-style diagnostics channel.

## 5. API Contract

- `CreateDelegationTaskRequest` — unchanged shape; `prompt` remains the raw input. Response DTO gains `promptRefined`, `promptRefinement`.
- `POST /api/delegation/tasks` and `delegate_task` tool accept optional `"refine": "off|template|llm"` overriding the config default for that task.
- New request type (Contracts):

  ```csharp
  public sealed record TaskPromptRefineRequest(
      string RawPrompt, string? Cli, string? RepositoryPath,
      string? IssueRef, string? PriorFailureDetail, int Attempt);
  ```

- Config: `Taskboard:Delegation:PromptRefinement` (`off|template|llm`, default `template`), `Taskboard:Delegation:PromptRefineLlmModel` (optional override).

## 6. Acceptance Criteria

- **AC-01** Given `template` mode and a task bound to a repo with a matching `.specs/SPEC-*`, when the task is created, then `PromptRefined` contains `## Spec anchors` with the spec path and the dispatcher sends the refined text.
- **AC-02** Given `retry_of` a failed task, when the retry is created, then `PromptRefined` contains `## Attempt context` with the truncated prior failure.
- **AC-03** Given `llm` mode with the provider failing/timing out, when refined, then the result is the template brief and `PromptRefinement == "llm-fallback"`.
- **AC-04** Given mode `off`, when any path creates a task, then `PromptRefined == Prompt` byte-for-byte.
- **AC-05** Given an automation with a one-line prompt, when the dispatcher fires it, then the delegation task carries the refined brief (fire path uses the refiner).
- **AC-06** Given the raw prompt containing a secret-shaped string or the context excerpt containing one, when refined, then it is scrubbed in the output.

## 7. Task Plan

- **P1** — `ITaskPromptRefiner` + `TaskPromptRefiner` (template mode) + entity/migration fields + wire into `DelegationService.CreateAsync` + `AutomationRunner` + tests.
- **P2** — `llm` mode + retry attempt context + `refine` per-task override + `delegate_status` DTO fields.
- **P3** — refined-prompt run event + feed diagnostics + coordinator children refinement toggle.

## 8. Organization Guardrails

- No new NuGet package (LLM mode reuses existing provider abstraction).
- Refinement is non-blocking for task creation in `template`/`off` mode; `llm` adds ≤ 20 s worst case, isolated by cancellation token.
- Prompt size cap: refined ≤ 32 KB (cli-arg safe); overflow truncates context sections, never the raw task.

## 9. Definition of Done

- All AC green; dispatcher enqueues `PromptRefined`; audit fields visible in `delegate_status`/`GET /api/delegation/tasks/{id}`; spec + README updated; CI green.

## Open Questions

- Should the LLM refiner use the conversation's picked CLI profile or a fixed cheap model? (Recommended: fixed configured model — the CLI is the worker, not the refiner.)
