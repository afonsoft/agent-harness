# SPEC-20260930-sonar-s1118-utility-class-ctor

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S1118` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-utility-class-ctor` |
| Ticket | SonarQube rule `csharpsquid:S1118` (2 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | CODE_SMELL |
| Severities | {'MAJOR': 2} |
| Estimated effort | ~20 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S1118` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 2 unresolved `csharpsquid:S1118` issue(s): "Add a 'private' constructor or the 'static' keyword to the class declaration.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (2 issue(s), rule `csharpsquid:S1118` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Integrations/Harness/Verification/CompilerErrorParser.cs`
- `src/Taskboard.Mcp/Program.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHxpkXWrHeTEYN1a` | `src/Taskboard.Integrations/Harness/Verification/CompilerErrorParser.cs` | 12 | MAJOR | Add a 'private' constructor or the 'static' keyword to the class declaration. |
| `AaDbpH6KkXWrHeTEYN4G` | `src/Taskboard.Mcp/Program.cs` | 10 | MAJOR | Add a 'protected' constructor or the 'static' keyword to the class declaration. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S1118` at all listed locations
- **Description:** Add a private constructor or static modifier to the utility class.
- **Rules:** Prevent instantiation of all-static class.
- **Input → Output:** code flagged by `csharpsquid:S1118` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S1118` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-utility-class-ctor`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-utility-class-ctor`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S1118` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S1118` occurrences resolved (2/2).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
