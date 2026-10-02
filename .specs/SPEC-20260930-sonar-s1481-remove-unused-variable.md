# SPEC-20260930-sonar-s1481-remove-unused-variable

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S1481` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-remove-unused-variable` |
| Ticket | SonarQube rule `csharpsquid:S1481` (6 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 6} |
| Estimated effort | ~30 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S1481` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 6 unresolved `csharpsquid:S1481` issue(s): "Remove the unused local variable 'tool'.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (6 issue(s), rule `csharpsquid:S1481` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor`
- `src/Taskboard.Blazor/Components/Cockpit/RunTimeline.razor`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHk4kXWrHeTEYNwd` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 131 | MINOR | Remove the unused local variable 'tool'. |
| `AaDbpHk4kXWrHeTEYNwe` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 137 | MINOR | Remove the unused local variable 'group'. |
| `AaDbpH01kXWrHeTEYN2p` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 628 | MINOR | Remove the unused local variable 'pending'. |
| `AaDbpHoKkXWrHeTEYNyL` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 395 | MINOR | Remove the unused local variable 'tool'. |
| `AaDbpHoKkXWrHeTEYNyM` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 400 | MINOR | Remove the unused local variable 'output'. |
| `AaDbpHn1kXWrHeTEYNyE` | `src/Taskboard.Blazor/Components/Cockpit/RunTimeline.razor` | 19 | MINOR | Remove the unused local variable 'tool'. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S1481` at all listed locations
- **Description:** Remove each flagged unused local variable.
- **Rules:** Delete the declaration/assignment.
- **Input → Output:** code flagged by `csharpsquid:S1481` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S1481` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-remove-unused-variable`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-remove-unused-variable`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S1481` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S1481` occurrences resolved (6/6).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
