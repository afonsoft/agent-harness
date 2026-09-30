# SPEC-20260930-sonar-s7747-js-iterable-direct

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `javascript:S7747` |
| Type | Refactor |
| Stack | Frontend |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-js-iterable-direct` |
| Ticket | SonarQube rule `javascript:S7747` (1 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 1} |
| Estimated effort | ~5 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `javascript:S7747` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 1 unresolved `javascript:S7747` issue(s): "`for…of` can iterate over iterable, it's unnecessary to convert to an array.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (1 issue(s), rule `javascript:S7747` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Client/wwwroot/js/terminal.js`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHv2kXWrHeTEYN0o` | `src/Taskboard.Client/wwwroot/js/terminal.js` | 299 | MINOR | `for…of` can iterate over iterable, it's unnecessary to convert to an array. |

## 4. Requirements

### RF-001: Resolve `javascript:S7747` at all listed locations
- **Description:** Iterate the iterable directly; drop the array conversion.
- **Rules:** for-of on the iterable.
- **Input → Output:** code flagged by `javascript:S7747` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `javascript:S7747` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-js-iterable-direct`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-js-iterable-direct`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `javascript:S7747` occurrences listed above.

## 9. Definition of Done

- [ ] All `javascript:S7747` occurrences resolved (1/1).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
