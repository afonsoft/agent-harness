# SPEC-20260930-sonar-s2486-js-empty-catch

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `javascript:S2486` |
| Type | Refactor |
| Stack | Frontend |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-js-empty-catch` |
| Ticket | SonarQube rule `javascript:S2486` (2 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 2} |
| Estimated effort | ~120 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `javascript:S2486` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 2 unresolved `javascript:S2486` issue(s): "Handle this exception, don't catch it at all, or explain in a comment why it is ignored.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (2 issue(s), rule `javascript:S2486` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Client/wwwroot/js/taskboard.js`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHwBkXWrHeTEYN0r` | `src/Taskboard.Client/wwwroot/js/taskboard.js` | 137 | MINOR | Handle this exception, don't catch it at all, or explain in a comment why it is ignored. |
| `AaDbpHwBkXWrHeTEYN0s` | `src/Taskboard.Client/wwwroot/js/taskboard.js` | 159 | MINOR | Handle this exception, don't catch it at all, or explain in a comment why it is ignored. |

## 4. Requirements

### RF-001: Resolve `javascript:S2486` at all listed locations
- **Description:** Handle or justify the ignored exception in each flagged catch.
- **Rules:** Add handling/comment per message.
- **Input → Output:** code flagged by `javascript:S2486` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `javascript:S2486` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-js-empty-catch`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-js-empty-catch`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `javascript:S2486` occurrences listed above.

## 9. Definition of Done

- [ ] All `javascript:S2486` occurrences resolved (2/2).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
