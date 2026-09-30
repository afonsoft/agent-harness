# SPEC-20260930-sonar-s107-reduce-parameter-count

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S107` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-reduce-parameter-count` |
| Ticket | SonarQube rule `csharpsquid:S107` (33 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MAJOR': 33} |
| Estimated effort | ~660 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S107` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 33 unresolved `csharpsquid:S107` issue(s): "Constructor has 8 parameters, which is greater than the 7 authorized.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (33 issue(s), rule `csharpsquid:S107` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application.Contracts/CliDb/ICliDatabaseReader.cs`
- `src/Taskboard.Application.Contracts/CliMetrics/ICliMetricsRepository.cs`
- `src/Taskboard.Application/AiChat/AiChatService.cs`
- `src/Taskboard.Application/Chat/ChatService.cs`
- `src/Taskboard.Application/Harness/FinOpsService.cs`
- `src/Taskboard.Application/Harness/PipelineEngine.cs`
- `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs`
- `src/Taskboard.Domain.Shared/Specs/LivingSpecification.cs`
- `src/Taskboard.Domain/Agents/AgentRunEvent.cs`
- `src/Taskboard.Domain/Entities/AgentCliDefinition.cs`
- `src/Taskboard.Domain/Entities/AiChatThread.cs`
- `src/Taskboard.Domain/Entities/Chat/ChatMessage.cs`
- `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs`
- `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs`
- `src/Taskboard.Domain/Entities/Harness/RunCostMetric.cs`
- `src/Taskboard.Domain/Entities/Harness/VerificationReport.cs`
- `src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs`
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs`
- `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs`
- `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Server/Services/AgentControlService.cs`
- `src/Taskboard.Server/Services/AgentSessionManager.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDukLOLkK9GZAB4jb_V` | `src/Taskboard.Application/Chat/ChatService.cs` | 33 | MAJOR | Constructor has 8 parameters, which is greater than the 7 authorized. |
| `AaDukK4nkK9GZAB4jb_N` | `src/Taskboard.Domain/Entities/Chat/ChatMessage.cs` | 35 | MAJOR | Constructor has 12 new parameters, which is greater than the 7 authorized. |
| `AaDpll2OTboaTa85QyoO` | `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` | 57 | MAJOR | Method has 8 parameters, which is greater than the 7 authorized. |
| `AaDpll2OTboaTa85QyoN` | `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` | 82 | MAJOR | Method has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHvEkXWrHeTEYN0O` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 38 | MAJOR | Constructor has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHvhkXWrHeTEYN0g` | `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs` | 33 | MAJOR | Constructor has 9 parameters, which is greater than the 7 authorized. |
| `AaDbpHjukXWrHeTEYNvY` | `src/Taskboard.Server/Program.cs` | 1430 | MAJOR | Lambda has 9 parameters, which is greater than the 7 authorized. |
| `AaDbpHvEkXWrHeTEYN0S` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 696 | MAJOR | Method has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHrSkXWrHeTEYNzB` | `src/Taskboard.Application.Contracts/CliDb/ICliDatabaseReader.cs` | 55 | MAJOR | Method has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHrKkXWrHeTEYNy_` | `src/Taskboard.Application.Contracts/CliMetrics/ICliMetricsRepository.cs` | 25 | MAJOR | Method has 13 parameters, which is greater than the 7 authorized. |
| `AaDbpHvPkXWrHeTEYN0c` | `src/Taskboard.Application/Harness/FinOpsService.cs` | 35 | MAJOR | Constructor has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHsAkXWrHeTEYNzU` | `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` | 39 | MAJOR | Constructor has 13 new parameters, which is greater than the 7 authorized. |
| `AaDbpHsAkXWrHeTEYNzS` | `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` | 67 | MAJOR | Method has 13 parameters, which is greater than the 7 authorized. |
| `AaDbpHsAkXWrHeTEYNzT` | `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` | 77 | MAJOR | Method has 9 parameters, which is greater than the 7 authorized. |
| `AaDbpHuEkXWrHeTEYNz6` | `src/Taskboard.Application/AiChat/AiChatService.cs` | 39 | MAJOR | Constructor has 16 parameters, which is greater than the 7 authorized. |
| `AaDbpHrpkXWrHeTEYNzK` | `src/Taskboard.Domain/Entities/AiChatThread.cs` | 70 | MAJOR | Method has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHtVkXWrHeTEYNzw` | `src/Taskboard.Domain/Agents/AgentRunEvent.cs` | 53 | MAJOR | Constructor has 16 new parameters, which is greater than the 7 authorized. |
| `AaDbpH01kXWrHeTEYN25` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1633 | MAJOR | Method has 10 parameters, which is greater than the 7 authorized. |
| `AaDbpHjPkXWrHeTEYNvD` | `src/Taskboard.Server/Services/AgentControlService.cs` | 41 | MAJOR | Constructor has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHjEkXWrHeTEYNul` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 37 | MAJOR | Constructor has 9 parameters, which is greater than the 7 authorized. |
| `AaDbpHjukXWrHeTEYNvZ` | `src/Taskboard.Server/Program.cs` | 3006 | MAJOR | Local function has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHjukXWrHeTEYNva` | `src/Taskboard.Server/Program.cs` | 3020 | MAJOR | Local function has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHtFkXWrHeTEYNzu` | `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` | 42 | MAJOR | Constructor has 8 new parameters, which is greater than the 7 authorized. |
| `AaDbpHtFkXWrHeTEYNzt` | `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` | 69 | MAJOR | Method has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHsakXWrHeTEYNze` | `src/Taskboard.Domain/Entities/Harness/RunCostMetric.cs` | 45 | MAJOR | Constructor has 11 new parameters, which is greater than the 7 authorized. |
| `AaDbpHeikXWrHeTEYNuJ` | `src/Taskboard.Domain.Shared/Specs/LivingSpecification.cs` | 11 | MAJOR | Constructor has 15 parameters, which is greater than the 7 authorized. |
| `AaDbpHrSkXWrHeTEYNzA` | `src/Taskboard.Application.Contracts/CliDb/ICliDatabaseReader.cs` | 36 | MAJOR | Method has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHsskXWrHeTEYNzh` | `src/Taskboard.Domain/Entities/Harness/VerificationReport.cs` | 50 | MAJOR | Method has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHsSkXWrHeTEYNzd` | `src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs` | 73 | MAJOR | Method has 8 parameters, which is greater than the 7 authorized. |
| `AaDbpHrpkXWrHeTEYNzL` | `src/Taskboard.Domain/Entities/AiChatThread.cs` | 40 | MAJOR | Constructor has 9 new parameters, which is greater than the 7 authorized. |
| `AaDbpHrpkXWrHeTEYNzM` | `src/Taskboard.Domain/Entities/AiChatThread.cs` | 94 | MAJOR | Method has 9 parameters, which is greater than the 7 authorized. |
| `AaDbpHz5kXWrHeTEYN2N` | `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` | 43 | MAJOR | Constructor has 10 parameters, which is greater than the 7 authorized. |
| `AaDbpHzUkXWrHeTEYN1_` | `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs` | 48 | MAJOR | Constructor has 8 parameters, which is greater than the 7 authorized. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S107` at all listed locations
- **Description:** Reduce parameter count to <= 7 at each flagged method/ctor.
- **Rules:** Introduce a parameter object/record grouping related parameters.
- **Input → Output:** code flagged by `csharpsquid:S107` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S107` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-reduce-parameter-count`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-reduce-parameter-count`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S107` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S107` occurrences resolved (33/33).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
