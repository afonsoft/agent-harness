# SPEC-20260930-sonar-s108-empty-code-block

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S108` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-empty-code-block` |
| Ticket | SonarQube rule `csharpsquid:S108` (41 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MAJOR': 41} |
| Estimated effort | ~205 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S108` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 41 unresolved `csharpsquid:S108` issue(s): "Either remove or fill this block of code.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (41 issue(s), rule `csharpsquid:S108` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application.Contracts/AiChat/ProblemDetailReader.cs`
- `src/Taskboard.Application/Harness/PipelineEngine.cs`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Blazor/Components/Pages/Cockpit.razor`
- `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor`
- `src/Taskboard.Blazor/Components/Pages/Jobs.razor`
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Blazor/Services/TaskboardClient.cs`
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs`
- `src/Taskboard.Integrations/Execution/ExecutableCommand.cs`
- `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs`
- `src/Taskboard.Integrations/Mcp/JsonConfigMerger.cs`
- `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs`
- `src/Taskboard.Integrations/Terminal/PtySession.cs`
- `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs`
- `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Server/Services/CockpitEventStream.cs`
- `src/Taskboard.Server/Services/ManagedJobService.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDt34v780awsUzQdyod` | `src/Taskboard.Blazor/Components/Pages/Jobs.razor` | 179 | MAJOR | Either remove or fill this block of code. |
| `AaDt34ju80awsUzQdyoc` | `src/Taskboard.Server/Services/ManagedJobService.cs` | 73 | MAJOR | Either remove or fill this block of code. |
| `AaDpXQtRyeT6D8TPfvBG` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1038 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHmMkXWrHeTEYNxU` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 361 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHmMkXWrHeTEYNxV` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 394 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHmMkXWrHeTEYNxg` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 659 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHkWkXWrHeTEYNvj` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 294 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHkWkXWrHeTEYNvk` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 307 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHkWkXWrHeTEYNvw` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 483 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHjukXWrHeTEYNvU` | `src/Taskboard.Server/Program.cs` | 3082 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHjukXWrHeTEYNvV` | `src/Taskboard.Server/Program.cs` | 3103 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHqSkXWrHeTEYNyv` | `src/Taskboard.Application.Contracts/AiChat/ProblemDetailReader.cs` | 55 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHk4kXWrHeTEYNwD` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1127 | MAJOR | Either remove or fill this block of code. |
| `AaDbpH01kXWrHeTEYN2h` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 437 | MAJOR | Either remove or fill this block of code. |
| `AaDbpH01kXWrHeTEYN2m` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 525 | MAJOR | Either remove or fill this block of code. |
| `AaDbpH01kXWrHeTEYN2r` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 835 | MAJOR | Either remove or fill this block of code. |
| `AaDbpH01kXWrHeTEYN2w` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1115 | MAJOR | Either remove or fill this block of code. |
| `AaDbpH01kXWrHeTEYN22` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1333 | MAJOR | Either remove or fill this block of code. |
| `AaDbpH01kXWrHeTEYN2z` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1381 | MAJOR | Either remove or fill this block of code. |
| `AaDbpH01kXWrHeTEYN21` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1310 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHigkXWrHeTEYNug` | `src/Taskboard.Server/Services/CockpitEventStream.cs` | 38 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHvEkXWrHeTEYN0P` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 100 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHvEkXWrHeTEYN0Q` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 442 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHyMkXWrHeTEYN1t` | `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` | 101 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHyMkXWrHeTEYN1u` | `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` | 104 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHk4kXWrHeTEYNwJ` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1199 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHk4kXWrHeTEYNwX` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1602 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHk4kXWrHeTEYNwW` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1619 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHz5kXWrHeTEYN2R` | `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` | 394 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHwwkXWrHeTEYN1F` | `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` | 373 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHwwkXWrHeTEYN1H` | `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` | 397 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHwmkXWrHeTEYN05` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 296 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHwmkXWrHeTEYN06` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 310 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHwmkXWrHeTEYN1A` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 330 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHwmkXWrHeTEYN0_` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 353 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHwmkXWrHeTEYN1B` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 366 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHlHkXWrHeTEYNwj` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 849 | MAJOR | Either remove or fill this block of code. |
| `AaDbpH37kXWrHeTEYN3c` | `src/Taskboard.Integrations/Mcp/JsonConfigMerger.cs` | 122 | MAJOR | Either remove or fill this block of code. |
| `AaDbpH4DkXWrHeTEYN3g` | `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs` | 142 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHpFkXWrHeTEYNyf` | `src/Taskboard.Blazor/Services/TaskboardClient.cs` | 856 | MAJOR | Either remove or fill this block of code. |
| `AaDbpHywkXWrHeTEYN14` | `src/Taskboard.Integrations/Execution/ExecutableCommand.cs` | 50 | MAJOR | Either remove or fill this block of code. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S108` at all listed locations
- **Description:** Either implement or remove each flagged empty block.
- **Rules:** Remove dead blocks; if intentionally empty add a comment per rule guidance or restructure.
- **Input → Output:** code flagged by `csharpsquid:S108` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S108` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-empty-code-block`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-empty-code-block`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S108` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S108` occurrences resolved (41/41).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
