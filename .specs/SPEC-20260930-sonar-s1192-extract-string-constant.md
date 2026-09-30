# SPEC-20260930-sonar-s1192-extract-string-constant

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S1192` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-extract-string-constant` |
| Ticket | SonarQube rule `csharpsquid:S1192` (55 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 55} |
| Estimated effort | ~220 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S1192` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 55 unresolved `csharpsquid:S1192` issue(s): "Define a constant instead of using this literal 'function' 5 times.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (55 issue(s), rule `csharpsquid:S1192` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs`
- `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs`
- `src/Taskboard.Application/AiChat/AiChatCatalogService.cs`
- `src/Taskboard.Application/AiChat/AiChatService.cs`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs`
- `src/Taskboard.Application/Harness/PipelineEngine.cs`
- `src/Taskboard.Application/Harness/PipelineTemplates.cs`
- `src/Taskboard.Domain.Shared/Mcp/AgentMcpConfigMap.cs`
- `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs`
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs`
- `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs`
- `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs`
- `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs`
- `src/Taskboard.Integrations/Chat/Tools/CodeInterpreterTool.cs`
- `src/Taskboard.Integrations/CliDb/Extractors/AntigravityCliDbExtractor.cs`
- `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs`
- `src/Taskboard.Integrations/Harness/GitWorktreeManager.cs`
- `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs`
- `src/Taskboard.Integrations/Vscode/VscodeInstallService.cs`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Server/Services/AcpClientToolHandler.cs`
- `src/Taskboard.Server/Services/AgentControlService.cs`
- `src/Taskboard.Server/Services/AgentSessionManager.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDukKv_kK9GZAB4jb_F` | `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs` | 96 | MINOR | Define a constant instead of using this literal 'function' 5 times. |
| `AaDukLJhkK9GZAB4jb_U` | `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` | 33 | MINOR | Define a constant instead of using this literal 'https' 4 times. |
| `AaDukLvJkK9GZAB4jb_d` | `src/Taskboard.Integrations/Chat/Tools/CodeInterpreterTool.cs` | 20 | MINOR | Define a constant instead of using this literal '{file}' 4 times. |
| `AaDukKOQkK9GZAB4jb-z` | `src/Taskboard.Server/Program.cs` | 1804 | MINOR | Define a constant instead of using this literal 'VALIDATION' 5 times. |
| `AaDukKOQkK9GZAB4jb-0` | `src/Taskboard.Server/Program.cs` | 1864 | MINOR | Define a constant instead of using this literal 'CONVERSATION_NOT_FOUND' 4 times. |
| `AaDuh0-EnAunJMVSQPzr` | `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` | 67 | MINOR | Define a constant instead of using this literal 'rowid' 6 times. |
| `AaDuh0-EnAunJMVSQPzs` | `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` | 67 | MINOR | Define a constant instead of using this literal 'title' 4 times. |
| `AaDuh0-EnAunJMVSQPzt` | `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` | 67 | MINOR | Define a constant instead of using this literal 'created_at' 4 times. |
| `AaDuh0-EnAunJMVSQPzu` | `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` | 67 | MINOR | Define a constant instead of using this literal 'last_activity_at' 4 times. |
| `AaDuh0-EnAunJMVSQPzv` | `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` | 67 | MINOR | Define a constant instead of using this literal 'model' 4 times. |
| `AaDbpHvEkXWrHeTEYN0N` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 213 | MINOR | Define a constant instead of using this literal 'stage' 6 times. |
| `AaDbpHw6kXWrHeTEYN1J` | `src/Taskboard.Integrations/CliDb/Extractors/AntigravityCliDbExtractor.cs` | 57 | MINOR | Define a constant instead of using this literal 'rowid' 5 times. |
| `AaDbpHt4kXWrHeTEYNz2` | `src/Taskboard.Application/AiChat/AiChatCatalogService.cs` | 77 | MINOR | Define a constant instead of using this literal 'custom' 4 times. |
| `AaDbpHuEkXWrHeTEYNz5` | `src/Taskboard.Application/AiChat/AiChatService.cs` | 253 | MINOR | Define a constant instead of using this literal 'default' 4 times. |
| `AaDbpH01kXWrHeTEYN2c` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 160 | MINOR | Define a constant instead of using this literal 'error' 12 times. |
| `AaDbpH01kXWrHeTEYN2d` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 160 | MINOR | Define a constant instead of using this literal 'system' 21 times. |
| `AaDbpHjukXWrHeTEYNvM` | `src/Taskboard.Server/Program.cs` | 1553 | MINOR | Define a constant instead of using this literal 'Taskboard:WebCliAgent:Enabled' 6 times. |
| `AaDbpHjukXWrHeTEYNvN` | `src/Taskboard.Server/Program.cs` | 1555 | MINOR | Define a constant instead of using this literal 'FEATURE_DISABLED' 6 times. |
| `AaDbpHjukXWrHeTEYNvO` | `src/Taskboard.Server/Program.cs` | 1555 | MINOR | Define a constant instead of using this literal 'Web CLI Agent feature is disabled.' 6 tim |
| `AaDbpHjEkXWrHeTEYNuk` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 214 | MINOR | Define a constant instead of using this literal 'ai_chat.event' 4 times. |
| `AaDbpH3DkXWrHeTEYN3B` | `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` | 125 | MINOR | Define a constant instead of using this literal 'agent' 4 times. |
| `AaDbpH01kXWrHeTEYN2e` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 226 | MINOR | Define a constant instead of using this literal 'session' 4 times. |
| `AaDbpH01kXWrHeTEYN2f` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 466 | MINOR | Define a constant instead of using this literal 'cancelled' 6 times. |
| `AaDbpH01kXWrHeTEYN2g` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 649 | MINOR | Define a constant instead of using this literal 'assistant' 7 times. |
| `AaDbpH26kXWrHeTEYN29` | `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs` | 85 | MINOR | Define a constant instead of using this literal 'session/update' 12 times. |
| `AaDbpH3NkXWrHeTEYN3H` | `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` | 104 | MINOR | Define a constant instead of using this literal 'session/update' 12 times. |
| `AaDbpH3NkXWrHeTEYN3I` | `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` | 123 | MINOR | Define a constant instead of using this literal 'assistant' 4 times. |
| `AaDbpHjPkXWrHeTEYNu9` | `src/Taskboard.Server/Services/AgentControlService.cs` | 96 | MINOR | Define a constant instead of using this literal 'steer' 5 times. |
| `AaDbpHjPkXWrHeTEYNvA` | `src/Taskboard.Server/Services/AgentControlService.cs` | 269 | MINOR | Define a constant instead of using this literal 'stage:' 4 times. |
| `AaDbpHjPkXWrHeTEYNvB` | `src/Taskboard.Server/Services/AgentControlService.cs` | 349 | MINOR | Define a constant instead of using this literal 'running' 4 times. |
| `AaDbpHiYkXWrHeTEYNuZ` | `src/Taskboard.Server/Services/AcpClientToolHandler.cs` | 77 | MINOR | Define a constant instead of using this literal 'terminal/create' 4 times. |
| `AaDbpHiYkXWrHeTEYNua` | `src/Taskboard.Server/Services/AcpClientToolHandler.cs` | 165 | MINOR | Define a constant instead of using this literal 'allow' 6 times. |
| `AaDbpHjPkXWrHeTEYNu_` | `src/Taskboard.Server/Services/AgentControlService.cs` | 174 | MINOR | Define a constant instead of using this literal 'no-active-session' 4 times. |
| `AaDbpHjPkXWrHeTEYNu-` | `src/Taskboard.Server/Services/AgentControlService.cs` | 131 | MINOR | Define a constant instead of using this literal 'no-such-run' 5 times. |
| `AaDbpHuvkXWrHeTEYN0G` | `src/Taskboard.Application/Harness/PipelineTemplates.cs` | 21 | MINOR | Define a constant instead of using this literal 'builder' 8 times. |
| `AaDbpHuvkXWrHeTEYN0H` | `src/Taskboard.Application/Harness/PipelineTemplates.cs` | 21 | MINOR | Define a constant instead of using this literal 'Builder' 4 times. |
| `AaDbpHuvkXWrHeTEYN0I` | `src/Taskboard.Application/Harness/PipelineTemplates.cs` | 23 | MINOR | Define a constant instead of using this literal 'verifier' 5 times. |
| `AaDbpHuvkXWrHeTEYN0J` | `src/Taskboard.Application/Harness/PipelineTemplates.cs` | 23 | MINOR | Define a constant instead of using this literal 'Verifier' 4 times. |
| `AaDbpHjukXWrHeTEYNvL` | `src/Taskboard.Server/Program.cs` | 1201 | MINOR | Define a constant instead of using this literal 'repo must have the 'owner/name' shape.' 4 |
| `AaDbpHuvkXWrHeTEYN0F` | `src/Taskboard.Application/Harness/PipelineTemplates.cs` | 17 | MINOR | Define a constant instead of using this literal 'architect' 4 times. |
| `AaDbpHyfkXWrHeTEYN1z` | `src/Taskboard.Integrations/Harness/GitWorktreeManager.cs` | 277 | MINOR | Define a constant instead of using this literal 'worktree' 4 times. |
| `AaDbpHjukXWrHeTEYNvQ` | `src/Taskboard.Server/Program.cs` | 2339 | MINOR | Define a constant instead of using this literal 'repo-not-found' 4 times. |
| `AaDbpHzUkXWrHeTEYN1-` | `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs` | 178 | MINOR | Define a constant instead of using this literal 'install-sh' 6 times. |
| `AaDbpHpXkXWrHeTEYNyl` | `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` | 26 | MINOR | Define a constant instead of using this literal 'gpt-5.6-luna' 4 times. |
| `AaDbpHpXkXWrHeTEYNym` | `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` | 27 | MINOR | Define a constant instead of using this literal 'gpt-5.1' 4 times. |
| `AaDbpHpXkXWrHeTEYNyh` | `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` | 18 | MINOR | Define a constant instead of using this literal '--model' 7 times. |
| `AaDbpHpXkXWrHeTEYNyi` | `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` | 18 | MINOR | Define a constant instead of using this literal 'haiku' 4 times. |
| `AaDbpHpXkXWrHeTEYNyj` | `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` | 18 | MINOR | Define a constant instead of using this literal 'sonnet' 4 times. |
| `AaDbpHpXkXWrHeTEYNyk` | `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` | 19 | MINOR | Define a constant instead of using this literal 'claude-sonnet-4-5' 4 times. |
| `AaDbpHjukXWrHeTEYNvP` | `src/Taskboard.Server/Program.cs` | 2170 | MINOR | Define a constant instead of using this literal 'issue-not-found' 4 times. |
| `AaDbpHjukXWrHeTEYNvK` | `src/Taskboard.Server/Program.cs` | 632 | MINOR | Define a constant instead of using this literal '/vscode' 4 times. |
| `AaDbpHzvkXWrHeTEYN2J` | `src/Taskboard.Integrations/Vscode/VscodeInstallService.cs` | 77 | MINOR | Define a constant instead of using this literal 'stderr' 4 times. |
| `AaDbpHgzkXWrHeTEYNuR` | `src/Taskboard.Domain.Shared/Mcp/AgentMcpConfigMap.cs` | 69 | MINOR | Define a constant instead of using this literal 'mcpServers' 10 times. |
| `AaDbpH3pkXWrHeTEYN3U` | `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` | 63 | MINOR | Define a constant instead of using this literal 'stderr' 4 times. |
| `AaDbpHjukXWrHeTEYNvJ` | `src/Taskboard.Server/Program.cs` | 187 | MINOR | Define a constant instead of using this literal 'skills' 6 times. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S1192` at all listed locations
- **Description:** Define a named constant for each duplicated string literal.
- **Rules:** Place const at class level (or shared static class when used across classes).
- **Input → Output:** code flagged by `csharpsquid:S1192` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S1192` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-extract-string-constant`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-extract-string-constant`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S1192` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S1192` occurrences resolved (55/55).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
