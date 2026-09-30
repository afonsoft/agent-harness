# SPEC-20260930-sonar-s4487-unread-private-field

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S4487` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-unread-private-field` |
| Ticket | SonarQube rule `csharpsquid:S4487` (2 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'CRITICAL': 2} |
| Estimated effort | ~10 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S4487` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 2 unresolved `csharpsquid:S4487` issue(s): "Remove this unread private field '_diff' or refactor the code to use its value.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (2 issue(s), rule `csharpsquid:S4487` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor`
- `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHkWkXWrHeTEYNvi` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 149 | CRITICAL | Remove this unread private field '_diff' or refactor the code to use its value. |
| `AaDbpHz5kXWrHeTEYN2M` | `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` | 40 | CRITICAL | Remove this unread private field '_lastStartFailed' or refactor the code to use its value. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S4487` at all listed locations
- **Description:** Remove the unread private field or use its value.
- **Rules:** Delete if dead; wire into logic if intended.
- **Input → Output:** code flagged by `csharpsquid:S4487` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S4487` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-unread-private-field`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-unread-private-field`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S4487` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S4487` occurrences resolved (2/2).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
