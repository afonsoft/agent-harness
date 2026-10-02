# SPEC-20260930-sonar-s6444-process-execution-timeout

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S6444` |
| Type | Security |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-process-execution-timeout` |
| Ticket | SonarQube rule `csharpsquid:S6444` (17 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | VULNERABILITY |
| Severities | {'MINOR': 17} |
| Estimated effort | ~85 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S6444` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 17 unresolved `csharpsquid:S6444` issue(s): "Pass a timeout to limit the execution time.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (17 issue(s), rule `csharpsquid:S6444` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs`
- `src/Taskboard.Blazor/Services/RepositoryFilter.cs`
- `src/Taskboard.Domain/Entities/AgentCliDefinition.cs`
- `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs`
- `src/Taskboard.Integrations/Agents/DockerCliSpawner.cs`
- `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs`
- `src/Taskboard.Integrations/Skills/SkillsRepository.cs`
- `src/Taskboard.Integrations/Vscode/VscodeInstallService.cs`
- `src/Taskboard.Server/Program.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDpll2OTboaTa85QyoP` | `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` | 105 | MINOR | Pass a timeout to limit the execution time. |
| `AaDplmUoTboaTa85QyoR` | `src/Taskboard.Integrations/Agents/DockerCliSpawner.cs` | 25 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHjukXWrHeTEYNve` | `src/Taskboard.Server/Program.cs` | 1268 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHxPkXWrHeTEYN1X` | `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` | 20 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHxPkXWrHeTEYN1U` | `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` | 24 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHxPkXWrHeTEYN1W` | `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` | 26 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHxPkXWrHeTEYN1V` | `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` | 28 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHphkXWrHeTEYNyq` | `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs` | 33 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHjukXWrHeTEYNvd` | `src/Taskboard.Server/Program.cs` | 3053 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpH3pkXWrHeTEYN3W` | `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` | 19 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpH3pkXWrHeTEYN3X` | `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` | 25 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHzvkXWrHeTEYN2K` | `src/Taskboard.Integrations/Vscode/VscodeInstallService.cs` | 28 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHzvkXWrHeTEYN2L` | `src/Taskboard.Integrations/Vscode/VscodeInstallService.cs` | 34 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHunkXWrHeTEYN0E` | `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` | 274 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHzdkXWrHeTEYN2D` | `src/Taskboard.Integrations/Skills/SkillsRepository.cs` | 37 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHoukXWrHeTEYNyW` | `src/Taskboard.Blazor/Services/RepositoryFilter.cs` | 16 | MINOR | Pass a timeout to limit the execution time. |
| `AaDbpHunkXWrHeTEYN0D` | `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` | 331 | MINOR | Pass a timeout to limit the execution time. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S6444` at all listed locations
- **Description:** Pass an explicit timeout to every flagged process/exec invocation so execution time is bounded.
- **Rules:** Use the overload/API that accepts a timeout (e.g. CliWrap WithTimeout / Command.ExecuteAsync(ct) with a TimeoutCancellationTokenSource, Process.WaitForExit(timeout), or CancellationTokenSource with a bounded delay). Choose a timeout consistent with neighboring calls.
- **Input → Output:** code flagged by `csharpsquid:S6444` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S6444` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-process-execution-timeout`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-process-execution-timeout`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S6444` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S6444` occurrences resolved (17/17).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
