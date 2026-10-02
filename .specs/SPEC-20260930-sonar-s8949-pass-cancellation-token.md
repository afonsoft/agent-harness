# SPEC-20260930-sonar-s8949-pass-cancellation-token

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S8949` |
| Type | Bugfix |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-pass-cancellation-token` |
| Ticket | SonarQube rule `csharpsquid:S8949` (37 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | BUG |
| Severities | {'MAJOR': 37} |
| Estimated effort | ~37 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S8949` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 37 unresolved `csharpsquid:S8949` issue(s): "Pass the 'requestAborted' to this method to allow cancellation of the operation, or use 'CancellationToken.None' to opt out explicitly.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (37 issue(s), rule `csharpsquid:S8949` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application/AiChat/AiChatService.cs`
- `src/Taskboard.Application/Chat/ChatService.cs`
- `src/Taskboard.Application/Harness/PipelineEngine.cs`
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs`
- `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs`
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs`
- `src/Taskboard.Integrations/Execution/ProcessCommandRunner.cs`
- `src/Taskboard.Integrations/Harness/GitCommandRunner.cs`
- `src/Taskboard.Integrations/Terminal/PtySession.cs`
- `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs`
- `src/Taskboard.Server/Services/AgentSessionManager.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDukLOLkK9GZAB4jb_Y` | `src/Taskboard.Application/Chat/ChatService.cs` | 241 | MAJOR | Pass the 'requestAborted' to this method to allow cancellation of the operation, or use 'C |
| `AaDukLOLkK9GZAB4jb_Z` | `src/Taskboard.Application/Chat/ChatService.cs` | 243 | MAJOR | Pass the 'requestAborted' to this method to allow cancellation of the operation, or use 'C |
| `AaDbpHvEkXWrHeTEYN0Y` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 481 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHjEkXWrHeTEYNuq` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 138 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHjEkXWrHeTEYNu1` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 450 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHjEkXWrHeTEYNu2` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 450 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpH01kXWrHeTEYN2k` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 145 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpH01kXWrHeTEYN2t` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 803 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpH01kXWrHeTEYN23` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1426 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpH01kXWrHeTEYN26` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1557 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpH0TkXWrHeTEYN2V` | `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` | 57 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpH0TkXWrHeTEYN2W` | `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` | 114 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpH0TkXWrHeTEYN2X` | `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` | 132 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpH0TkXWrHeTEYN2Y` | `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` | 137 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHjEkXWrHeTEYNu0` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 429 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHjEkXWrHeTEYNux` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 545 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHjEkXWrHeTEYNuy` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 552 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHjEkXWrHeTEYNuz` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 554 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpH01kXWrHeTEYN2n` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 401 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHyokXWrHeTEYN11` | `src/Taskboard.Integrations/Execution/ProcessCommandRunner.cs` | 37 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHyokXWrHeTEYN12` | `src/Taskboard.Integrations/Execution/ProcessCommandRunner.cs` | 38 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHyVkXWrHeTEYN1x` | `src/Taskboard.Integrations/Harness/GitCommandRunner.cs` | 44 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHyVkXWrHeTEYN1y` | `src/Taskboard.Integrations/Harness/GitCommandRunner.cs` | 45 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHjEkXWrHeTEYNus` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 363 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHjEkXWrHeTEYNut` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 364 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHjEkXWrHeTEYNuu` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 366 | MAJOR | Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use |
| `AaDbpHjEkXWrHeTEYNu3` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 453 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHjEkXWrHeTEYNu4` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 481 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHjEkXWrHeTEYNu5` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 487 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHjEkXWrHeTEYNu6` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 507 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHjEkXWrHeTEYNu7` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 508 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHjEkXWrHeTEYNu8` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 510 | MAJOR | Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or |
| `AaDbpHuEkXWrHeTEYNz9` | `src/Taskboard.Application/AiChat/AiChatService.cs` | 428 | MAJOR | Pass the 'ct' to this method to allow cancellation of the operation, or use 'CancellationT |
| `AaDbpHwwkXWrHeTEYN1D` | `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` | 66 | MAJOR | Pass the 'this._sweepCts.Token' to this method to allow cancellation of the operation, or  |
| `AaDbpH3YkXWrHeTEYN3Q` | `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` | 213 | MAJOR | Pass the 'stoppingToken' to this method to allow cancellation of the operation, or use 'Ca |
| `AaDbpHwmkXWrHeTEYN1C` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 120 | MAJOR | Pass the 'this._pumpCts.Token' to this method to allow cancellation of the operation, or u |
| `AaDbpHwmkXWrHeTEYN03` | `src/Taskboard.Integrations/Terminal/PtySession.cs` | 150 | MAJOR | Pass the 'this._pumpCts.Token' to this method to allow cancellation of the operation, or u |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S8949` at all listed locations
- **Description:** Pass the ambient CancellationToken into the flagged method call instead of dropping it.
- **Rules:** Thread the existing cancellationToken parameter through; use CancellationToken.None only when cancellation is genuinely undesired.
- **Input → Output:** code flagged by `csharpsquid:S8949` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S8949` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-pass-cancellation-token`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-pass-cancellation-token`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S8949` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S8949` occurrences resolved (37/37).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
