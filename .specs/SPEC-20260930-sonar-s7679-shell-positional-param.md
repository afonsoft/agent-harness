# SPEC-20260930-sonar-s7679-shell-positional-param

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `shelldre:S7679` |
| Type | Refactor |
| Stack | Infra |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-shell-positional-param` |
| Ticket | SonarQube rule `shelldre:S7679` (17 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | CODE_SMELL |
| Severities | {'MAJOR': 17} |
| Estimated effort | ~85 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `shelldre:S7679` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 17 unresolved `shelldre:S7679` issue(s): "Assign this positional parameter to a local variable.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (17 issue(s), rule `shelldre:S7679` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `install-cli.sh`
- `scripts/tests/check-spec-status.test.sh`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpH7OkXWrHeTEYN4K` | `scripts/tests/check-spec-status.test.sh` | 15 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4L` | `scripts/tests/check-spec-status.test.sh` | 15 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4M` | `scripts/tests/check-spec-status.test.sh` | 15 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4N` | `scripts/tests/check-spec-status.test.sh` | 15 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4O` | `scripts/tests/check-spec-status.test.sh` | 15 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4P` | `scripts/tests/check-spec-status.test.sh` | 15 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4Q` | `scripts/tests/check-spec-status.test.sh` | 17 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4R` | `scripts/tests/check-spec-status.test.sh` | 17 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4S` | `scripts/tests/check-spec-status.test.sh` | 17 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4T` | `scripts/tests/check-spec-status.test.sh` | 17 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4U` | `scripts/tests/check-spec-status.test.sh` | 17 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4V` | `scripts/tests/check-spec-status.test.sh` | 18 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4W` | `scripts/tests/check-spec-status.test.sh` | 18 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4X` | `scripts/tests/check-spec-status.test.sh` | 18 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4Y` | `scripts/tests/check-spec-status.test.sh` | 18 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7OkXWrHeTEYN4Z` | `scripts/tests/check-spec-status.test.sh` | 18 | MAJOR | Assign this positional parameter to a local variable. |
| `AaDbpH7FkXWrHeTEYN4J` | `install-cli.sh` | 146 | MAJOR | Assign this positional parameter to a local variable. |

## 4. Requirements

### RF-001: Resolve `shelldre:S7679` at all listed locations
- **Description:** Assign each flagged positional parameter to a named local variable.
- **Rules:** e.g. local target="$1".
- **Input → Output:** code flagged by `shelldre:S7679` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `shelldre:S7679` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-shell-positional-param`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-shell-positional-param`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `shelldre:S7679` occurrences listed above.

## 9. Definition of Done

- [ ] All `shelldre:S7679` occurrences resolved (17/17).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
