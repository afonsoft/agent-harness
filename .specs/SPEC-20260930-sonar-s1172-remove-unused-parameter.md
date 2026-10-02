# SPEC-20260930-sonar-s1172-remove-unused-parameter

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S1172` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-remove-unused-parameter` |
| Ticket | SonarQube rule `csharpsquid:S1172` (13 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | CODE_SMELL |
| Severities | {'MAJOR': 13} |
| Estimated effort | ~65 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S1172` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 13 unresolved `csharpsquid:S1172` issue(s): "Remove this unused method parameter 'enumeratorCancelled'.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (13 issue(s), rule `csharpsquid:S1172` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application/Chat/ChatService.cs`
- `src/Taskboard.Application/Harness/PipelineEngine.cs`
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs`
- `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs`
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs`
- `src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs`
- `src/Taskboard.Integrations/GitHub/GitHubService.cs`
- `src/Taskboard.Server/Services/AcpClientToolHandler.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDukLOLkK9GZAB4jb_c` | `src/Taskboard.Application/Chat/ChatService.cs` | 276 | MAJOR | Remove this unused method parameter 'enumeratorCancelled'. |
| `AaDukLOLkK9GZAB4jb_b` | `src/Taskboard.Application/Chat/ChatService.cs` | 447 | MAJOR | Remove this unused method parameter 'provider'. |
| `AaDukKWIkK9GZAB4jb-1` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1258 | MAJOR | Remove this unused method parameter 'capability'. |
| `AaDbpHvEkXWrHeTEYN0T` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 244 | MAJOR | Remove this unused method parameter 'cancellationToken'. |
| `AaDbpH01kXWrHeTEYN24` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1290 | MAJOR | Remove this unused method parameter 'payloadJson'. |
| `AaDbpH01kXWrHeTEYN20` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1347 | MAJOR | Remove this unused method parameter 'payloadJson'. |
| `AaDbpHiYkXWrHeTEYNuf` | `src/Taskboard.Server/Services/AcpClientToolHandler.cs` | 184 | MAJOR | Remove this unused method parameter 'sessionId'. |
| `AaDbpH0KkXWrHeTEYN2T` | `src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs` | 63 | MAJOR | Remove this unused method parameter 'sandbox'. |
| `AaDbpH3YkXWrHeTEYN3O` | `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` | 441 | MAJOR | Remove this unused method parameter 'cancellationToken'. |
| `AaDbpH3pkXWrHeTEYN3V` | `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` | 131 | MAJOR | Remove this unused method parameter 'kind'. |
| `AaDbpH3YkXWrHeTEYN3R` | `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` | 539 | MAJOR | Remove this unused method parameter 'cancellationToken'. |
| `AaDbpH3zkXWrHeTEYN3a` | `src/Taskboard.Integrations/GitHub/GitHubService.cs` | 477 | MAJOR | Remove this unused method parameter 'repositoryFullName'. |
| `AaDbpH3zkXWrHeTEYN3Z` | `src/Taskboard.Integrations/GitHub/GitHubService.cs` | 457 | MAJOR | Remove this unused method parameter 'cancellationToken'. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S1172` at all listed locations
- **Description:** Remove each flagged unused parameter, or use it.
- **Rules:** Update all call sites; for interface implementations prefer discarding via signature change if allowed.
- **Input → Output:** code flagged by `csharpsquid:S1172` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S1172` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-remove-unused-parameter`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-remove-unused-parameter`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S1172` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S1172` occurrences resolved (13/13).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
