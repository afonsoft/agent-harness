# SPEC-20260930-sonar-s3358-extract-nested-ternary

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S3358` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-extract-nested-ternary` |
| Ticket | SonarQube rule `csharpsquid:S3358` (49 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MAJOR': 49} |
| Estimated effort | ~245 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S3358` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 49 unresolved `csharpsquid:S3358` issue(s): "Extract this nested ternary operation into an independent statement.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (49 issue(s), rule `csharpsquid:S3358` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs`
- `src/Taskboard.Application/AiChat/AiChatService.cs`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs`
- `src/Taskboard.Application/Harness/FinOpsAggregator.cs`
- `src/Taskboard.Application/Harness/FinOpsService.cs`
- `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor`
- `src/Taskboard.Blazor/Components/AiChat/ThreadRail.razor`
- `src/Taskboard.Blazor/Components/AiChat/ToolCallCard.razor`
- `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Blazor/Services/RepositoryFilter.cs`
- `src/Taskboard.Domain/Entities/AgentCliDefinition.cs`
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs`
- `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs`
- `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs`
- `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs`
- `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs`
- `src/Taskboard.Integrations/Mcp/JsonConfigMerger.cs`
- `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs`
- `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs`
- `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Server/Services/AgentSessionManager.cs`
- `src/Taskboard.Server/Services/ThreadPtyResolver.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDtM3K0Nw_k69o7k__d` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1356 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDtEW9DB79WDrchFHf6` | `src/Taskboard.Application/AiChat/AiChatService.cs` | 95 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDtDRfHod7YJFS1ySKf` | `src/Taskboard.Server/Services/ThreadPtyResolver.cs` | 53 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDpllQ1TboaTa85QyoC` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 857 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDpllQ1TboaTa85QyoD` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1027 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDpll2OTboaTa85QyoM` | `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` | 131 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDpXQ6ZyeT6D8TPfvBH` | `src/Taskboard.Blazor/Components/AiChat/ThreadRail.razor` | 126 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDpXQ6ZyeT6D8TPfvBI` | `src/Taskboard.Blazor/Components/AiChat/ThreadRail.razor` | 127 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHvPkXWrHeTEYN0d` | `src/Taskboard.Application/Harness/FinOpsService.cs` | 178 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHk4kXWrHeTEYNv7` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 619 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHk4kXWrHeTEYNv8` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 624 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHu3kXWrHeTEYN0K` | `src/Taskboard.Application/Harness/FinOpsAggregator.cs` | 72 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHqlkXWrHeTEYNy4` | `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs` | 39 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHuEkXWrHeTEYNz8` | `src/Taskboard.Application/AiChat/AiChatService.cs` | 256 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHoAkXWrHeTEYNyF` | `src/Taskboard.Blazor/Components/AiChat/ToolCallCard.razor` | 119 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHk4kXWrHeTEYNwc` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 403 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHk4kXWrHeTEYNwC` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1099 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH26kXWrHeTEYN3A` | `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs` | 193 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH3NkXWrHeTEYN3L` | `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` | 288 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH3NkXWrHeTEYN3M` | `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` | 329 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH01kXWrHeTEYN2l` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 515 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH01kXWrHeTEYN2u` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 749 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHoKkXWrHeTEYNyI` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 440 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHoKkXWrHeTEYNyP` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 497 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHoKkXWrHeTEYNyQ` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 500 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHoKkXWrHeTEYNyR` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 504 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHjukXWrHeTEYNvR` | `src/Taskboard.Server/Program.cs` | 1213 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHnjkXWrHeTEYNx_` | `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` | 174 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHnjkXWrHeTEYNyA` | `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` | 175 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHnjkXWrHeTEYNyB` | `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` | 176 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHnjkXWrHeTEYNyC` | `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` | 177 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHjukXWrHeTEYNvS` | `src/Taskboard.Server/Program.cs` | 1223 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHjukXWrHeTEYNvT` | `src/Taskboard.Server/Program.cs` | 1232 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHyEkXWrHeTEYN1k` | `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` | 275 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHyMkXWrHeTEYN1s` | `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` | 89 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHjEkXWrHeTEYNup` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 336 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHk4kXWrHeTEYNwI` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1189 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH37kXWrHeTEYN3b` | `src/Taskboard.Integrations/Mcp/JsonConfigMerger.cs` | 71 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH4NkXWrHeTEYN3n` | `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` | 166 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH4NkXWrHeTEYN3h` | `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` | 271 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH4NkXWrHeTEYN3k` | `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` | 345 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH4DkXWrHeTEYN3d` | `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs` | 65 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH4NkXWrHeTEYN3t` | `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` | 535 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH4NkXWrHeTEYN3q` | `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` | 482 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH4NkXWrHeTEYN3r` | `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` | 483 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpH4DkXWrHeTEYN3e` | `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs` | 84 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHoukXWrHeTEYNyV` | `src/Taskboard.Blazor/Services/RepositoryFilter.cs` | 52 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHunkXWrHeTEYN0C` | `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` | 266 | MAJOR | Extract this nested ternary operation into an independent statement. |
| `AaDbpHzmkXWrHeTEYN2G` | `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` | 209 | MAJOR | Extract this nested ternary operation into an independent statement. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S3358` at all listed locations
- **Description:** Extract each nested ternary into an independent statement.
- **Rules:** Rewrite as if/else or separate assignment preserving semantics.
- **Input → Output:** code flagged by `csharpsquid:S3358` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S3358` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-extract-nested-ternary`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-extract-nested-ternary`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S3358` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S3358` occurrences resolved (49/49).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
