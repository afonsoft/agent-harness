# SPEC-20260930-sonar-s2486-empty-catch-block

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S2486` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-empty-catch-block` |
| Ticket | SonarQube rule `csharpsquid:S2486` (8 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 8} |
| Estimated effort | ~480 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S2486` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 8 unresolved `csharpsquid:S2486` issue(s): "Handle the exception or explain in a comment why it can be ignored.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (8 issue(s), rule `csharpsquid:S2486` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Blazor/Services/TaskboardClient.cs`
- `src/Taskboard.Integrations/Execution/ExecutableCommand.cs`
- `src/Taskboard.Integrations/Terminal/PtySession.cs`
- `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs`
- `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHpFkXWrHeTEYNye` | `src/Taskboard.Blazor/Services/TaskboardClient.cs` | 855 | MINOR | Handle the exception or explain in a comment why it can be ignored. |
| `AaDbpHz5kXWrHeTEYN2Q` | `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` | 393 | MINOR | Handle the exception or explain in a comment why it can be ignored. |
| `AaDbpHwwkXWrHeTEYN1G` | `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` | 396 | MINOR | Handle the exception or explain in a comment why it can be ignored. |
| `AaDbpHwmkXWrHeTEYN04` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 309 | MINOR | Handle the exception or explain in a comment why it can be ignored. |
| `AaDbpHwmkXWrHeTEYN09` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 329 | MINOR | Handle the exception or explain in a comment why it can be ignored. |
| `AaDbpHwmkXWrHeTEYN08` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 352 | MINOR | Handle the exception or explain in a comment why it can be ignored. |
| `AaDbpHwmkXWrHeTEYN0-` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 365 | MINOR | Handle the exception or explain in a comment why it can be ignored. |
| `AaDbpHywkXWrHeTEYN13` | `src/Taskboard.Integrations/Execution/ExecutableCommand.cs` | 50 | MINOR | Handle the exception or explain in a comment why it can be ignored. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S2486` at all listed locations
- **Description:** Handle the caught exception or document why ignoring it is safe.
- **Rules:** Log the exception or add an explicit justification comment; rethrow when appropriate.
- **Input → Output:** code flagged by `csharpsquid:S2486` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S2486` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-empty-catch-block`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-empty-catch-block`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S2486` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S2486` occurrences resolved (8/8).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
