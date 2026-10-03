# SPEC-20261003-mcp-tool-surface: expand Taskboard.Mcp tools to harness parity

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | MCP Tool Surface Expansion |
| Product / System | agent-harness |
| Module / Bounded Context | Taskboard.Mcp |
| Change type | Feature / API |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261003-mcp-tool-surface` |
| Technical owner | afonsoft |
| Status | In implementation |
| Date | 2026-10-03 |
| Target agent | Devin |
| Related SPECs | SPEC-20261001-chat-mcp-client (Done), SPEC-20260917-rag-mcp-provisioning |
| Gap keys | GAP-impl-mcp-tool-surface |

---

## 1. Executive Summary

### Problem

`Taskboard.Mcp` — the MCP server agent CLIs connect to — exposes only
4 tools: `get_issue_history`, `list_github_issue_comments`,
`add_github_issue_comment`, `cloud_status`. An external agent wired to
the harness cannot see the board, read a SPEC, move an issue through
the workflow, or check job/agent-run status — capabilities `taskctl`
and the REST API already have. The harness sells "workbench for
orchestrating AI coding agents" but its own MCP surface cannot drive
that workbench.

### Objective

Extend `Taskboard.Mcp` toward parity with `taskctl`: board/issue read
+ write tools, SPEC read, and run/job status — all backed by the
existing internal API client rather than duplicating logic.

## 2. Scope

**In scope:**
- New tools (suggested set — trim/extend at implementation per REST surface):
  - `list_issues` (board items w/ status, priority, assignee — via existing API)
  - `get_issue <id>` (detail incl. description)
  - `move_issue <id> <status>` (workflow move)
  - `get_spec <path-or-slug>` (read `.specs` entry surfaced by the API)
  - `list_jobs` / `get_run_status <runId>` (agent-run and job status)
- Input validation + clear error payloads (not found / conflict) consistent with the 4 existing tools.
- Unit tests per tool mirroring existing test conventions.
- `docs/` MCP tool list updated (where the 4 tools are documented today).
- Consolidate the duplicated `TaskboardApiClient` (Cli vs Mcp copies) — see SPEC-20261003-ops-hardening; either land the shared client here or keep the Mcp copy until that SPEC runs (note the dependency).

**Out of scope:**
- RAG/knowledge server (external MCP — untouched).
- Mutating GitHub state beyond what tools already do (no new GH writes besides move_issue if backed by board API, not GitHub).
- Auth model changes — the Mcp server already proxies with its configured credentials.

## 3. Technical Context

`src/Taskboard.Mcp/Tools/TaskboardTools.cs` (111 lines, 4 tools) +
`src/Taskboard.Mcp/Program.cs`. HTTP calls go through
`src/Taskboard.Mcp/Services/TaskboardApiClient.cs` (near-duplicate of
`src/Taskboard.Cli/Services/TaskboardApiClient.cs`). Server REST
surface lives in `src/Taskboard.Server/Program.cs` (`api.Map*` groups).
`manage-taskboard` skill documents the taskctl command surface for parity.

**Files to read before implementing:**
- `src/Taskboard.Mcp/Tools/TaskboardTools.cs`
- `src/Taskboard.Mcp/Services/TaskboardApiClient.cs`
- `src/Taskboard.Server/Program.cs` (board/issues/specs/jobs endpoints)
- `tests/` for existing MCP tool tests
- `.claude/skills/manage-taskboard/SKILL.md` (parity target)

**Files to create or modify:**
```text
src/Taskboard.Mcp/Tools/TaskboardTools.cs       (extend)
src/Taskboard.Mcp/Services/TaskboardApiClient.cs (new methods)
tests/Taskboard.Tests.Unit/Mcp/TaskboardToolsTests.cs (extend/new)
docs/api.md (+ pt-br) or docs/mcp doc where tools are listed
```

## 4. Requirements

### RF-001: board read tools
- **Description:** `list_issues` returns board items (id, title, status, priority, labels); `get_issue` returns one item's detail.
- **Rules:** reuse existing REST endpoints; empty board → empty list, not error.

### RF-002: workflow write tool
- **Description:** `move_issue` transitions an issue through the board workflow via the existing API (respecting version/conflict semantics).
- **Rules:** invalid transition or stale version → structured error; never bypasses server rules.

### RF-003: spec read tool
- **Description:** `get_spec` returns a `.specs` file's content/metadata through the API (read-only).
- **Rules:** path traversal rejected; only `.specs/` files.

### RF-004: run/job status tools
- **Description:** expose agent-run state and job status equivalents of `taskctl` status commands.

### RF-005: tests + docs
- **Description:** each tool has unit tests; the tool list in docs is updated.

**Business rules / invariants:**
- All tools read/write through the HTTP API client — no direct DB access from Taskboard.Mcp.
- Error payloads carry code + message, never stack traces or PII.
- Tool count stays discoverable — keep names consistent with existing snake_case style.

## 5. API Contract

MCP tool surface (stdio/SSE as already configured). Each tool returns the existing DTO JSON from the internal API. Errors: `{"error": "<code>", "message": "<text>"}` — 404 not found, 409 version conflict, 400 invalid transition.

## 6. Acceptance Criteria

- [ ] **Given** a populated board **when** `list_issues` is called **then** all items return with status/priority.
- [ ] **Given** issue X **when** `move_issue` with valid transition **then** state changes and the board reflects it.
- [ ] **Given** issue X and a stale version **when** `move_issue` **then** structured conflict error.
- [ ] **Given** `.specs/SPEC-*.md` exists **when** `get_spec` **then** content returned; `../etc/passwd` → rejection.
- [ ] **Given** an agent run **when** `get_run_status` **then** current state returned.

## 7. Task Plan

- [ ] **T1 — Discovery:** map REST endpoints to each tool; check taskctl parity list.
- [ ] **T2 — Implementation:** client methods + tools + validation.
- [ ] **T3 — Verification:** unit tests per tool; live `taskctl`/`mcp` smoke.
- [ ] **T4 — Validation:** build/test/format; docs updated.
- [ ] **T5 — Done + PR.**

## 8. Organization Guardrails

- No secrets logged; tool outputs never include API keys/tokens.
- BDD test names pt-BR; feature branch; warnings as errors.

## 9. Definition of Done

- [ ] RF-001..RF-005; ACs green; docs updated.

## Open Questions / Pending Ambiguity

- Exact REST endpoints backing list/move/spec (implementation maps them; if an endpoint is missing, a thin server endpoint may be added — that expansion is in-scope only if required).
- Shared `TaskboardApiClient` consolidation — do it here or in ops-hardening SPEC (dependency noted).
