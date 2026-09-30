# SPEC-20260930-sonar-s2930-dispose-idisposable

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S2930` |
| Type | Bugfix |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-dispose-idisposable` |
| Ticket | SonarQube rule `csharpsquid:S2930` (4 issue(s)) |
| Status | Approved |
| Sonar type | BUG |
| Severities | {'BLOCKER': 4} |
| Estimated effort | ~40 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S2930` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 4 unresolved `csharpsquid:S2930` issue(s): "Dispose '_cts' when it is no longer needed.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (4 issue(s), rule `csharpsquid:S2930` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Blazor/Components/Pages/AgentInstallDialog.razor`
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Blazor/Components/Pages/VscodeEditor.razor`
- `src/Taskboard.Integrations/Terminal/PtySession.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHl4kXWrHeTEYNxQ` | `src/Taskboard.Blazor/Components/Pages/VscodeEditor.razor` | 120 | BLOCKER | Dispose '_cts' when it is no longer needed. |
| `AaDbpHkNkXWrHeTEYNvh` | `src/Taskboard.Blazor/Components/Pages/AgentInstallDialog.razor` | 63 | BLOCKER | Dispose '_cts' when it is no longer needed. |
| `AaDbpHwmkXWrHeTEYN02` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 119 | BLOCKER | Dispose '_pumpCts' when it is no longer needed. |
| `AaDbpHlHkXWrHeTEYNwf` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 791 | BLOCKER | Dispose '_pollCts' when it is no longer needed. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S2930` at all listed locations
- **Description:** Dispose the flagged IDisposable (field/local) when it is no longer needed.
- **Rules:** Fields: dispose in Dispose()/DisposeAsync; locals: use `using`/`await using` declarations.
- **Input → Output:** code flagged by `csharpsquid:S2930` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S2930` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-dispose-idisposable`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-dispose-idisposable`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S2930` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S2930` occurrences resolved (4/4).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
