# SPEC-20260930-sonar-s6966-use-awaitable-overload

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S6966` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-use-awaitable-overload` |
| Ticket | SonarQube rule `csharpsquid:S6966` (148 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MAJOR': 148} |
| Estimated effort | ~740 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S6966` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 148 unresolved `csharpsquid:S6966` issue(s): "Await CancelAsync instead.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (148 issue(s), rule `csharpsquid:S6966` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application/Chat/ChatService.cs`
- `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor`
- `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor`
- `src/Taskboard.Blazor/Components/GitHub/AgentSelectionModal.razor`
- `src/Taskboard.Blazor/Components/GitHub/IssueCommentsTab.razor`
- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor`
- `src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor`
- `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor`
- `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor`
- `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor`
- `src/Taskboard.Blazor/Components/Pages/Agents.razor`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Blazor/Components/Pages/Cockpit.razor`
- `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor`
- `src/Taskboard.Blazor/Components/Pages/FinOps.razor`
- `src/Taskboard.Blazor/Components/Pages/Jobs.razor`
- `src/Taskboard.Blazor/Components/Pages/Prompts.razor`
- `src/Taskboard.Blazor/Components/Pages/RunAgentDialog.razor`
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Blazor/Components/Pages/Specs.razor`
- `src/Taskboard.Blazor/Components/Shared/SkillDetailDialog.razor`
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs`
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs`
- `src/Taskboard.Integrations/Agents/StreamingProcessRunner.cs`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Server/Services/AgentSessionManager.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDukLOLkK9GZAB4jb_X` | `src/Taskboard.Application/Chat/ChatService.cs` | 250 | MAJOR | Await CancelAsync instead. |
| `AaDukKWIkK9GZAB4jb-5` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1298 | MAJOR | Await NotifyAsync instead. |
| `AaDukKWIkK9GZAB4jb-7` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1302 | MAJOR | Await NotifyAsync instead. |
| `AaDukKWIkK9GZAB4jb--` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1314 | MAJOR | Await NotifyAsync instead. |
| `AaDukKWIkK9GZAB4jb-_` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1321 | MAJOR | Await NotifyAsync instead. |
| `AaDukKWIkK9GZAB4jb-8` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1335 | MAJOR | Await NotifyAsync instead. |
| `AaDukKWIkK9GZAB4jb-9` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1339 | MAJOR | Await NotifyAsync instead. |
| `AaDukKWIkK9GZAB4jb-2` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1353 | MAJOR | Await NotifyAsync instead. |
| `AaDukKWIkK9GZAB4jb-3` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1358 | MAJOR | Await NotifyAsync instead. |
| `AaDukKWIkK9GZAB4jb-4` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1385 | MAJOR | Await NotifyAsync instead. |
| `AaDukKWIkK9GZAB4jb-6` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1389 | MAJOR | Await NotifyAsync instead. |
| `AaDuhz_snAunJMVSQPzp` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1418 | MAJOR | Await NotifyAsync instead. |
| `AaDuhz_snAunJMVSQPzq` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1422 | MAJOR | Await NotifyAsync instead. |
| `AaDt34v780awsUzQdyoe` | `src/Taskboard.Blazor/Components/Pages/Jobs.razor` | 230 | MAJOR | Await NotifyAsync instead. |
| `AaDt34v780awsUzQdyof` | `src/Taskboard.Blazor/Components/Pages/Jobs.razor` | 234 | MAJOR | Await NotifyAsync instead. |
| `AaDt34v780awsUzQdyog` | `src/Taskboard.Blazor/Components/Pages/Jobs.razor` | 253 | MAJOR | Await NotifyAsync instead. |
| `AaDt34v780awsUzQdyoj` | `src/Taskboard.Blazor/Components/Pages/Jobs.razor` | 258 | MAJOR | Await NotifyAsync instead. |
| `AaDt34v780awsUzQdyoh` | `src/Taskboard.Blazor/Components/Pages/Jobs.razor` | 267 | MAJOR | Await NotifyAsync instead. |
| `AaDt34v780awsUzQdyoi` | `src/Taskboard.Blazor/Components/Pages/Jobs.razor` | 271 | MAJOR | Await NotifyAsync instead. |
| `AaDtM3K0Nw_k69o7k__e` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1374 | MAJOR | Await NotifyAsync instead. |
| `AaDpllTpTboaTa85QyoH` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 415 | MAJOR | Await NotifyAsync instead. |
| `AaDpllToTboaTa85QyoF` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 429 | MAJOR | Await NotifyAsync instead. |
| `AaDpllTpTboaTa85QyoG` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 433 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxT` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 344 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxh` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 653 | MAJOR | Await CancelAsync instead. |
| `AaDbpHkWkXWrHeTEYNv1` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 234 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvy` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 479 | MAJOR | Await CancelAsync instead. |
| `AaDbpHk4kXWrHeTEYNwK` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1261 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwM` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1286 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwP` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1303 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwR` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1314 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwN` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1416 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwb` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1572 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlRkXWrHeTEYNw8` | `src/Taskboard.Blazor/Components/Pages/FinOps.razor` | 423 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwQ` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1421 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwL` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1436 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwO` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1456 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwU` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1476 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwV` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1491 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmykXWrHeTEYNxs` | `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` | 288 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxX` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 532 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxY` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 555 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxZ` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 563 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxf` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 600 | MAJOR | Await NotifyAsync instead. |
| `AaDbpH01kXWrHeTEYN2o` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 632 | MAJOR | Await CancelAsync instead. |
| `AaDbpH01kXWrHeTEYN2q` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 680 | MAJOR | Await CancelAsync instead. |
| `AaDbpHoKkXWrHeTEYNyK` | `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` | 283 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmokXWrHeTEYNxm` | `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` | 49 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmokXWrHeTEYNxo` | `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` | 75 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmokXWrHeTEYNxp` | `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` | 80 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvr` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 334 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvu` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 339 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvq` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 408 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvt` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 422 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnHkXWrHeTEYNxz` | `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` | 440 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxc` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 446 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxW` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 413 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxa` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 431 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxd` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 458 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxb` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 499 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmMkXWrHeTEYNxi` | `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` | 655 | MAJOR | Await CancelAsync instead. |
| `AaDbpHkWkXWrHeTEYNvl` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 187 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNv2` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 275 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvp` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 377 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvn` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 390 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvo` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 394 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvv` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 435 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvx` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 455 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNvz` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 466 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkWkXWrHeTEYNv0` | `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` | 471 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlRkXWrHeTEYNw7` | `src/Taskboard.Blazor/Components/Pages/FinOps.razor` | 411 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlRkXWrHeTEYNw9` | `src/Taskboard.Blazor/Components/Pages/FinOps.razor` | 439 | MAJOR | Await NotifyAsync instead. |
| `AaDbpH3YkXWrHeTEYN3P` | `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` | 208 | MAJOR | Await CancelAsync instead. |
| `AaDbpHlakXWrHeTEYNw-` | `src/Taskboard.Blazor/Components/Pages/Specs.razor` | 230 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlakXWrHeTEYNxA` | `src/Taskboard.Blazor/Components/Pages/Specs.razor` | 233 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlakXWrHeTEYNxB` | `src/Taskboard.Blazor/Components/Pages/Specs.razor` | 242 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlakXWrHeTEYNw_` | `src/Taskboard.Blazor/Components/Pages/Specs.razor` | 263 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHllkXWrHeTEYNxH` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 609 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHllkXWrHeTEYNxI` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 613 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHllkXWrHeTEYNxJ` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 617 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHllkXWrHeTEYNxK` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 624 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwF` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1394 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwS` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1487 | MAJOR | Await NotifyAsync instead. |
| `AaDbpH01kXWrHeTEYN2s` | `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` | 799 | MAJOR | Await CancelAsync instead. |
| `AaDbpHjEkXWrHeTEYNuw` | `src/Taskboard.Server/Services/AgentSessionManager.cs` | 565 | MAJOR | Await CancelAsync instead. |
| `AaDbpHk4kXWrHeTEYNwA` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 822 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwT` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1320 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwY` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1500 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwZ` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1556 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHk4kXWrHeTEYNwa` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 1562 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmBkXWrHeTEYNxR` | `src/Taskboard.Blazor/Components/Pages/RunAgentDialog.razor` | 97 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwp` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1032 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwr` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1061 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwt` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1067 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwv` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1075 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlvkXWrHeTEYNxN` | `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` | 236 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlvkXWrHeTEYNxL` | `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` | 275 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlvkXWrHeTEYNxM` | `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` | 280 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlvkXWrHeTEYNxO` | `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` | 294 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlvkXWrHeTEYNxP` | `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` | 299 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnakXWrHeTEYNx8` | `src/Taskboard.Blazor/Components/GitHub/IssueCommentsTab.razor` | 129 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnakXWrHeTEYNx9` | `src/Taskboard.Blazor/Components/GitHub/IssueCommentsTab.razor` | 134 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmWkXWrHeTEYNxj` | `src/Taskboard.Blazor/Components/Shared/SkillDetailDialog.razor` | 117 | MAJOR | Await NotifyAsync instead. |
| `AaDbpH2ykXWrHeTEYN27` | `src/Taskboard.Integrations/Agents/StreamingProcessRunner.cs` | 65 | MAJOR | Await WaitForExitAsync instead. |
| `AaDbpHmokXWrHeTEYNxl` | `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` | 37 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHllkXWrHeTEYNxC` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 451 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHllkXWrHeTEYNxD` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 465 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHllkXWrHeTEYNxE` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 470 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHllkXWrHeTEYNxF` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 484 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHllkXWrHeTEYNxG` | `src/Taskboard.Blazor/Components/Pages/Agents.razor` | 489 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmykXWrHeTEYNxr` | `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` | 198 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmykXWrHeTEYNxt` | `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` | 294 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnHkXWrHeTEYNxw` | `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` | 367 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnHkXWrHeTEYNxx` | `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` | 373 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnHkXWrHeTEYNxy` | `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` | 386 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnQkXWrHeTEYNx2` | `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` | 221 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnQkXWrHeTEYNx3` | `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` | 226 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnQkXWrHeTEYNx4` | `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` | 246 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnQkXWrHeTEYNx5` | `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` | 251 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnQkXWrHeTEYNx6` | `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` | 266 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnQkXWrHeTEYNx7` | `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` | 271 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwg` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 865 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwh` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 873 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwk` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 887 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwl` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 894 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwm` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 908 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwn` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 916 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwo` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1006 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwq` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1037 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNws` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1045 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwu` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1090 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNww` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1153 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwx` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1167 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNw1` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1188 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNw3` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1194 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNwy` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1205 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNw2` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1209 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNw6` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1504 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmfkXWrHeTEYNxk` | `src/Taskboard.Blazor/Components/GitHub/AgentSelectionModal.razor` | 106 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnHkXWrHeTEYNx0` | `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` | 446 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHnHkXWrHeTEYNx1` | `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` | 454 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHm6kXWrHeTEYNxu` | `src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor` | 92 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHm6kXWrHeTEYNxv` | `src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor` | 97 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHmokXWrHeTEYNxn` | `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` | 54 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHkfkXWrHeTEYNv3` | `src/Taskboard.Blazor/Components/Pages/Prompts.razor` | 92 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNw4` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1466 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHlHkXWrHeTEYNw5` | `src/Taskboard.Blazor/Components/Pages/Settings.razor` | 1499 | MAJOR | Await NotifyAsync instead. |
| `AaDbpHjukXWrHeTEYNvc` | `src/Taskboard.Server/Program.cs` | 2971 | MAJOR | Await RunAsync instead. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S6966` at all listed locations
- **Description:** Call the available async/awaitable overload instead of the synchronous one at each flagged site.
- **Rules:** e.g. await XAsync(...) instead of X(...); keep behavior identical.
- **Input → Output:** code flagged by `csharpsquid:S6966` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S6966` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-use-awaitable-overload`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-use-awaitable-overload`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S6966` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S6966` occurrences resolved (148/148).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
