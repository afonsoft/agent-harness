# SPEC-20260930-sonar-s3267-simplify-loop-linq

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S3267` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-simplify-loop-linq` |
| Ticket | SonarQube rule `csharpsquid:S3267` (18 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 18} |
| Estimated effort | ~90 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S3267` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 18 unresolved `csharpsquid:S3267` issue(s): "Loops should be simplified using the "Where" LINQ method". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (18 issue(s), rule `csharpsquid:S3267` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs`
- `src/Taskboard.Application.Contracts/GitHub/GitHubBoardColumnExtensions.cs`
- `src/Taskboard.Application.Contracts/GitHub/GitHubBoardGrouper.cs`
- `src/Taskboard.Application/Agents/StaleRunReaper.cs`
- `src/Taskboard.Application/Harness/PipelineEngine.cs`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor`
- `src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs`
- `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs`
- `src/Taskboard.Integrations/Harness/GitWorktreeManager.cs`
- `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs`
- `src/Taskboard.Integrations/Harness/Verification/CompilerErrorParser.cs`
- `src/Taskboard.Integrations/Skills/SkillsRepository.cs`
- `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDtEWM7B79WDrchFHf5` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 661 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDpllQ1TboaTa85QyoE` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 276 | MINOR | Loop should be simplified by calling Select(container => container.Name) |
| `AaDplmCATboaTa85QyoQ` | `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` | 185 | MINOR | Loops should be simplified using the "Any" LINQ method |
| `AaDbpHyfkXWrHeTEYN10` | `src/Taskboard.Integrations/Harness/GitWorktreeManager.cs` | 128 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDbpHkWkXWrHeTEYNvs` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 249 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDbpHudkXWrHeTEYN0B` | `src/Taskboard.Application/Agents/StaleRunReaper.cs` | 34 | MINOR | Loop should be simplified by calling Select(run => run.Id) |
| `AaDbpHvEkXWrHeTEYN0a` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 217 | MINOR | Loop should be simplified by calling Select(stage => stage.StageKey) |
| `AaDbpHs8kXWrHeTEYNzm` | `src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs` | 26 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDbpHs8kXWrHeTEYNzn` | `src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs` | 41 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDbpHxPkXWrHeTEYN1Q` | `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` | 181 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDbpHxpkXWrHeTEYN1b` | `src/Taskboard.Integrations/Harness/Verification/CompilerErrorParser.cs` | 22 | MINOR | Loop should be simplified by calling Select(match => match.Groups) |
| `AaDbpHyEkXWrHeTEYN1f` | `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` | 214 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDbpHzdkXWrHeTEYN2C` | `src/Taskboard.Integrations/Skills/SkillsRepository.cs` | 147 | MINOR | Loops should be simplified using the "Any" LINQ method |
| `AaDbpHphkXWrHeTEYNyn` | `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs` | 39 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDbpHphkXWrHeTEYNyp` | `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs` | 102 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDbpHq4kXWrHeTEYNy9` | `src/Taskboard.Application.Contracts/GitHub/GitHubBoardColumnExtensions.cs` | 126 | MINOR | Loops should be simplified using the "FirstOrDefault" LINQ method |
| `AaDbpHq4kXWrHeTEYNy8` | `src/Taskboard.Application.Contracts/GitHub/GitHubBoardColumnExtensions.cs` | 143 | MINOR | Loops should be simplified using the "Where" LINQ method |
| `AaDbpHqukXWrHeTEYNy7` | `src/Taskboard.Application.Contracts/GitHub/GitHubBoardGrouper.cs` | 62 | MINOR | Loops should be simplified using the "Where" LINQ method |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S3267` at all listed locations
- **Description:** Simplify each flagged loop using LINQ (Where/Select/etc).
- **Rules:** Replace manual filter/projection loops with LINQ while preserving semantics.
- **Input → Output:** code flagged by `csharpsquid:S3267` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S3267` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-simplify-loop-linq`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-simplify-loop-linq`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S3267` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S3267` occurrences resolved (18/18).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
