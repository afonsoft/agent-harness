# SPEC-20260930-sonar-s6667-log-caught-exception

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S6667` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-log-caught-exception` |
| Ticket | SonarQube rule `csharpsquid:S6667` (7 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 7} |
| Estimated effort | ~35 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S6667` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 7 unresolved `csharpsquid:S6667` issue(s): "Logging in a catch clause should pass the caught exception as a parameter.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (7 issue(s), rule `csharpsquid:S6667` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application/CliMetrics/CliMetricsService.cs`
- `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs`
- `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs`
- `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHxFkXWrHeTEYN1K` | `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` | 149 | MINOR | Logging in a catch clause should pass the caught exception as a parameter. |
| `AaDbpHvskXWrHeTEYN0j` | `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` | 53 | MINOR | Logging in a catch clause should pass the caught exception as a parameter. |
| `AaDbpHvskXWrHeTEYN0k` | `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` | 58 | MINOR | Logging in a catch clause should pass the caught exception as a parameter. |
| `AaDbpHxFkXWrHeTEYN1M` | `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` | 216 | MINOR | Logging in a catch clause should pass the caught exception as a parameter. |
| `AaDbpHxFkXWrHeTEYN1N` | `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` | 222 | MINOR | Logging in a catch clause should pass the caught exception as a parameter. |
| `AaDbpHxPkXWrHeTEYN1O` | `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` | 82 | MINOR | Logging in a catch clause should pass the caught exception as a parameter. |
| `AaDbpHz5kXWrHeTEYN2O` | `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` | 299 | MINOR | Logging in a catch clause should pass the caught exception as a parameter. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S6667` at all listed locations
- **Description:** Pass the caught exception object to the logger in each flagged catch.
- **Rules:** Use logger.LogError(ex, ...) / LogWarning(ex, ...) overloads.
- **Input → Output:** code flagged by `csharpsquid:S6667` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S6667` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-log-caught-exception`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-log-caught-exception`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S6667` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S6667` occurrences resolved (7/7).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
