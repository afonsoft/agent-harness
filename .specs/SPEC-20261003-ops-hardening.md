# SPEC-20261003-ops-hardening: docker pinning, client dedup, RAG test button

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Ops Hardening |
| Product / System | agent-harness |
| Module / Bounded Context | Infra + Integrations + Settings UI |
| Change type | Refactor / Infra |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261003-ops-hardening` |
| Technical owner | afonsoft |
| Status | Draft |
| Date | 2026-10-03 |
| Target agent | Devin |
| Related SPECs | SPEC-20261003-mcp-tool-surface |
| Gap keys | GAP-ops-docker-pin, GAP-impl-duplicate-apiclient, GAP-impl-rag-test-connection |

---

## 1. Executive Summary

### Problem

Three small operational debts:

- **Docker base image unpinned:** Dockerfile uses floating `…:10.0`
  tags — builds are not reproducible; dependabot covers NuGet +
  Actions but not Docker.
- **Duplicated `TaskboardApiClient`:** two copies —
  `src/Taskboard.Cli/Services/TaskboardApiClient.cs:18` and
  `src/Taskboard.Mcp/Services/TaskboardApiClient.cs:29` — drift risk.
- **No RAG connectivity test:** Settings edits `Rag:Url/ApiKey`
  blind — Jira already has `TestConnectionAsync` (Program.cs:1577)
  but the RAG MCP URL does not; `mcp/status` reports provisioning of
  the CLIs, not whether the RAG endpoint itself is reachable.

### Objective

Pin base images (digest + dependabot docker ecosystem), extract one
shared API client, and add a "test connection" action for the RAG
MCP server in Settings.

## 2. Scope

**In scope:**
- Pin Dockerfile base images by digest (record the tag+digest); add `docker` (or `dockerfile`) ecosystem to `.github/dependabot.yml` — **NOTE: dependabot.yml edit needs the user's workflow-approval exception confirmed; if refused, document pinning policy in docs instead.**
- Extract shared `TaskboardApiClient` into a common project (e.g. `Taskboard.Application.Contracts` consumers or a small `Taskboard.Http` shared lib — choose the lightest option matching layering rules) used by both Cli and Mcp.
- `GET/POST /api/settings/rag/test` (or reuse existing settings endpoint pattern): server does an MCP handshake/`tools/list` against `Rag:Url` with the configured key, times out in ~10s, returns ok/error; Settings RAG section gets a "Test connection" button + result badge.

**Out of scope:**
- Renaming/moving other shared services.
- RAG provisioning logic changes.
- Health-check redesign.

## 3. Technical Context

- `Dockerfile` stages: sdk build/test + aspnet runtime.
- `.github/dependabot.yml` — nuget + github-actions ecosystems today.
- `src/Taskboard.Server/Program.cs` settings endpoints ~1940+;
  `api.MapGet("mcp/status", …)` ~3035; Jira test at ~1577 (pattern to copy).
- `src/Taskboard.Blazor/Components/Pages/Settings.razor` RAG fields ~182-228.
- `Taskboard.Mcp` SDK client (`McpClient`) already used by `ChatMcpClientManager` — reuse for the handshake.

**Files to read before implementing:**
- `Dockerfile`, `.github/dependabot.yml`, `docker-compose.yml`
- `src/Taskboard.Cli/Services/TaskboardApiClient.cs`
- `src/Taskboard.Mcp/Services/TaskboardApiClient.cs`
- `src/Taskboard.Integrations/Mcp/ChatMcpClientManager.cs` (MCP handshake usage)
- `src/Taskboard.Server/Program.cs` (Jira test + mcp/status patterns)
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`

**Files to create or modify:**
```text
Dockerfile
.github/dependabot.yml                          (pending approval note)
src/Taskboard.Cli/Services/TaskboardApiClient.cs (delete/move)
src/Taskboard.Mcp/Services/TaskboardApiClient.cs (delete/move)
(new shared client location)
src/Taskboard.Server/Program.cs                 (rag test endpoint)
src/Taskboard.Blazor/Components/Pages/Settings.razor
tests/Taskboard.Tests.Unit/…                    (client move + endpoint)
docs/installation.md / api.md                   (if pinning documented)
```

## 4. Requirements

### RF-001: reproducible base images
- **Description:** base `FROM` lines pinned by digest with tag kept for readability; dependabot docker ecosystem added (or doc-only policy if workflow file can't be touched).
- **Rules:** `docker build` reproducible; CI still green.

### RF-002: single API client
- **Description:** one `TaskboardApiClient` implementation shared by Cli and Mcp.
- **Rules:** no behavior change; both projects compile against the shared type; layering respected (no upward references).

### RF-003: RAG connectivity test
- **Description:** endpoint performs an MCP `tools/list` (or equivalent handshake) against `Rag:Url` using `Rag:ApiKey`; Settings UI button shows reachable/unreachable + latency or error message.
- **Rules:** 10s timeout; never returns/logs the API key; works when `Rag:Url` empty → clear "not configured" result.

**Business rules / invariants:**
- API key never appears in logs, responses, or the DOM beyond the existing masked field.
- Dependabot edit only with explicit user approval (workflow-file rule).

## 5. API Contract

**Endpoint:** `POST /api/settings/rag/test` (auth: same as settings endpoints)
**Response:** `{ "ok": true, "latencyMs": 123 }` or `{ "ok": false, "error": "<reason>" }` — never includes secrets.

## 6. Acceptance Criteria

- [ ] **Given** the Dockerfile **when** built twice **then** identical base image resolved (digest pinned).
- [ ] **Given** both projects **when** compiled **then** a single `TaskboardApiClient` type serves both.
- [ ] **Given** a valid RAG MCP URL+key **when** "Test connection" clicked **then** Settings shows reachable + latency.
- [ ] **Given** an unreachable URL **when** tested **then** a clear error, no stack trace, no key.

## 7. Task Plan

- [ ] **T1 — Discovery:** compare the two ApiClients (drift), pick shared home.
- [ ] **T2 — Implementation:** pin images, extract client, endpoint + button.
- [ ] **T3 — Verification:** build/test; live RAG test against a stub MCP server.
- [ ] **T4 — Validation:** docker build reproducible; tests green.
- [ ] **T5 — Done + PR.**

## 8. Organization Guardrails

- `.github/dependabot.yml` is inside the protected workflows surface — confirm approval before editing; else doc-only.
- Feature branch; no secret logging; warnings as errors.

## 9. Definition of Done

- [ ] RF-001..RF-003; ACs green.

## Open Questions / Pending Ambiguity

- Approval to edit `.github/dependabot.yml` (add `docker` ecosystem) — workflow-file rule. If denied: pin digests anyway + document update policy.
- Shared client home: prefer existing shared project over new csproj.
