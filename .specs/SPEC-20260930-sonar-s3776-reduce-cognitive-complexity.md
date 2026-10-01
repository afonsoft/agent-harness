# SPEC-20260930-sonar-s3776-reduce-cognitive-complexity

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S3776` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-reduce-cognitive-complexity` |
| Ticket | SonarQube rule `csharpsquid:S3776` (59 issue(s)) |
| Status | Done |
| Sonar type | CODE_SMELL |
| Severities | {'CRITICAL': 59} |
| Estimated effort | ~354 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S3776` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 59 unresolved `csharpsquid:S3776` issue(s): "Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (59 issue(s), rule `csharpsquid:S3776` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs`
- `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs`
- `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs`
- `src/Taskboard.Application/AiChat/AiChatCatalogService.cs`
- `src/Taskboard.Application/AiChat/AiChatService.cs`
- `src/Taskboard.Application/Chat/ChatService.cs`
- `src/Taskboard.Application/CliMetrics/CliMetricsService.cs`
- `src/Taskboard.Application/Harness/FinOpsService.cs`
- `src/Taskboard.Application/Harness/PipelineEngine.cs`
- `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs`
- `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor`
- `src/Taskboard.Blazor/Components/Chat/ProviderChat.razor`
- `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor`
- `src/Taskboard.Blazor/Components/Cockpit/RunTerminal.razor`
- `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor`
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Blazor/Components/Pages/Terminal.razor`
- `src/Taskboard.Domain.Shared/Agents/AgentCliArgsTemplate.cs`
- `src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs`
- `src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs`
- `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs`
- `src/Taskboard.Integrations/Agents/AcpProtocolParser.cs`
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs`
- `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs`
- `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs`
- `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs`
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs`
- `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs`
- `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs`
- `src/Taskboard.Integrations/Harness/Context/ProjectContextCompiler.cs`
- `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs`
- `src/Taskboard.Integrations/Skills/FrontmatterReader.cs`
- `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs`
- `src/Taskboard.Integrations/Skills/SkillsSyncService.cs`
- `src/Taskboard.Integrations/Specs/MarkdigSpecParser.cs`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Server/Services/AcpClientToolHandler.cs`
- `src/Taskboard.Server/Services/AcpSessionModelCatalog.cs`
- `src/Taskboard.Server/Services/AgentControlService.cs`
- `src/Taskboard.Server/Services/AgentSessionManager.cs`
- `src/Taskboard.Server/Services/ThreadPtyResolver.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDukKv_kK9GZAB4jb_G` | `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs` | 157 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| `AaDukKv_kK9GZAB4jb_H` | `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs` | 246 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 66 to the 15 allowed. |
| `AaDukLOLkK9GZAB4jb_a` | `src/Taskboard.Application/Chat/ChatService.cs` | 270 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 45 to the 15 allowed. |
| `AaDukKmakK9GZAB4jb_A` | `src/Taskboard.Blazor/Components/Chat/ProviderChat.razor` | 319 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 17 to the 15 allowed. |
| `AaDukLvmkK9GZAB4jb_e` | `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs` | 11 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| `AaDukLvmkK9GZAB4jb_g` | `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs` | 60 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed. |
| `AaDukLvmkK9GZAB4jb_h` | `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs` | 81 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| `AaDtEWM7B79WDrchFHf4` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 646 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 33 to the 15 allowed. |
| `AaDtEXYuB79WDrchFHf9` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1451 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 17 to the 15 allowed. |
| `AaDpllQ1TboaTa85QyoB` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 813 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed. |
| `AaDplk9_TboaTa85Qyn9` | `src/Taskboard.Domain.Shared/Agents/AgentCliArgsTemplate.cs` | 46 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed. |
| `AaDpllKNTboaTa85Qyn-` | `src/Taskboard.Server/Services/ThreadPtyResolver.cs` | 33 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed. |
| `AaDbpHvEkXWrHeTEYN0Z` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 105 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 37 to the 15 allowed. |
| `AaDbpHvEkXWrHeTEYN0R` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 241 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 20 to the 15 allowed. |
| `AaDbpHvEkXWrHeTEYN0X` | `src/Taskboard.Application/Harness/PipelineEngine.cs` | 464 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed. |
| `AaDbpHkWkXWrHeTEYNvm` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 196 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| `AaDbpHjukXWrHeTEYNvI` | `src/Taskboard.Server/Program.cs` | ? | CRITICAL | Refactor this top-level file to reduce its Cognitive Complexity from 350 to the 15 allowed |
| `AaDbpHuEkXWrHeTEYNz-` | `src/Taskboard.Application/AiChat/AiChatService.cs` | 438 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 38 to the 15 allowed. |
| `AaDbpHvhkXWrHeTEYN0h` | `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs` | 409 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 31 to the 15 allowed. |
| `AaDbpHvskXWrHeTEYN0l` | `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` | 82 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 20 to the 15 allowed. |
| `AaDbpHk4kXWrHeTEYNwG` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1223 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 26 to the 15 allowed. |
| `AaDbpHvPkXWrHeTEYN0f` | `src/Taskboard.Application/Harness/FinOpsService.cs` | 427 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed. |
| `AaDbpHzKkXWrHeTEYN18` | `src/Taskboard.Integrations/Skills/SkillsSyncService.cs` | 218 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed. |
| `AaDbpHntkXWrHeTEYNyD` | `src/Taskboard.Blazor/Components/Cockpit/RunTerminal.razor` | 94 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| `AaDbpHqlkXWrHeTEYNy5` | `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs` | 289 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 26 to the 15 allowed. |
| `AaDbpHt4kXWrHeTEYNz3` | `src/Taskboard.Application/AiChat/AiChatCatalogService.cs` | 32 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 27 to the 15 allowed. |
| `AaDbpHuEkXWrHeTEYNz7` | `src/Taskboard.Application/AiChat/AiChatService.cs` | 104 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 61 to the 15 allowed. |
| `AaDbpHk4kXWrHeTEYNwB` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1070 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 66 to the 15 allowed. |
| `AaDbpHk4kXWrHeTEYNwH` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1132 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 43 to the 15 allowed. |
| `AaDbpHiEkXWrHeTEYNuX` | `src/Taskboard.Server/Services/AcpSessionModelCatalog.cs` | 32 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed. |
| `AaDbpHjEkXWrHeTEYNuv` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 419 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 31 to the 15 allowed. |
| `AaDbpH3DkXWrHeTEYN3C` | `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` | 145 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 42 to the 15 allowed. |
| `AaDbpH0CkXWrHeTEYN2S` | `src/Taskboard.Integrations/Agents/AcpProtocolParser.cs` | 73 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 20 to the 15 allowed. |
| `AaDbpH01kXWrHeTEYN2v` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 972 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 39 to the 15 allowed. |
| `AaDbpH01kXWrHeTEYN2x` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1119 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed. |
| `AaDbpH26kXWrHeTEYN2_` | `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs` | 142 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 32 to the 15 allowed. |
| `AaDbpH3NkXWrHeTEYN3K` | `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` | 281 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 32 to the 15 allowed. |
| `AaDbpHjPkXWrHeTEYNvC` | `src/Taskboard.Server/Services/AgentControlService.cs` | 62 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 35 to the 15 allowed. |
| `AaDbpHmykXWrHeTEYNxq` | `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` | 147 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| `AaDbpH3DkXWrHeTEYN3D` | `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` | 52 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 49 to the 15 allowed. |
| `AaDbpH01kXWrHeTEYN2y` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 1347 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 21 to the 15 allowed. |
| `AaDbpH0TkXWrHeTEYN2U` | `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` | 29 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| `AaDbpHiYkXWrHeTEYNue` | `src/Taskboard.Server/Services/AcpClientToolHandler.cs` | 184 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 25 to the 15 allowed. |
| `AaDbpHoKkXWrHeTEYNyN` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 514 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| `AaDbpHnjkXWrHeTEYNx-` | `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` | 172 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed. |
| `AaDbpH3YkXWrHeTEYN3N` | `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` | 162 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 32 to the 15 allowed. |
| `AaDbpHj8kXWrHeTEYNvf` | `src/Taskboard.Blazor/Components/Pages/Terminal.razor` | 160 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 17 to the 15 allowed. |
| `AaDbpH4XkXWrHeTEYN3v` | `src/Taskboard.Integrations/Specs/MarkdigSpecParser.cs` | 269 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 34 to the 15 allowed. |
| `AaDbpHs8kXWrHeTEYNzl` | `src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs` | 17 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 21 to the 15 allowed. |
| `AaDbpH5FkXWrHeTEYN31` | `src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs` | 135 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| `AaDbpHxFkXWrHeTEYN1L` | `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` | 156 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 23 to the 15 allowed. |
| `AaDbpHyEkXWrHeTEYN1g` | `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` | 122 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed. |
| `AaDbpHyEkXWrHeTEYN1j` | `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` | 233 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| `AaDbpHyEkXWrHeTEYN1m` | `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` | 336 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 30 to the 15 allowed. |
| `AaDbpHxzkXWrHeTEYN1c` | `src/Taskboard.Integrations/Harness/Context/ProjectContextCompiler.cs` | 47 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 17 to the 15 allowed. |
| `AaDbpHzUkXWrHeTEYN2A` | `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs` | 139 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 25 to the 15 allowed. |
| `AaDbpHphkXWrHeTEYNyo` | `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs` | 88 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 31 to the 15 allowed. |
| `AaDbpHlHkXWrHeTEYNwi` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 795 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| `AaDbpHzBkXWrHeTEYN16` | `src/Taskboard.Integrations/Skills/FrontmatterReader.cs` | 7 | CRITICAL | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S3776` at all listed locations
- **Description:** Refactor each flagged method so its Cognitive Complexity drops to <= 15.
- **Rules:** Extract cohesive helper methods; do not change behavior. Split large switch/if chains.
- **Input → Output:** code flagged by `csharpsquid:S3776` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S3776` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [x] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [x] **T3 — Implementation:** apply the per-rule fix at each location.
- [x] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [x] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-reduce-cognitive-complexity`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-reduce-cognitive-complexity`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S3776` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S3776` occurrences resolved (59/59).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
