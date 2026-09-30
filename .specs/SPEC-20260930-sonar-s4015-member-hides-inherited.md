# SPEC-20260930-sonar-s4015-member-hides-inherited

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S4015` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-member-hides-inherited` |
| Ticket | SonarQube rule `csharpsquid:S4015` (8 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'CRITICAL': 8} |
| Estimated effort | ~16 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S4015` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 8 unresolved `csharpsquid:S4015` issue(s): "This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.GitHubIssueHistorySettings>.ExecuteAsync(Spectre.Console.Cli.CommandContex". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (8 issue(s), rule `csharpsquid:S4015` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Cli/Program.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDtDTMUod7YJFS1ySKk` | `src/Taskboard.Cli/Program.cs` | 250 | CRITICAL | This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.GitHubIssueHistorySettin |
| `AaDtDTMUod7YJFS1ySKn` | `src/Taskboard.Cli/Program.cs` | 274 | CRITICAL | This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.GitHubIssueCommentListSe |
| `AaDtDTMUod7YJFS1ySKm` | `src/Taskboard.Cli/Program.cs` | 310 | CRITICAL | This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.GitHubIssueCommentAddSet |
| `AaDtDTMUod7YJFS1ySKl` | `src/Taskboard.Cli/Program.cs` | 143 | CRITICAL | This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.EmptySettings>.ExecuteAs |
| `AaDtDTMUod7YJFS1ySKh` | `src/Taskboard.Cli/Program.cs` | 161 | CRITICAL | This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.CloudLoginSettings>.Exec |
| `AaDtDTMUod7YJFS1ySKg` | `src/Taskboard.Cli/Program.cs` | 187 | CRITICAL | This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.CloudStatusSettings>.Exe |
| `AaDtDTMUod7YJFS1ySKi` | `src/Taskboard.Cli/Program.cs` | 201 | CRITICAL | This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.CloudLogoutSettings>.Exe |
| `AaDtDTMUod7YJFS1ySKj` | `src/Taskboard.Cli/Program.cs` | 219 | CRITICAL | This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.ContextCurrentSettings>. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S4015` at all listed locations
- **Description:** Rename/change the member that hides an inherited member.
- **Rules:** Choose a distinct name/signature so intent is explicit.
- **Input → Output:** code flagged by `csharpsquid:S4015` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S4015` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-member-hides-inherited`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-member-hides-inherited`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S4015` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S4015` occurrences resolved (8/8).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
