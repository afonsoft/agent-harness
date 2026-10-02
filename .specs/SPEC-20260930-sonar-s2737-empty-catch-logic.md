# SPEC-20260930-sonar-s2737-empty-catch-logic

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S2737` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-empty-catch-logic` |
| Ticket | SonarQube rule `csharpsquid:S2737` (2 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 2} |
| Estimated effort | ~10 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S2737` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 2 unresolved `csharpsquid:S2737` issue(s): "Add logic to this catch clause or eliminate it and rethrow the exception automatically.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (2 issue(s), rule `csharpsquid:S2737` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application/Chat/ChatService.cs`
- `src/Taskboard.Integrations/Agents/JsonRpcAcpClient.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDukLOLkK9GZAB4jb_W` | `src/Taskboard.Application/Chat/ChatService.cs` | 120 | MINOR | Add logic to this catch clause or eliminate it and rethrow the exception automatically. |
| `AaDbpH3gkXWrHeTEYN3T` | `src/Taskboard.Integrations/Agents/JsonRpcAcpClient.cs` | 95 | MINOR | Add logic to this catch clause or eliminate it and rethrow the exception automatically. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S2737` at all listed locations
- **Description:** Add logic to the catch clause or let the exception propagate.
- **Rules:** Remove try/catch if nothing is done.
- **Input → Output:** code flagged by `csharpsquid:S2737` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S2737` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-empty-catch-logic`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-empty-catch-logic`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S2737` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S2737` occurrences resolved (2/2).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
