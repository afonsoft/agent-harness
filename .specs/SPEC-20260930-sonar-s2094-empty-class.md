# SPEC-20260930-sonar-s2094-empty-class

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S2094` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-empty-class` |
| Ticket | SonarQube rule `csharpsquid:S2094` (5 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 5} |
| Estimated effort | ~25 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S2094` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 5 unresolved `csharpsquid:S2094` issue(s): "Remove this empty class, write its code or make it an "interface".". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (5 issue(s), rule `csharpsquid:S2094` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application.Contracts/Operations/OperationLog.cs`
- `src/Taskboard.Cli/Program.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHqGkXWrHeTEYNyt` | `src/Taskboard.Application.Contracts/Operations/OperationLog.cs` | 50 | MINOR | Remove this empty class, write its code or make it an "interface". |
| `AaDbpHqGkXWrHeTEYNyu` | `src/Taskboard.Application.Contracts/Operations/OperationLog.cs` | 55 | MINOR | Remove this empty class, write its code or make it an "interface". |
| `AaDbpH5wkXWrHeTEYN3_` | `src/Taskboard.Cli/Program.cs` | 181 | MINOR | Remove this empty class, write its code or make it an "interface". |
| `AaDbpH5wkXWrHeTEYN4A` | `src/Taskboard.Cli/Program.cs` | 195 | MINOR | Remove this empty class, write its code or make it an "interface". |
| `AaDbpH5wkXWrHeTEYN4B` | `src/Taskboard.Cli/Program.cs` | 213 | MINOR | Remove this empty class, write its code or make it an "interface". |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S2094` at all listed locations
- **Description:** Remove the empty class or give it content/make it an interface.
- **Rules:** Delete if unused; otherwise implement.
- **Input → Output:** code flagged by `csharpsquid:S2094` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S2094` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-empty-class`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-empty-class`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S2094` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S2094` occurrences resolved (5/5).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
