# SPEC-20260930-sonar-s2325-make-member-static

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S2325` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-make-member-static` |
| Ticket | SonarQube rule `csharpsquid:S2325` (9 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 9} |
| Estimated effort | ~45 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S2325` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 9 unresolved `csharpsquid:S2325` issue(s): "Make 'ValidateRollupColumn' a static method.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (9 issue(s), rule `csharpsquid:S2325` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor`
- `src/Taskboard.Blazor/Components/BoardView.razor`
- `src/Taskboard.Blazor/Components/Pages/Gantt.razor`
- `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs`
- `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs`
- `src/Taskboard.Integrations/Harness/Security/SecretScrubber.cs`
- `src/Taskboard.Integrations/Harness/Verification/DotNetVerificationEngine.cs`
- `src/Taskboard.Server/Services/AcpClientToolHandler.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHxPkXWrHeTEYN1S` | `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` | 312 | MINOR | Make 'ValidateRollupColumn' a static method. |
| `AaDbpHiYkXWrHeTEYNuc` | `src/Taskboard.Server/Services/AcpClientToolHandler.cs` | 131 | MINOR | Make 'ReadTextFile' a static method. |
| `AaDbpHoKkXWrHeTEYNyJ` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 433 | MINOR | Make 'RenderPlan' a static method. |
| `AaDbpHoKkXWrHeTEYNyO` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 448 | MINOR | Make 'RenderMetric' a static method. |
| `AaDbpHxakXWrHeTEYN1Y` | `src/Taskboard.Integrations/Harness/Verification/DotNetVerificationEngine.cs` | 100 | MINOR | Make 'ParseNewestTrx' a static method. |
| `AaDbpHyMkXWrHeTEYN1r` | `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` | 18 | MINOR | Make 'Validate' a static method. |
| `AaDbpHx7kXWrHeTEYN1d` | `src/Taskboard.Integrations/Harness/Security/SecretScrubber.cs` | 17 | MINOR | Make 'Scrub' a static method. |
| `AaDbpHoTkXWrHeTEYNyS` | `src/Taskboard.Blazor/Components/BoardView.razor` | 63 | MINOR | Make 'GetPrioritySwatchStyle' a static method. |
| `AaDbpHkFkXWrHeTEYNvg` | `src/Taskboard.Blazor/Components/Pages/Gantt.razor` | 224 | MINOR | Make 'GetBarClass' a static method. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S2325` at all listed locations
- **Description:** Make the flagged member static (it does not use instance state).
- **Rules:** Mark method/property static; adjust call sites.
- **Input → Output:** code flagged by `csharpsquid:S2325` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S2325` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-make-member-static`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-make-member-static`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S2325` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S2325` occurrences resolved (9/9).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
