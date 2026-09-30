# SonarQube Review ToDo Board
Project: `afonsoft_agent-harness` (SonarCloud) — 1811 unresolved issues
Sorted: VULNERABILITY → SECURITY_HOTSPOT → BUG → CODE_SMELL, then severity (Blocker→Info), grouped by file.

## SonarQube Issues Checklist

### `.github/workflows/dotnet.yml` (1 issues)
- [ ] Issue AaDbpH7ZkXWrHeTEYN4a — Sonar type: VULNERABILITY — Severity: MAJOR — Rule: githubactions:S7637 — File: `.github/workflows/dotnet.yml` — Line: 118
      Summary: Use full commit SHA hash for this dependency.

### `src/Taskboard.Blazor/Components/Pages/Settings.razor` (41 issues)
- [ ] Issue AaDbpHlHkXWrHeTEYNwz — Sonar type: VULNERABILITY — Severity: MAJOR — Rule: csharpsquid:S2068 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1453
      Summary: "password" detected here, make sure this is not a hard-coded credential.
- [ ] Issue AaDbpHlHkXWrHeTEYNw0 — Sonar type: VULNERABILITY — Severity: MAJOR — Rule: csharpsquid:S2068 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1471
      Summary: "password" detected here, make sure this is not a hard-coded credential.
- [ ] Issue AaDbpHlHkXWrHeTEYNwf — Sonar type: BUG — Severity: BLOCKER — Rule: csharpsquid:S2930 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 791
      Summary: Dispose '_pollCts' when it is no longer needed.
- [ ] Issue AaDbpHlHkXWrHeTEYNwi — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 795
      Summary: Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed.
- [ ] Issue AaDbpHlHkXWrHeTEYNwj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 849
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHlHkXWrHeTEYNwg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 865
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 873
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 887
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 894
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 908
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 916
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1006
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1032
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1037
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNws — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1045
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1061
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1067
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1075
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1090
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNww — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1153
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1167
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNw1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1188
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNw3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1194
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNwy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1205
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNw2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1209
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1258
      Summary: Remove this unused method parameter 'capability'.
- [ ] Issue AaDukKWIkK9GZAB4jb-5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1298
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1302
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1314
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1321
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1335
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1339
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1353
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1358
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1385
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDukKWIkK9GZAB4jb-6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1389
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDuhz_snAunJMVSQPzp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1418
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDuhz_snAunJMVSQPzq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1422
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNw4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1466
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNw5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1499
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlHkXWrHeTEYNw6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Line: 1504
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` (10 issues)
- [ ] Issue AaDbpHxPkXWrHeTEYN1R — Sonar type: VULNERABILITY — Severity: MAJOR — Rule: csharpsquid:S2077 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 341
      Summary: Use a parameterized query instead of string formatting.
- [ ] Issue AaDbpHxPkXWrHeTEYN1T — Sonar type: VULNERABILITY — Severity: MAJOR — Rule: csharpsquid:S2077 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 362
      Summary: Use a parameterized query instead of string formatting.
- [ ] Issue AaDbpHxPkXWrHeTEYN1X — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 20
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHxPkXWrHeTEYN1U — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 24
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHxPkXWrHeTEYN1W — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 26
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHxPkXWrHeTEYN1V — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 28
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHxPkXWrHeTEYN1P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S2589 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 120
      Summary: Remove this unnecessary check for null.
- [ ] Issue AaDbpHxPkXWrHeTEYN1O — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S6667 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 82
      Summary: Logging in a catch clause should pass the caught exception as a parameter.
- [ ] Issue AaDbpHxPkXWrHeTEYN1Q — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 181
      Summary: Loops should be simplified using the "Where" LINQ method
- [ ] Issue AaDbpHxPkXWrHeTEYN1S — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2325 — File: `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — Line: 312
      Summary: Make 'ValidateRollupColumn' a static method.

### `Dockerfile` (3 issues)
- [ ] Issue AaDbpH-ekXWrHeTEYOJJ — Sonar type: VULNERABILITY — Severity: MINOR — Rule: docker:S6471 — File: `Dockerfile` — Line: 9
      Summary: This image runs with "root" or "containerAdministrator" as the default user. Make sure it is safe he
- [ ] Issue AaDbpH-ekXWrHeTEYOJI — Sonar type: CODE_SMELL — Severity: MINOR — Rule: docker:S7031 — File: `Dockerfile` — Line: 20
      Summary: Merge this RUN instruction with the consecutive ones.
- [ ] Issue AaDbpH-ekXWrHeTEYOJK — Sonar type: CODE_SMELL — Severity: MINOR — Rule: docker:S7020 — File: `Dockerfile` — Line: 21
      Summary: Line is too long. Split it into multiple lines using backslash continuations.

### `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs` (4 issues)
- [ ] Issue AaDbpHphkXWrHeTEYNyq — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs` — Line: 33
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHphkXWrHeTEYNyo — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs` — Line: 88
      Summary: Refactor this method to reduce its Cognitive Complexity from 31 to the 15 allowed.
- [ ] Issue AaDbpHphkXWrHeTEYNyn — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs` — Line: 39
      Summary: Loops should be simplified using the "Where" LINQ method
- [ ] Issue AaDbpHphkXWrHeTEYNyp — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Application.Contracts/Agents/AgentModelListProbe.cs` — Line: 102
      Summary: Loops should be simplified using the "Where" LINQ method

### `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` (4 issues)
- [ ] Issue AaDbpHunkXWrHeTEYN0E — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` — Line: 274
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHunkXWrHeTEYN0D — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` — Line: 331
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHunkXWrHeTEYN0C — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` — Line: 266
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDukLJhkK9GZAB4jb_U — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` — Line: 33
      Summary: Define a constant instead of using this literal 'https' 4 times.

### `src/Taskboard.Blazor/Services/RepositoryFilter.cs` (2 issues)
- [ ] Issue AaDbpHoukXWrHeTEYNyW — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Blazor/Services/RepositoryFilter.cs` — Line: 16
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHoukXWrHeTEYNyV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Services/RepositoryFilter.cs` — Line: 52
      Summary: Extract this nested ternary operation into an independent statement.

### `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` (8 issues)
- [ ] Issue AaDpll2OTboaTa85QyoP — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` — Line: 105
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDpll2OTboaTa85QyoO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` — Line: 57
      Summary: Method has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDpll2OTboaTa85QyoN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` — Line: 82
      Summary: Method has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDpll2OTboaTa85QyoM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` — Line: 131
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDpll2OTboaTa85QyoK — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` — Line: 19
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDpll2OTboaTa85QyoI — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` — Line: 22
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDpll2OTboaTa85QyoJ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` — Line: 31
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDpll2OTboaTa85QyoL — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AgentCliDefinition.cs` — Line: 44
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` (4 issues)
- [ ] Issue AaDbpH3pkXWrHeTEYN3W — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` — Line: 19
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpH3pkXWrHeTEYN3X — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` — Line: 25
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpH3pkXWrHeTEYN3V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` — Line: 131
      Summary: Remove this unused method parameter 'kind'.
- [ ] Issue AaDbpH3pkXWrHeTEYN3U — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` — Line: 63
      Summary: Define a constant instead of using this literal 'stderr' 4 times.

### `src/Taskboard.Integrations/Agents/DockerCliSpawner.cs` (1 issues)
- [ ] Issue AaDplmUoTboaTa85QyoR — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/Agents/DockerCliSpawner.cs` — Line: 25
      Summary: Pass a timeout to limit the execution time.

### `src/Taskboard.Integrations/Execution/ProcessTreeSignaler.cs` (1 issues)
- [ ] Issue AaDbpHy3kXWrHeTEYN15 — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S4036 — File: `src/Taskboard.Integrations/Execution/ProcessTreeSignaler.cs` — Line: 34
      Summary: Use an absolute path for this command.

### `src/Taskboard.Integrations/Skills/SkillsRepository.cs` (2 issues)
- [ ] Issue AaDbpHzdkXWrHeTEYN2D — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/Skills/SkillsRepository.cs` — Line: 37
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHzdkXWrHeTEYN2C — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Integrations/Skills/SkillsRepository.cs` — Line: 147
      Summary: Loops should be simplified using the "Any" LINQ method

### `src/Taskboard.Integrations/Vscode/VscodeInstallService.cs` (3 issues)
- [ ] Issue AaDbpHzvkXWrHeTEYN2K — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/Vscode/VscodeInstallService.cs` — Line: 28
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHzvkXWrHeTEYN2L — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Integrations/Vscode/VscodeInstallService.cs` — Line: 34
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHzvkXWrHeTEYN2J — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Vscode/VscodeInstallService.cs` — Line: 77
      Summary: Define a constant instead of using this literal 'stderr' 4 times.

### `src/Taskboard.Server/Program.cs` (24 issues)
- [ ] Issue AaDbpHjukXWrHeTEYNve — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Server/Program.cs` — Line: 1268
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHjukXWrHeTEYNvd — Sonar type: VULNERABILITY — Severity: MINOR — Rule: csharpsquid:S6444 — File: `src/Taskboard.Server/Program.cs` — Line: 3053
      Summary: Pass a timeout to limit the execution time.
- [ ] Issue AaDbpHjukXWrHeTEYNvI — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Server/Program.cs` — Line: ?
      Summary: Refactor this top-level file to reduce its Cognitive Complexity from 350 to the 15 allowed.
- [ ] Issue AaDbpHjukXWrHeTEYNvR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Server/Program.cs` — Line: 1213
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHjukXWrHeTEYNvS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Server/Program.cs` — Line: 1223
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHjukXWrHeTEYNvT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Server/Program.cs` — Line: 1232
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHjukXWrHeTEYNvY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Server/Program.cs` — Line: 1430
      Summary: Lambda has 9 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHjukXWrHeTEYNvc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Server/Program.cs` — Line: 2971
      Summary: Await RunAsync instead.
- [ ] Issue AaDbpHjukXWrHeTEYNvZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Server/Program.cs` — Line: 3006
      Summary: Local function has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHjukXWrHeTEYNva — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Server/Program.cs` — Line: 3020
      Summary: Local function has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHjukXWrHeTEYNvU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Server/Program.cs` — Line: 3082
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHjukXWrHeTEYNvV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Server/Program.cs` — Line: 3103
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHjukXWrHeTEYNvX — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1905 — File: `src/Taskboard.Server/Program.cs` — Line: 108
      Summary: Remove this unnecessary cast to 'string'.
- [ ] Issue AaDbpHjukXWrHeTEYNvJ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 187
      Summary: Define a constant instead of using this literal 'skills' 6 times.
- [ ] Issue AaDbpHjukXWrHeTEYNvK — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 632
      Summary: Define a constant instead of using this literal '/vscode' 4 times.
- [ ] Issue AaDbpHjukXWrHeTEYNvb — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1075 — File: `src/Taskboard.Server/Program.cs` — Line: 776
      Summary: Refactor your code not to use hardcoded absolute paths or URIs.
- [ ] Issue AaDbpHjukXWrHeTEYNvL — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 1201
      Summary: Define a constant instead of using this literal 'repo must have the 'owner/name' shape.' 4 times.
- [ ] Issue AaDbpHjukXWrHeTEYNvM — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 1553
      Summary: Define a constant instead of using this literal 'Taskboard:WebCliAgent:Enabled' 6 times.
- [ ] Issue AaDbpHjukXWrHeTEYNvN — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 1555
      Summary: Define a constant instead of using this literal 'FEATURE_DISABLED' 6 times.
- [ ] Issue AaDbpHjukXWrHeTEYNvO — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 1555
      Summary: Define a constant instead of using this literal 'Web CLI Agent feature is disabled.' 6 times.
- [ ] Issue AaDukKOQkK9GZAB4jb-z — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 1804
      Summary: Define a constant instead of using this literal 'VALIDATION' 5 times.
- [ ] Issue AaDukKOQkK9GZAB4jb-0 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 1864
      Summary: Define a constant instead of using this literal 'CONVERSATION_NOT_FOUND' 4 times.
- [ ] Issue AaDbpHjukXWrHeTEYNvP — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 2170
      Summary: Define a constant instead of using this literal 'issue-not-found' 4 times.
- [ ] Issue AaDbpHjukXWrHeTEYNvQ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Program.cs` — Line: 2339
      Summary: Define a constant instead of using this literal 'repo-not-found' 4 times.

### `src/Taskboard.Blazor/Components/Pages/AgentInstallDialog.razor` (1 issues)
- [ ] Issue AaDbpHkNkXWrHeTEYNvh — Sonar type: BUG — Severity: BLOCKER — Rule: csharpsquid:S2930 — File: `src/Taskboard.Blazor/Components/Pages/AgentInstallDialog.razor` — Line: 63
      Summary: Dispose '_cts' when it is no longer needed.

### `src/Taskboard.Blazor/Components/Pages/VscodeEditor.razor` (1 issues)
- [ ] Issue AaDbpHl4kXWrHeTEYNxQ — Sonar type: BUG — Severity: BLOCKER — Rule: csharpsquid:S2930 — File: `src/Taskboard.Blazor/Components/Pages/VscodeEditor.razor` — Line: 120
      Summary: Dispose '_cts' when it is no longer needed.

### `src/Taskboard.Integrations/Terminal/PtySession.cs` (13 issues)
- [ ] Issue AaDbpHwmkXWrHeTEYN02 — Sonar type: BUG — Severity: BLOCKER — Rule: csharpsquid:S2930 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 119
      Summary: Dispose '_pumpCts' when it is no longer needed.
- [ ] Issue AaDbpHwmkXWrHeTEYN1C — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 120
      Summary: Pass the 'this._pumpCts.Token' to this method to allow cancellation of the operation, or use 'Cancel
- [ ] Issue AaDbpHwmkXWrHeTEYN03 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 150
      Summary: Pass the 'this._pumpCts.Token' to this method to allow cancellation of the operation, or use 'Cancel
- [ ] Issue AaDbpHwmkXWrHeTEYN05 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 296
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHwmkXWrHeTEYN06 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 310
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHwmkXWrHeTEYN1A — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 330
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHwmkXWrHeTEYN0_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 353
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHwmkXWrHeTEYN1B — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 366
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHwmkXWrHeTEYN07 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 284
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHwmkXWrHeTEYN04 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2486 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 309
      Summary: Handle the exception or explain in a comment why it can be ignored.
- [ ] Issue AaDbpHwmkXWrHeTEYN09 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2486 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 329
      Summary: Handle the exception or explain in a comment why it can be ignored.
- [ ] Issue AaDbpHwmkXWrHeTEYN08 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2486 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 352
      Summary: Handle the exception or explain in a comment why it can be ignored.
- [ ] Issue AaDbpHwmkXWrHeTEYN0- — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2486 — File: `src/Taskboard.Integrations/Terminal/PtySession.cs` — Line: 365
      Summary: Handle the exception or explain in a comment why it can be ignored.

### `docs/architecture/architecture.html` (1036 issues)
- [ ] Issue AaDbpH90kXWrHeTEYN4b — Sonar type: BUG — Severity: MAJOR — Rule: Web:PageWithoutTitleCheck — File: `docs/architecture/architecture.html` — Line: 3
      Summary: Add a <title> tag to this page.
- [ ] Issue AaDbpH90kXWrHeTEYN40 — Sonar type: BUG — Severity: MAJOR — Rule: Web:S5254 — File: `docs/architecture/architecture.html` — Line: 170
      Summary: Add "lang" and/or "xml:lang" attributes to the "<html>" or "<body>" element
- [ ] Issue AaDbpH90kXWrHeTEYN7I — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 6177
      Summary: Refactor this function to reduce its Cognitive Complexity from 16 to the 15 allowed.
- [ ] Issue AaDbpH90kXWrHeTEYN7j — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 6546
      Summary: Refactor this function to reduce its Cognitive Complexity from 16 to the 15 allowed.
- [ ] Issue AaDbpH90kXWrHeTEYN7v — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S2004 — File: `docs/architecture/architecture.html` — Line: 6882
      Summary: Refactor this code to not nest functions more than 5 levels deep.
- [ ] Issue AaDbpH90kXWrHeTEYN7w — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S2004 — File: `docs/architecture/architecture.html` — Line: 6913
      Summary: Refactor this code to not nest functions more than 5 levels deep.
- [ ] Issue AaDbpH90kXWrHeTEYN8Z — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 7395
      Summary: Refactor this function to reduce its Cognitive Complexity from 29 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYN9L — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 7814
      Summary: Refactor this function to reduce its Cognitive Complexity from 16 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYN_X — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 8666
      Summary: Refactor this function to reduce its Cognitive Complexity from 24 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYOAy — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 9646
      Summary: Refactor this function to reduce its Cognitive Complexity from 16 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYOBM — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 9828
      Summary: Refactor this function to reduce its Cognitive Complexity from 18 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYOBW — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3735 — File: `docs/architecture/architecture.html` — Line: 9908
      Summary: Remove this use of the "void" operator.
- [ ] Issue AaDbpH91kXWrHeTEYOBd — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 9942
      Summary: Refactor this function to reduce its Cognitive Complexity from 17 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYOBp — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3735 — File: `docs/architecture/architecture.html` — Line: 10044
      Summary: Remove this use of the "void" operator.
- [ ] Issue AaDbpH91kXWrHeTEYOBz — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 10157
      Summary: Refactor this function to reduce its Cognitive Complexity from 22 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYOC1 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3735 — File: `docs/architecture/architecture.html` — Line: 10490
      Summary: Remove this use of the "void" operator.
- [ ] Issue AaDbpH91kXWrHeTEYOC2 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 10503
      Summary: Refactor this function to reduce its Cognitive Complexity from 23 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYODK — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 11081
      Summary: Refactor this function to reduce its Cognitive Complexity from 17 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYODf — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 11393
      Summary: Refactor this function to reduce its Cognitive Complexity from 33 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYODw — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 11875
      Summary: Refactor this function to reduce its Cognitive Complexity from 18 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYOFB — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 12903
      Summary: Refactor this function to reduce its Cognitive Complexity from 21 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYOGw — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 13842
      Summary: Refactor this function to reduce its Cognitive Complexity from 31 to the 15 allowed.
- [ ] Issue AaDbpH91kXWrHeTEYOIH — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 14488
      Summary: Refactor this function to reduce its Cognitive Complexity from 21 to the 15 allowed.
- [ ] Issue AaDbpH92kXWrHeTEYOIh — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 14906
      Summary: Refactor this function to reduce its Cognitive Complexity from 16 to the 15 allowed.
- [ ] Issue AaDbpH92kXWrHeTEYOIi — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: javascript:S3776 — File: `docs/architecture/architecture.html` — Line: 14970
      Summary: Refactor this function to reduce its Cognitive Complexity from 38 to the 15 allowed.
- [ ] Issue AaDbpH90kXWrHeTEYN41 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 18
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN42 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 21
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN44 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 30
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH92kXWrHeTEYOIj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: css:S4666 — File: `docs/architecture/architecture.html` — Line: 3141
      Summary: Duplicate selector ".diagram-container", first used at line 3135
- [ ] Issue AaDbpH92kXWrHeTEYOIk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: css:S4666 — File: `docs/architecture/architecture.html` — Line: 3276
      Summary: Duplicate selector ".toolbar-chevron", first used at line 3254
- [ ] Issue AaDbpH92kXWrHeTEYOIl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: css:S4666 — File: `docs/architecture/architecture.html` — Line: 3893
      Summary: Duplicate selector ".route-probe-node", first used at line 3886
- [ ] Issue AaDbpH90kXWrHeTEYN4c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5053
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5061
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5067
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4f — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5102
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4g — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S7927 — File: `docs/architecture/architecture.html` — Line: 5123
      Summary: The accessible name should be part of the visible label.
- [ ] Issue AaDbpH90kXWrHeTEYN4h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5370
      Summary: Use <output> instead of the status role to ensure accessibility across all devices.
- [ ] Issue AaDbpH90kXWrHeTEYN4i — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5371
      Summary: Use <output> instead of the status role to ensure accessibility across all devices.
- [ ] Issue AaDbpH90kXWrHeTEYN4j — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5383
      Summary: Use <dialog> instead of the dialog role to ensure accessibility across all devices.
- [ ] Issue AaDbpH90kXWrHeTEYN4k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5392
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5429
      Summary: Use <dialog> instead of the dialog role to ensure accessibility across all devices.
- [ ] Issue AaDbpH90kXWrHeTEYN4m — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5439
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5443
      Summary: Use <section> instead of the region role to ensure accessibility across all devices.
- [ ] Issue AaDbpH90kXWrHeTEYN4p — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5466
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5485
      Summary: Use <section> instead of the region role to ensure accessibility across all devices.
- [ ] Issue AaDbpH90kXWrHeTEYN4r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S7927 — File: `docs/architecture/architecture.html` — Line: 5492
      Summary: The accessible name should be part of the visible label.
- [ ] Issue AaDbpH90kXWrHeTEYN4s — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5500
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5511
      Summary: Use <dialog> instead of the dialog role to ensure accessibility across all devices.
- [ ] Issue AaDbpH90kXWrHeTEYN4u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5520
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4v — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5527
      Summary: Use <section> instead of the region role to ensure accessibility across all devices.
- [ ] Issue AaDbpH90kXWrHeTEYN4w — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S7927 — File: `docs/architecture/architecture.html` — Line: 5534
      Summary: The accessible name should be part of the visible label.
- [ ] Issue AaDbpH90kXWrHeTEYN4x — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6845 — File: `docs/architecture/architecture.html` — Line: 5537
      Summary: "tabindex" should only be declared on interactive elements.
- [ ] Issue AaDbpH90kXWrHeTEYN4y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5537
      Summary: Use <address> or <details> or <fieldset> or <optgroup> instead of the group role to ensure accessibi
- [ ] Issue AaDbpH90kXWrHeTEYN4z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: Web:S6819 — File: `docs/architecture/architecture.html` — Line: 5540
      Summary: Use <output> instead of the status role to ensure accessibility across all devices.
- [ ] Issue AaDbpH90kXWrHeTEYN49 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S6661 — File: `docs/architecture/architecture.html` — Line: 5637
      Summary: Use an object spread instead of `Object.assign` eg: `{ ...foo }`.
- [ ] Issue AaDbpH90kXWrHeTEYN4- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 5660
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH90kXWrHeTEYN5B — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5695
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5C — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5696
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5D — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5700
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5F — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5707
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5H — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5709
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5I — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5710
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5J — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5711
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5K — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5716
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5L — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5725
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5M — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5760
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5825
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5831
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S8786 — File: `docs/architecture/architecture.html` — Line: 5889
      Summary: Simplify this regular expression to reduce its runtime, as it has super-linear performance due to ba
- [ ] Issue AaDbpH90kXWrHeTEYN5S — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S8786 — File: `docs/architecture/architecture.html` — Line: 5890
      Summary: Simplify this regular expression to reduce its runtime, as it has super-linear performance due to ba
- [ ] Issue AaDbpH90kXWrHeTEYN5T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S6535 — File: `docs/architecture/architecture.html` — Line: 5892
      Summary: Unnecessary escape character: \-.
- [ ] Issue AaDbpH90kXWrHeTEYN5U — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S8786 — File: `docs/architecture/architecture.html` — Line: 5894
      Summary: Simplify this regular expression to reduce its runtime, as it has super-linear performance due to ba
- [ ] Issue AaDbpH90kXWrHeTEYN5V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5906
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5W — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5907
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5X — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5908
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5Y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5909
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5Z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5910
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5a — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5911
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5b — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5912
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5913
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5914
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5915
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5f — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5916
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5g — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5917
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5918
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5i — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5919
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5j — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5920
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5921
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5922
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5m — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5923
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5924
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5925
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5p — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5926
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5927
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5928
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5s — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5929
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5934
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5937
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5v — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5958
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5w — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5959
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5x — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5960
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5961
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5962
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN50 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5972
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN51 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5986
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN52 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5989
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN53 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5990
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN54 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5993
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN55 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5994
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN56 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5995
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN57 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 5999
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN58 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6000
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN59 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6003
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6004
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN5_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6005
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6A — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6008
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6B — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6009
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6C — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6010
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6D — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6013
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6E — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6014
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6F — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6015
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6G — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6018
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6H — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6019
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6I — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6020
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6J — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6021
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6K — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6024
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6L — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6025
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6M — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6028
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6N — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6029
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6030
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6031
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6Q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6032
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6033
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6S — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6034
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6038
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6U — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6039
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6040
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6W — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6041
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6X — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6042
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6Y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6045
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6Z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6046
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6a — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6047
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6b — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6050
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6051
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6056
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6057
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6f — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6058
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6g — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6059
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6060
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6i — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6061
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6j — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6062
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6063
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6064
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6m — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6065
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6066
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6067
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6p — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6068
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6069
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6070
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6s — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6071
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6072
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6073
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6v — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6074
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6w — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6075
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6x — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6076
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6077
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6078
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN60 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6107
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN61 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6109
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN63 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6124
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN64 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6138
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN65 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6142
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN66 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6143
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN67 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6144
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN68 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6150
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN69 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6153
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6154
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN6_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6155
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7A — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6156
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7B — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6157
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7C — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6162
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7D — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6163
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7E — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6167
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7F — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6168
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7G — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6169
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7H — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6170
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7J — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6187
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7K — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6189
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7M — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6208
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6224
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6228
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7Q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6229
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6230
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7S — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6236
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6239
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7U — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6240
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6241
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7W — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6246
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7X — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6247
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7Y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6251
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7Z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6252
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7a — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6253
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7b — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S8786 — File: `docs/architecture/architecture.html` — Line: 6319
      Summary: Simplify this regular expression to reduce its runtime, as it has super-linear performance due to ba
- [ ] Issue AaDbpH90kXWrHeTEYN7c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6329
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6330
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6330
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7f — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7762 — File: `docs/architecture/architecture.html` — Line: 6339
      Summary: Prefer `childNode.remove()` over `parentNode.removeChild(childNode)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7g — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6371
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6377
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7i — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 6482
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH90kXWrHeTEYN7k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 6558
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH90kXWrHeTEYN7l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 6572
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH90kXWrHeTEYN7m — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 6580
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH90kXWrHeTEYN7n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6581
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6582
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7p — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 6592
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH90kXWrHeTEYN7r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 6681
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7721 — File: `docs/architecture/architecture.html` — Line: 6734
      Summary: Move function 'authoredStep' to the outer scope.
- [ ] Issue AaDbpH90kXWrHeTEYN7y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 7080
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH90kXWrHeTEYN7z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 7086
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH90kXWrHeTEYN70 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7088
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN71 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7101
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN72 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7102
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN73 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7133
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN74 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7134
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN75 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7157
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN76 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7158
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN77 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7167
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN78 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7168
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN79 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7169
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7171
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN7_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7173
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8A — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7176
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8B — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7178
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8C — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7181
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8D — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7183
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8E — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7186
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8F — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7187
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8G — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7189
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8H — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7190
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8I — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7195
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8J — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7196
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8K — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7197
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8L — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7198
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8M — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7199
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8N — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7200
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7201
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7202
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8Q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7203
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7204
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8S — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7237
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7238
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8U — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7322
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7343
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8W — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7344
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8X — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7359
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN8Y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7360
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8a — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7399
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8b — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7400
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7400
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7400
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 7412
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN8f — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 7415
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN8g — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7445
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7446
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8i — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7452
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8j — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7452
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7453
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7454
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8m — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7454
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7455
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7456
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8p — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7457
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7458
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7459
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8s — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7503
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7506
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7513
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8v — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7514
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8w — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7515
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8x — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7516
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7517
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7533
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN80 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7542
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN82 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7595
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN83 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7607
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN84 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7609
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN85 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7627
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN86 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7628
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN87 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7698
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN88 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7704
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN89 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7705
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7706
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN8_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7706
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9E — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7774
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9F — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7775
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9G — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7777
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9H — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7778
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9I — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7779
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9J — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7782
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9K — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7783
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9M — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7832
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9N — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7833
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7835
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9Q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7837
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7838
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9S — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7839
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7842
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9U — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7843
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7844
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9W — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7845
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9X — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7848
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9Y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7849
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9Z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7850
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9a — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7851
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9b — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7887
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7908
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7911
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7914
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9f — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7923
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9g — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7929
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7933
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9i — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7935
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9j — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7936
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7937
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7938
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9m — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7940
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7941
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7942
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9p — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7958
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 7959
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 8010
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN9s — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 8010
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN9t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 8011
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN9u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8022
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9v — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8023
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9w — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8024
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9x — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8025
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8026
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8035
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN90 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8036
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN91 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8038
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN92 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8039
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN93 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8040
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN94 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 8043
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN95 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 8056
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN96 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8072
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN97 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8077
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN98 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8078
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN99 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8079
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8080
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN9_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8081
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-B — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8154
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-C — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8155
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-D — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8156
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-E — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8157
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-F — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8158
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-G — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8159
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-H — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8182
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-I — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8183
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-J — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8184
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-K — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8185
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-L — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8186
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-M — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8187
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-N — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8188
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8189
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8199
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-Q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8200
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8201
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-S — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8202
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8203
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-U — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8204
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8241
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-W — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8242
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-X — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8275
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-Y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8276
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-Z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8277
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-a — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8278
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-b — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8289
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8293
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8298
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8299
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-f — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8314
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-g — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7768 — File: `docs/architecture/architecture.html` — Line: 8327
      Summary: Prefer `firstNode.before(overlay)` over `svg.insertBefore(overlay, firstNode)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8337
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-i — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8338
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-j — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8342
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8344
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8345
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-m — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8347
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8348
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8349
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-p — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8352
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8355
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8356
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-s — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8366
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8367
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8368
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-v — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8370
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-w — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8371
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-x — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8373
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8373
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8376
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8378
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8379
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8380
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8382
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8383
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8391
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8394
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8395
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8398
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8399
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8400
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN-_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8401
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_A — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8402
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_B — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8415
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_C — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8429
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_D — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8446
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_E — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8452
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_F — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8472
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_G — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8477
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_H — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8487
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_I — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8488
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_J — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8489
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_K — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8490
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_L — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8491
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_M — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7768 — File: `docs/architecture/architecture.html` — Line: 8520
      Summary: Prefer `nodeLayer.before(relationshipHitOverlay)` over `svg.insertBefore(relationshipHitOverlay, nod
- [ ] Issue AaDbpH91kXWrHeTEYN_N — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8560
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8574
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8596
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_Q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8634
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8635
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_S — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8636
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8637
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_U — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8638
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8639
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_W — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 8650
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN_Y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8681
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_Z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8682
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_a — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8762
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_b — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8764
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8765
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8768
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8797
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_f — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8822
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8843
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_i — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8844
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_j — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8847
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8849
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8849
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_m — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8853
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8854
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8856
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_p — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8860
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S8786 — File: `docs/architecture/architecture.html` — Line: 8902
      Summary: Simplify this regular expression to reduce its runtime, as it has super-linear performance due to ba
- [ ] Issue AaDbpH91kXWrHeTEYN_r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 8904
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN_s — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 8911
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN_t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 8912
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN_u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8921
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_v — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8924
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_x — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8936
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8947
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8948
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8949
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 8959
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9014
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9044
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 9084
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYN_7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9111
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9121
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9122
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN_- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9124
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYN__ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9125
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9126
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9144
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9147
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9148
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9150
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9164
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9165
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9166
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9167
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9168
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9169
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9170
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9171
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9173
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9188
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9194
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9202
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9203
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 9205
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOAT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9206
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9207
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9214
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAW — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9224
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9225
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9226
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7768 — File: `docs/architecture/architecture.html` — Line: 9232
      Summary: Prefer `firstNode.before(overlay)` over `svg.insertBefore(overlay, firstNode)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9236
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9253
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9262
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAd — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9282
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9369
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9382
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9384
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9385
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9394
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9426
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9437
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9439
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9440
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9442
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9443
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9444
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9455
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9469
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9470
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9472
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9547
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9564
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9606
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOAx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9632
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 9650
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOAz — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 9650
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOA1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9651
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9657
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9681
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9695
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9702
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9704
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9704
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9705
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9705
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9706
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOA_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9706
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9707
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9711
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9713
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9715
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9746
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9754
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9755
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9757
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9758
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9807
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9812
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9813
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9862
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9863
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9865
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9866
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9869
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9873
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 9874
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOBU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 9874
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOBV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9878
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9914
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9919
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9920
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9921
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9922
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9950
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9951
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 9961
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOBh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 9962
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOBi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 9963
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOBj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9974
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9977
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 9978
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10028
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10034
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 10041
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOBq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S4144 — File: `docs/architecture/architecture.html` — Line: 10058
      Summary: Update this function so that its implementation is not identical to the one on line 8884.
- [ ] Issue AaDbpH91kXWrHeTEYOBr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10075
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10083
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10093
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10127
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10128
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10129
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOBy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10145
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10168
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10180
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10183
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10184
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7768 — File: `docs/architecture/architecture.html` — Line: 10189
      Summary: Prefer `firstNode.before(carrierOverlay)` over `svg.insertBefore(carrierOverlay, firstNode)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10226
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10227
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10228
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10253
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10254
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10258
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOB_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10259
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10260
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10266
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10271
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10272
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10275
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10287
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10288
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10290
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10291
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10293
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10294
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10303
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10304
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10307
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10308
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10311
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10339
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10340
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10341
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10342
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10344
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10347
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCW — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10366
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10367
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10368
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10369
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10370
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10371
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10376
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCd — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10377
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10381
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10382
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10404
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10411
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10418
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10419
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10420
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10421
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10422
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10432
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10436
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10442
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10453
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10454
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10455
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10456
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10457
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10458
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10460
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10477
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10477
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOC0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10478
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOCz — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10478
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOC3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10505
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOC4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 10511
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOC5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 10513
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOC6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 10518
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOC7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 10520
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOC8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 10526
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOC9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 10539
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOC- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10545
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOC_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10555
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10606
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10630
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10648
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10813
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10820
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10825
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10877
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10949
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 10960
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11102
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11103
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11122
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11123
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11129
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11130
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11150
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11164
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11166
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11168
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODW — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11205
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11206
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11245
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11335
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11341
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11352
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11353
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODd — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11356
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11357
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11611
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 11641
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYODi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11657
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11658
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11659
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11660
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11703
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11733
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11780
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11828
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11859
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11860
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11869
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYODx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S6666 — File: `docs/architecture/architecture.html` — Line: 11884
      Summary: Use the spread operator instead of '.apply()'.
- [ ] Issue AaDbpH91kXWrHeTEYODy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S6666 — File: `docs/architecture/architecture.html` — Line: 11885
      Summary: Use the spread operator instead of '.apply()'.
- [ ] Issue AaDbpH91kXWrHeTEYODz — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S6666 — File: `docs/architecture/architecture.html` — Line: 11886
      Summary: Use the spread operator instead of '.apply()'.
- [ ] Issue AaDbpH91kXWrHeTEYOD0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S6666 — File: `docs/architecture/architecture.html` — Line: 11887
      Summary: Use the spread operator instead of '.apply()'.
- [ ] Issue AaDbpH91kXWrHeTEYOD1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11912
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOD2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 11939
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOD4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11949
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOD5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 11984
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOD6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S6666 — File: `docs/architecture/architecture.html` — Line: 11992
      Summary: Use the spread operator instead of '.apply()'.
- [ ] Issue AaDbpH91kXWrHeTEYOD7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S6666 — File: `docs/architecture/architecture.html` — Line: 11993
      Summary: Use the spread operator instead of '.apply()'.
- [ ] Issue AaDbpH91kXWrHeTEYOD8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 12002
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOD9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12007
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOD- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12026
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOD_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12037
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12038
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12137
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12149
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOED — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12157
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12158
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12158
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 12281
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOEH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12299
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12300
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12312
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12313
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12314
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12315
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12316
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12317
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12318
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12329
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOER — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12330
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOES — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12331
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOET — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12332
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12348
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12349
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEW — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12356
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12371
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12378
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12385
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12387
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12403
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12406
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEd — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12423
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12427
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12438
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12439
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12448
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12463
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 12466
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOEk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12471
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12472
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12473
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12474
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12509
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12564
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12583
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12598
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12599
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12608
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12609
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12635
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12648
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12660
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOEy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12729
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12757
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12760
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12826
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12836
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12837
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12848
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12849
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12851
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12852
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12853
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOE- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12854
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12896
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12938
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 12941
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOFF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 12946
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOFG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 12976
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13106
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13110
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13122
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13129
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13130
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13134
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13138
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13139
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13140
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13142
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13143
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13152
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13153
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13154
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13155
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFW — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13156
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13197
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13198
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13200
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13201
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13204
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13205
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFd — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13208
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13213
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13224
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13225
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13228
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13229
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13230
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13231
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13232
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13236
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13237
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13240
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13269
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13270
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13281
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13282
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13303
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13304
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13325
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOFw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13326
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOF3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13402
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOF4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13403
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOF5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13404
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOF6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13405
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOF7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13406
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOF8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13407
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOF9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13408
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOF- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13409
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOF_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13420
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7768 — File: `docs/architecture/architecture.html` — Line: 13429
      Summary: Prefer `firstNode.before(overlay)` over `svg.insertBefore(overlay, firstNode)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13436
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13468
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13502
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13503
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13504
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13505
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13506
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13507
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13508
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13509
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13510
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13511
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13521
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7768 — File: `docs/architecture/architecture.html` — Line: 13529
      Summary: Prefer `firstNode.before(overlay)` over `svg.insertBefore(overlay, firstNode)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13553
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13554
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 13558
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOGS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13559
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13560
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13561
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 13565
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOGW — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13566
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13567
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13568
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 13572
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOGa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13573
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 13581
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOGc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S4144 — File: `docs/architecture/architecture.html` — Line: 13696
      Summary: Update this function so that its implementation is not identical to the one on line 11283.
- [ ] Issue AaDbpH91kXWrHeTEYOGd — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13703
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 13728
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOGf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13738
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13739
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13754
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13755
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13757
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13759
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13760
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13785
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13786
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13788
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13789
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13792
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13793
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13796
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13799
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13825
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13832
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13844
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13863
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOGz — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13864
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOG0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13893
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOG1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13930
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOG2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13934
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOG3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S4144 — File: `docs/architecture/architecture.html` — Line: 13943
      Summary: Update this function so that its implementation is not identical to the one on line 8884.
- [ ] Issue AaDbpH91kXWrHeTEYOG4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S8786 — File: `docs/architecture/architecture.html` — Line: 13958
      Summary: Simplify this regular expression to reduce its runtime, as it has super-linear performance due to ba
- [ ] Issue AaDbpH91kXWrHeTEYOG5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13974
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOG6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 13980
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOG8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14030
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOG9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14038
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOG- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14123
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOG_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14133
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14144
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14145
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14146
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14146
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S4144 — File: `docs/architecture/architecture.html` — Line: 14152
      Summary: Update this function so that its implementation is not identical to the one on line 8063.
- [ ] Issue AaDbpH91kXWrHeTEYOHF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14160
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14180
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14209
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14213
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14216
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14218
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14219
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14223
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14224
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14232
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14237
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14257
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14260
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14261
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14266
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14268
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHW — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14269
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14270
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14274
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14275
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14275
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14276
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14276
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHd — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14277
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14277
      Summary: Prefer `.dataset` over `hasAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14282
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14285
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14291
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14292
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14296
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14299
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14300
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14303
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14316
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14323
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14324
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14337
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14338
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14339
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14340
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14341
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14342
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14343
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14345
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14352
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOHz — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14353
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOH0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14355
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOH1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14360
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOH2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7768 — File: `docs/architecture/architecture.html` — Line: 14374
      Summary: Prefer `firstNode.before(overlay)` over `svg.insertBefore(overlay, firstNode)`.
- [ ] Issue AaDbpH91kXWrHeTEYOH3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14380
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOH4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14382
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOH5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14383
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOH6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14384
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOH7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14393
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOH_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S4144 — File: `docs/architecture/architecture.html` — Line: 14425
      Summary: Update this function so that its implementation is not identical to the one on line 11283.
- [ ] Issue AaDbpH91kXWrHeTEYOIA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14431
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14437
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOID — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14457
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14465
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14479
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14480
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOII — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S1854 — File: `docs/architecture/architecture.html` — Line: 14500
      Summary: Remove this useless assignment to variable "direction".
- [ ] Issue AaDbpH91kXWrHeTEYOIJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S3358 — File: `docs/architecture/architecture.html` — Line: 14505
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH91kXWrHeTEYOIK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14508
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14512
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14513
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14518
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14580
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14603
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S4144 — File: `docs/architecture/architecture.html` — Line: 14614
      Summary: Update this function so that its implementation is not identical to the one on line 8884.
- [ ] Issue AaDbpH91kXWrHeTEYOIS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S8786 — File: `docs/architecture/architecture.html` — Line: 14629
      Summary: Simplify this regular expression to reduce its runtime, as it has super-linear performance due to ba
- [ ] Issue AaDbpH91kXWrHeTEYOIV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14720
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14794
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14795
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14796
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14797
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14824
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14825
      Summary: Prefer `.dataset` over `removeAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOId — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14833
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH91kXWrHeTEYOIe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14847
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH92kXWrHeTEYOIf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14848
      Summary: Prefer `.dataset` over `setAttribute(…)`.
- [ ] Issue AaDbpH92kXWrHeTEYOIg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: javascript:S7761 — File: `docs/architecture/architecture.html` — Line: 14904
      Summary: Prefer `.dataset` over `getAttribute(…)`.
- [ ] Issue AaDbpH90kXWrHeTEYN43 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 23
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH90kXWrHeTEYN45 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 31
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH90kXWrHeTEYN4o — Sonar type: CODE_SMELL — Severity: MINOR — Rule: Web:S6827 — File: `docs/architecture/architecture.html` — Line: 5459
      Summary: Anchors must have content and the content must be accessible by a screen reader.
- [ ] Issue AaDbpH90kXWrHeTEYN46 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 5629
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH90kXWrHeTEYN47 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6353 — File: `docs/architecture/architecture.html` — Line: 5630
      Summary: Use concise character class syntax '\w' instead of '[a-zA-Z0-9_]'.
- [ ] Issue AaDbpH90kXWrHeTEYN48 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 5631
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH90kXWrHeTEYN4_ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 5667
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH90kXWrHeTEYN5A — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 5695
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH90kXWrHeTEYN5E — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 5701
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH90kXWrHeTEYN5G — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 5708
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH90kXWrHeTEYN5N — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 5812
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH90kXWrHeTEYN5Q — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 5849
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH90kXWrHeTEYN62 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S4138 — File: `docs/architecture/architecture.html` — Line: 6120
      Summary: Expected a `for-of` loop instead of a `for` loop with this simple iteration.
- [ ] Issue AaDbpH90kXWrHeTEYN7L — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S4138 — File: `docs/architecture/architecture.html` — Line: 6201
      Summary: Expected a `for-of` loop instead of a `for` loop with this simple iteration.
- [ ] Issue AaDbpH90kXWrHeTEYN7N — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S4138 — File: `docs/architecture/architecture.html` — Line: 6217
      Summary: Expected a `for-of` loop instead of a `for` loop with this simple iteration.
- [ ] Issue AaDbpH90kXWrHeTEYN7q — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S4138 — File: `docs/architecture/architecture.html` — Line: 6673
      Summary: Expected a `for-of` loop instead of a `for` loop with this simple iteration.
- [ ] Issue AaDbpH90kXWrHeTEYN7s — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 6716
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH90kXWrHeTEYN7u — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7773 — File: `docs/architecture/architecture.html` — Line: 6745
      Summary: Prefer `Number.parseFloat` over `parseFloat`.
- [ ] Issue AaDbpH90kXWrHeTEYN7x — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 6971
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDuh1yhnAunJMVSQPzw — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S9381 — File: `docs/architecture/architecture.html` — Line: 7229
      Summary: Avoid nesting promises.
- [ ] Issue AaDbpH91kXWrHeTEYN81 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 7587
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH91kXWrHeTEYN9A — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S4138 — File: `docs/architecture/architecture.html` — Line: 7733
      Summary: Expected a `for-of` loop instead of a `for` loop with this simple iteration.
- [ ] Issue AaDbpH91kXWrHeTEYN9B — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 7739
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYN9C — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 7748
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYN9D — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 7749
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYN9P — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 7836
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYN-A — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 8089
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYN_g — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 8825
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYN_w — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 8926
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYN_y — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 8938
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYN_5 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 9054
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH91kXWrHeTEYOBc — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 9931
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYOBu — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 10122
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH91kXWrHeTEYODH — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 10924
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH91kXWrHeTEYODN — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7773 — File: `docs/architecture/architecture.html` — Line: 11107
      Summary: Prefer `Number.parseFloat` over `parseFloat`.
- [ ] Issue AaDbpH91kXWrHeTEYODn — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7778 — File: `docs/architecture/architecture.html` — Line: 11732
      Summary: Do not call `Element#classList.remove()` multiple times.
- [ ] Issue AaDbpH91kXWrHeTEYODp — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 11752
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYODq — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7778 — File: `docs/architecture/architecture.html` — Line: 11779
      Summary: Do not call `Element#classList.remove()` multiple times.
- [ ] Issue AaDbpH91kXWrHeTEYOD3 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7778 — File: `docs/architecture/architecture.html` — Line: 11948
      Summary: Do not call `Element#classList.add()` multiple times.
- [ ] Issue AaDbpH91kXWrHeTEYOEz — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 12737
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH91kXWrHeTEYOE_ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 12892
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYOFC — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 12932
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYOFx — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S4138 — File: `docs/architecture/architecture.html` — Line: 13338
      Summary: Expected a `for-of` loop instead of a `for` loop with this simple iteration.
- [ ] Issue AaDbpH91kXWrHeTEYOFy — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S4138 — File: `docs/architecture/architecture.html` — Line: 13353
      Summary: Expected a `for-of` loop instead of a `for` loop with this simple iteration.
- [ ] Issue AaDbpH91kXWrHeTEYOFz — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 13356
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYOF0 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 13369
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYOF1 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 13372
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYOF2 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `docs/architecture/architecture.html` — Line: 13378
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.
- [ ] Issue AaDbpH91kXWrHeTEYOG7 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 13996
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH91kXWrHeTEYOHQ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 14257
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYOH8 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 14394
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYOH9 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 14396
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYOH- — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 14416
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH91kXWrHeTEYOIB — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 14437
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYOIO — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7750 — File: `docs/architecture/architecture.html` — Line: 14529
      Summary: Prefer `.find(…)` over `.filter(…)[0]`.
- [ ] Issue AaDbpH91kXWrHeTEYOIT — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 14649
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.
- [ ] Issue AaDbpH91kXWrHeTEYOIU — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `docs/architecture/architecture.html` — Line: 14655
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpH91kXWrHeTEYOIW — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `docs/architecture/architecture.html` — Line: 14740
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.

### `src/Taskboard.Application/AiChat/AiChatService.cs` (10 issues)
- [ ] Issue AaDbpHuEkXWrHeTEYNz9 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 428
      Summary: Pass the 'ct' to this method to allow cancellation of the operation, or use 'CancellationToken.None'
- [ ] Issue AaDbpHuEkXWrHeTEYNz7 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 104
      Summary: Refactor this method to reduce its Cognitive Complexity from 61 to the 15 allowed.
- [ ] Issue AaDbpHuEkXWrHeTEYNz- — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 438
      Summary: Refactor this method to reduce its Cognitive Complexity from 38 to the 15 allowed.
- [ ] Issue AaDbpHuEkXWrHeTEYNz6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 39
      Summary: Constructor has 16 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDtEW9DB79WDrchFHf6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 95
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDtQslqyFJDtl23sZEl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S125 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 219
      Summary: Remove this commented out code.
- [ ] Issue AaDbpHuEkXWrHeTEYNz8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 256
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDtEW9DB79WDrchFHf7 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 184
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDtEW9DB79WDrchFHf8 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 199
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHuEkXWrHeTEYNz5 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application/AiChat/AiChatService.cs` — Line: 253
      Summary: Define a constant instead of using this literal 'default' 4 times.

### `src/Taskboard.Application/Chat/ChatService.cs` (8 issues)
- [ ] Issue AaDukLOLkK9GZAB4jb_Y — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Application/Chat/ChatService.cs` — Line: 241
      Summary: Pass the 'requestAborted' to this method to allow cancellation of the operation, or use 'Cancellatio
- [ ] Issue AaDukLOLkK9GZAB4jb_Z — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Application/Chat/ChatService.cs` — Line: 243
      Summary: Pass the 'requestAborted' to this method to allow cancellation of the operation, or use 'Cancellatio
- [ ] Issue AaDukLOLkK9GZAB4jb_a — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/Chat/ChatService.cs` — Line: 270
      Summary: Refactor this method to reduce its Cognitive Complexity from 45 to the 15 allowed.
- [ ] Issue AaDukLOLkK9GZAB4jb_V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Application/Chat/ChatService.cs` — Line: 33
      Summary: Constructor has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDukLOLkK9GZAB4jb_X — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Application/Chat/ChatService.cs` — Line: 250
      Summary: Await CancelAsync instead.
- [ ] Issue AaDukLOLkK9GZAB4jb_c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Application/Chat/ChatService.cs` — Line: 276
      Summary: Remove this unused method parameter 'enumeratorCancelled'.
- [ ] Issue AaDukLOLkK9GZAB4jb_b — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Application/Chat/ChatService.cs` — Line: 447
      Summary: Remove this unused method parameter 'provider'.
- [ ] Issue AaDukLOLkK9GZAB4jb_W — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2737 — File: `src/Taskboard.Application/Chat/ChatService.cs` — Line: 120
      Summary: Add logic to this catch clause or eliminate it and rethrow the exception automatically.

### `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` (4 issues)
- [ ] Issue AaDbpHvskXWrHeTEYN0i — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S2259 — File: `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` — Line: 143
      Summary: 'state' is null on at least one execution path.
- [ ] Issue AaDbpHvskXWrHeTEYN0l — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` — Line: 82
      Summary: Refactor this method to reduce its Cognitive Complexity from 20 to the 15 allowed.
- [ ] Issue AaDbpHvskXWrHeTEYN0j — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S6667 — File: `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` — Line: 53
      Summary: Logging in a catch clause should pass the caught exception as a parameter.
- [ ] Issue AaDbpHvskXWrHeTEYN0k — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S6667 — File: `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` — Line: 58
      Summary: Logging in a catch clause should pass the caught exception as a parameter.

### `src/Taskboard.Application/Harness/PipelineEngine.cs` (15 issues)
- [ ] Issue AaDbpHvEkXWrHeTEYN0Y — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 481
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpHvEkXWrHeTEYN0Z — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 105
      Summary: Refactor this method to reduce its Cognitive Complexity from 37 to the 15 allowed.
- [ ] Issue AaDbpHvEkXWrHeTEYN0R — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 241
      Summary: Refactor this method to reduce its Cognitive Complexity from 20 to the 15 allowed.
- [ ] Issue AaDbpHvEkXWrHeTEYN0X — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 464
      Summary: Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed.
- [ ] Issue AaDbpHvEkXWrHeTEYN0O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 38
      Summary: Constructor has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHvEkXWrHeTEYN0P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 100
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHvEkXWrHeTEYN0T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 244
      Summary: Remove this unused method parameter 'cancellationToken'.
- [ ] Issue AaDbpHvEkXWrHeTEYN0Q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 442
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHvEkXWrHeTEYN0S — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 696
      Summary: Method has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHvEkXWrHeTEYN0N — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 213
      Summary: Define a constant instead of using this literal 'stage' 6 times.
- [ ] Issue AaDbpHvEkXWrHeTEYN0a — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 217
      Summary: Loop should be simplified by calling Select(stage => stage.StageKey)
- [ ] Issue AaDbpHvEkXWrHeTEYN0b — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 225
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHvEkXWrHeTEYN0U — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 277
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHvEkXWrHeTEYN0V — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 288
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHvEkXWrHeTEYN0W — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/Harness/PipelineEngine.cs` — Line: 796
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Client/wwwroot/js/taskboard.js` (5 issues)
- [ ] Issue AaDbpHwBkXWrHeTEYN0q — Sonar type: BUG — Severity: MAJOR — Rule: javascript:S4822 — File: `src/Taskboard.Client/wwwroot/js/taskboard.js` — Line: 129
      Summary: Consider using 'await' for the promises inside this 'try' or replace it with 'Promise.prototype.catc
- [ ] Issue AaDbpHwBkXWrHeTEYN0p — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6582 — File: `src/Taskboard.Client/wwwroot/js/taskboard.js` — Line: 4
      Summary: Prefer using an optional chain expression instead, as it's more concise and easier to read.
- [ ] Issue AaDbpHwBkXWrHeTEYN0r — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `src/Taskboard.Client/wwwroot/js/taskboard.js` — Line: 137
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpHwBkXWrHeTEYN0s — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S2486 — File: `src/Taskboard.Client/wwwroot/js/taskboard.js` — Line: 159
      Summary: Handle this exception, don't catch it at all, or explain in a comment why it is ignored.
- [ ] Issue AaDbpHwBkXWrHeTEYN0t — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6653 — File: `src/Taskboard.Client/wwwroot/js/taskboard.js` — Line: 196
      Summary: Use 'Object.hasOwn()' instead of 'Object.prototype.hasOwnProperty.call()'.

### `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` (32 issues)
- [ ] Issue AaDbpH01kXWrHeTEYN2k — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 145
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpH01kXWrHeTEYN2n — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 401
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpH01kXWrHeTEYN2t — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 803
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpH01kXWrHeTEYN23 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1426
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpH01kXWrHeTEYN26 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1557
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpH01kXWrHeTEYN2v — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 972
      Summary: Refactor this method to reduce its Cognitive Complexity from 39 to the 15 allowed.
- [ ] Issue AaDbpH01kXWrHeTEYN2x — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1119
      Summary: Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed.
- [ ] Issue AaDbpH01kXWrHeTEYN2y — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1347
      Summary: Refactor this method to reduce its Cognitive Complexity from 21 to the 15 allowed.
- [ ] Issue AaDtEXYuB79WDrchFHf9 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1451
      Summary: Refactor this method to reduce its Cognitive Complexity from 17 to the 15 allowed.
- [ ] Issue AaDbpH01kXWrHeTEYN2h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 437
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpH01kXWrHeTEYN2l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 515
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH01kXWrHeTEYN2m — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 525
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpH01kXWrHeTEYN2o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 632
      Summary: Await CancelAsync instead.
- [ ] Issue AaDbpH01kXWrHeTEYN2q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 680
      Summary: Await CancelAsync instead.
- [ ] Issue AaDbpH01kXWrHeTEYN2u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 749
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH01kXWrHeTEYN2s — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 799
      Summary: Await CancelAsync instead.
- [ ] Issue AaDbpH01kXWrHeTEYN2r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 835
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpH01kXWrHeTEYN2w — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1115
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpH01kXWrHeTEYN24 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1290
      Summary: Remove this unused method parameter 'payloadJson'.
- [ ] Issue AaDbpH01kXWrHeTEYN21 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1310
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpH01kXWrHeTEYN22 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1333
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpH01kXWrHeTEYN20 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1347
      Summary: Remove this unused method parameter 'payloadJson'.
- [ ] Issue AaDbpH01kXWrHeTEYN2z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1381
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpH01kXWrHeTEYN25 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 1633
      Summary: Method has 10 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpH01kXWrHeTEYN2j — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 153
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH01kXWrHeTEYN2c — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 160
      Summary: Define a constant instead of using this literal 'error' 12 times.
- [ ] Issue AaDbpH01kXWrHeTEYN2d — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 160
      Summary: Define a constant instead of using this literal 'system' 21 times.
- [ ] Issue AaDbpH01kXWrHeTEYN2e — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 226
      Summary: Define a constant instead of using this literal 'session' 4 times.
- [ ] Issue AaDbpH01kXWrHeTEYN2i — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S6608 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 282
      Summary: Indexing at 0 should be used instead of the "Enumerable" extension method "First"
- [ ] Issue AaDbpH01kXWrHeTEYN2f — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 466
      Summary: Define a constant instead of using this literal 'cancelled' 6 times.
- [ ] Issue AaDbpH01kXWrHeTEYN2p — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1481 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 628
      Summary: Remove the unused local variable 'pending'.
- [ ] Issue AaDbpH01kXWrHeTEYN2g — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — Line: 649
      Summary: Define a constant instead of using this literal 'assistant' 7 times.

### `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` (5 issues)
- [ ] Issue AaDbpH0TkXWrHeTEYN2V — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` — Line: 57
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpH0TkXWrHeTEYN2W — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` — Line: 114
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpH0TkXWrHeTEYN2X — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` — Line: 132
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpH0TkXWrHeTEYN2Y — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` — Line: 137
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpH0TkXWrHeTEYN2U — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpSessionRunClient.cs` — Line: 29
      Summary: Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed.

### `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` (5 issues)
- [ ] Issue AaDbpH3YkXWrHeTEYN3Q — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` — Line: 213
      Summary: Pass the 'stoppingToken' to this method to allow cancellation of the operation, or use 'Cancellation
- [ ] Issue AaDbpH3YkXWrHeTEYN3N — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` — Line: 162
      Summary: Refactor this method to reduce its Cognitive Complexity from 32 to the 15 allowed.
- [ ] Issue AaDbpH3YkXWrHeTEYN3P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` — Line: 208
      Summary: Await CancelAsync instead.
- [ ] Issue AaDbpH3YkXWrHeTEYN3O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` — Line: 441
      Summary: Remove this unused method parameter 'cancellationToken'.
- [ ] Issue AaDbpH3YkXWrHeTEYN3R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` — Line: 539
      Summary: Remove this unused method parameter 'cancellationToken'.

### `src/Taskboard.Integrations/Execution/ProcessCommandRunner.cs` (2 issues)
- [ ] Issue AaDbpHyokXWrHeTEYN11 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Execution/ProcessCommandRunner.cs` — Line: 37
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpHyokXWrHeTEYN12 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Execution/ProcessCommandRunner.cs` — Line: 38
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella

### `src/Taskboard.Integrations/Harness/GitCommandRunner.cs` (2 issues)
- [ ] Issue AaDbpHyVkXWrHeTEYN1x — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Harness/GitCommandRunner.cs` — Line: 44
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpHyVkXWrHeTEYN1y — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Harness/GitCommandRunner.cs` — Line: 45
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella

### `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` (13 issues)
- [ ] Issue AaDbpHyEkXWrHeTEYN1i — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S2583 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 273
      Summary: Change this condition so that it does not always evaluate to 'False'. Some code paths are unreachabl
- [ ] Issue AaDbpHyEkXWrHeTEYN1g — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 122
      Summary: Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed.
- [ ] Issue AaDbpHyEkXWrHeTEYN1j — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 233
      Summary: Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed.
- [ ] Issue AaDbpHyEkXWrHeTEYN1m — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 336
      Summary: Refactor this method to reduce its Cognitive Complexity from 30 to the 15 allowed.
- [ ] Issue AaDbpHyEkXWrHeTEYN1l — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S127 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 251
      Summary: Do not update the stop condition variable 'i' in the body of the for loop.
- [ ] Issue AaDbpHyEkXWrHeTEYN1k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 275
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHyEkXWrHeTEYN1n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S127 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 407
      Summary: Do not update the stop condition variable 'i' in the body of the for loop.
- [ ] Issue AaDbpHyEkXWrHeTEYN1o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S127 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 413
      Summary: Do not update the stop condition variable 'i' in the body of the for loop.
- [ ] Issue AaDbpHyEkXWrHeTEYN1p — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S127 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 426
      Summary: Do not update the stop condition variable 'i' in the body of the for loop.
- [ ] Issue AaDbpHyEkXWrHeTEYN1f — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 214
      Summary: Loops should be simplified using the "Where" LINQ method
- [ ] Issue AaDbpHyEkXWrHeTEYN1h — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1125 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 328
      Summary: Remove the unnecessary Boolean literal(s).
- [ ] Issue AaDbpHyEkXWrHeTEYN1q — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1643 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 425
      Summary: Use a StringBuilder instead.
- [ ] Issue AaDbpHyEkXWrHeTEYN1e — Sonar type: CODE_SMELL — Severity: INFO — Rule: csharpsquid:S1135 — File: `src/Taskboard.Integrations/Harness/Security/DynamicCommandClassifier.cs` — Line: 285
      Summary: Complete the task associated to this 'TODO' comment.

### `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` (6 issues)
- [ ] Issue AaDbpHwwkXWrHeTEYN1D — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — Line: 66
      Summary: Pass the 'this._sweepCts.Token' to this method to allow cancellation of the operation, or use 'Cance
- [ ] Issue AaDbpHwwkXWrHeTEYN1F — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — Line: 373
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHwwkXWrHeTEYN1H — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — Line: 397
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDplmCATboaTa85QyoQ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — Line: 185
      Summary: Loops should be simplified using the "Any" LINQ method
- [ ] Issue AaDbpHwwkXWrHeTEYN1E — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — Line: 336
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHwwkXWrHeTEYN1G — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2486 — File: `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — Line: 396
      Summary: Handle the exception or explain in a comment why it can be ignored.

### `src/Taskboard.Server/Services/AgentSessionManager.cs` (25 issues)
- [ ] Issue AaDbpHjEkXWrHeTEYNuq — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 138
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpHjEkXWrHeTEYNus — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 363
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpHjEkXWrHeTEYNut — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 364
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpHjEkXWrHeTEYNuu — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 366
      Summary: Pass the 'cancellationToken' to this method to allow cancellation of the operation, or use 'Cancella
- [ ] Issue AaDbpHjEkXWrHeTEYNu0 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 429
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNu1 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 450
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNu2 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 450
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNu3 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 453
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNu4 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 481
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNu5 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 487
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNu6 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 507
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNu7 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 508
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNu8 — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 510
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNux — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 545
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNuy — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 552
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNuz — Sonar type: BUG — Severity: MAJOR — Rule: csharpsquid:S8949 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 554
      Summary: Pass the 'this._reaperCts.Token' to this method to allow cancellation of the operation, or use 'Canc
- [ ] Issue AaDbpHjEkXWrHeTEYNuv — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 419
      Summary: Refactor this method to reduce its Cognitive Complexity from 31 to the 15 allowed.
- [ ] Issue AaDbpHjEkXWrHeTEYNul — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 37
      Summary: Constructor has 9 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHjEkXWrHeTEYNum — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S2589 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 313
      Summary: Change this condition so that it does not always evaluate to 'True'.
- [ ] Issue AaDbpHjEkXWrHeTEYNun — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S2589 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 313
      Summary: Change this condition so that it does not always evaluate to 'True'.
- [ ] Issue AaDbpHjEkXWrHeTEYNuo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S2589 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 323
      Summary: Remove this unnecessary check for null.
- [ ] Issue AaDbpHjEkXWrHeTEYNup — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 336
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHjEkXWrHeTEYNuw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 565
      Summary: Await CancelAsync instead.
- [ ] Issue AaDbpHjEkXWrHeTEYNuk — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 214
      Summary: Define a constant instead of using this literal 'ai_chat.event' 4 times.
- [ ] Issue AaDbpHjEkXWrHeTEYNur — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Server/Services/AgentSessionManager.cs` — Line: 348
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entity.cs` (2 issues)
- [ ] Issue AaDbpHtekXWrHeTEYNzy — Sonar type: CODE_SMELL — Severity: BLOCKER — Rule: csharpsquid:S3875 — File: `src/Taskboard.Domain/Entity.cs` — Line: 40
      Summary: Remove this overload of 'operator =='.
- [ ] Issue AaDbpHtekXWrHeTEYNzx — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entity.cs` — Line: 7
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` (7 issues)
- [ ] Issue AaDbpHpXkXWrHeTEYNyg — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3218 — File: `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` — Line: 13
      Summary: Rename this property to not shadow the outer class' member with the same name.
- [ ] Issue AaDbpHpXkXWrHeTEYNyh — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` — Line: 18
      Summary: Define a constant instead of using this literal '--model' 7 times.
- [ ] Issue AaDbpHpXkXWrHeTEYNyi — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` — Line: 18
      Summary: Define a constant instead of using this literal 'haiku' 4 times.
- [ ] Issue AaDbpHpXkXWrHeTEYNyj — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` — Line: 18
      Summary: Define a constant instead of using this literal 'sonnet' 4 times.
- [ ] Issue AaDbpHpXkXWrHeTEYNyk — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` — Line: 19
      Summary: Define a constant instead of using this literal 'claude-sonnet-4-5' 4 times.
- [ ] Issue AaDbpHpXkXWrHeTEYNyl — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` — Line: 26
      Summary: Define a constant instead of using this literal 'gpt-5.6-luna' 4 times.
- [ ] Issue AaDbpHpXkXWrHeTEYNym — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` — Line: 27
      Summary: Define a constant instead of using this literal 'gpt-5.1' 4 times.

### `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs` (5 issues)
- [ ] Issue AaDbpHqlkXWrHeTEYNy5 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs` — Line: 289
      Summary: Refactor this method to reduce its Cognitive Complexity from 26 to the 15 allowed.
- [ ] Issue AaDbpHqlkXWrHeTEYNy4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs` — Line: 39
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHqlkXWrHeTEYNy2 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3398 — File: `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs` — Line: 182
      Summary: Move this method inside 'Accumulator'.
- [ ] Issue AaDbpHqlkXWrHeTEYNy3 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3398 — File: `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs` — Line: 217
      Summary: Move this method inside 'Accumulator'.
- [ ] Issue AaDbpHqlkXWrHeTEYNy6 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application.Contracts/AiChat/ToolCallRender.cs` — Line: 337
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs` (5 issues)
- [ ] Issue AaDukKv_kK9GZAB4jb_G — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs` — Line: 157
      Summary: Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed.
- [ ] Issue AaDukKv_kK9GZAB4jb_H — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs` — Line: 246
      Summary: Refactor this method to reduce its Cognitive Complexity from 66 to the 15 allowed.
- [ ] Issue AaDukKv_kK9GZAB4jb_I — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs` — Line: 69
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukKv_kK9GZAB4jb_F — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs` — Line: 96
      Summary: Define a constant instead of using this literal 'function' 5 times.
- [ ] Issue AaDukKv_kK9GZAB4jb_J — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application.Contracts/Chat/OpenAiCompatibleClient.cs` — Line: 194
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Application/AiChat/AiChatCatalogService.cs` (2 issues)
- [ ] Issue AaDbpHt4kXWrHeTEYNz3 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/AiChat/AiChatCatalogService.cs` — Line: 32
      Summary: Refactor this method to reduce its Cognitive Complexity from 27 to the 15 allowed.
- [ ] Issue AaDbpHt4kXWrHeTEYNz2 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application/AiChat/AiChatCatalogService.cs` — Line: 77
      Summary: Define a constant instead of using this literal 'custom' 4 times.

### `src/Taskboard.Application/Harness/FinOpsService.cs` (4 issues)
- [ ] Issue AaDbpHvPkXWrHeTEYN0f — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/Harness/FinOpsService.cs` — Line: 427
      Summary: Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed.
- [ ] Issue AaDbpHvPkXWrHeTEYN0c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Application/Harness/FinOpsService.cs` — Line: 35
      Summary: Constructor has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHvPkXWrHeTEYN0d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Application/Harness/FinOpsService.cs` — Line: 178
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHvPkXWrHeTEYN0e — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/Harness/FinOpsService.cs` — Line: 193
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs` (2 issues)
- [ ] Issue AaDbpHvhkXWrHeTEYN0h — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs` — Line: 409
      Summary: Refactor this method to reduce its Cognitive Complexity from 31 to the 15 allowed.
- [ ] Issue AaDbpHvhkXWrHeTEYN0g — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs` — Line: 33
      Summary: Constructor has 9 parameters, which is greater than the 7 authorized.

### `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` (10 issues)
- [ ] Issue AaDbpHoKkXWrHeTEYNyN — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 514
      Summary: Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed.
- [ ] Issue AaDbpHoKkXWrHeTEYNyK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 283
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHoKkXWrHeTEYNyI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 440
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHoKkXWrHeTEYNyP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 497
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHoKkXWrHeTEYNyQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 500
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHoKkXWrHeTEYNyR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 504
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHoKkXWrHeTEYNyL — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1481 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 395
      Summary: Remove the unused local variable 'tool'.
- [ ] Issue AaDbpHoKkXWrHeTEYNyM — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1481 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 400
      Summary: Remove the unused local variable 'output'.
- [ ] Issue AaDbpHoKkXWrHeTEYNyJ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2325 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 433
      Summary: Make 'RenderPlan' a static method.
- [ ] Issue AaDbpHoKkXWrHeTEYNyO — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2325 — File: `src/Taskboard.Blazor/Components/Agents/AgentRunTimeline.razor` — Line: 448
      Summary: Make 'RenderMetric' a static method.

### `src/Taskboard.Blazor/Components/Chat/ProviderChat.razor` (1 issues)
- [ ] Issue AaDukKmakK9GZAB4jb_A — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Chat/ProviderChat.razor` — Line: 319
      Summary: Refactor this method to reduce its Cognitive Complexity from 17 to the 15 allowed.

### `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` (5 issues)
- [ ] Issue AaDbpHnjkXWrHeTEYNx- — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` — Line: 172
      Summary: Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed.
- [ ] Issue AaDbpHnjkXWrHeTEYNx_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` — Line: 174
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHnjkXWrHeTEYNyA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` — Line: 175
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHnjkXWrHeTEYNyB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` — Line: 176
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHnjkXWrHeTEYNyC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor` — Line: 177
      Summary: Extract this nested ternary operation into an independent statement.

### `src/Taskboard.Blazor/Components/Cockpit/RunTerminal.razor` (1 issues)
- [ ] Issue AaDbpHntkXWrHeTEYNyD — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Cockpit/RunTerminal.razor` — Line: 94
      Summary: Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed.

### `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` (4 issues)
- [ ] Issue AaDbpHmykXWrHeTEYNxq — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` — Line: 147
      Summary: Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed.
- [ ] Issue AaDbpHmykXWrHeTEYNxr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` — Line: 198
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmykXWrHeTEYNxs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` — Line: 288
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmykXWrHeTEYNxt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor` — Line: 294
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/Pages/AiChat.razor` (44 issues)
- [ ] Issue AaDpllQ1TboaTa85QyoA — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S2365 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 628
      Summary: Refactor 'ConfigAgentModels' into a method, properties should not copy collections.
- [ ] Issue AaDtEWM7B79WDrchFHf4 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 646
      Summary: Refactor this method to reduce its Cognitive Complexity from 33 to the 15 allowed.
- [ ] Issue AaDpllQ1TboaTa85QyoB — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 813
      Summary: Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed.
- [ ] Issue AaDbpHk4kXWrHeTEYNwB — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1070
      Summary: Refactor this method to reduce its Cognitive Complexity from 66 to the 15 allowed.
- [ ] Issue AaDbpHk4kXWrHeTEYNwH — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1132
      Summary: Refactor this method to reduce its Cognitive Complexity from 43 to the 15 allowed.
- [ ] Issue AaDbpHk4kXWrHeTEYNwG — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1223
      Summary: Refactor this method to reduce its Cognitive Complexity from 26 to the 15 allowed.
- [ ] Issue AaDbpHk4kXWrHeTEYNwc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 403
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHk4kXWrHeTEYNv6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S2933 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 566
      Summary: Make '_disposeCts' 'readonly'.
- [ ] Issue AaDbpHk4kXWrHeTEYNv7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 619
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHk4kXWrHeTEYNv8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 624
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHk4kXWrHeTEYNwA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 822
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDpllQ1TboaTa85QyoC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 857
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDpllQ1TboaTa85QyoD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1027
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDpXQtRyeT6D8TPfvBG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1038
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHk4kXWrHeTEYNwC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1099
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHk4kXWrHeTEYNwD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1127
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHk4kXWrHeTEYNwI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1189
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHk4kXWrHeTEYNwJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1199
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHk4kXWrHeTEYNwE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1066 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1216
      Summary: Merge this if statement with the enclosing one.
- [ ] Issue AaDbpHk4kXWrHeTEYNwK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1261
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1286
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1303
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1314
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1320
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDtM3K0Nw_k69o7k__d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1356
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDtM3K0Nw_k69o7k__e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1374
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1394
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1416
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwQ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1421
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1436
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1456
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1476
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1487
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1491
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1500
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1556
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1562
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1572
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHk4kXWrHeTEYNwX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1602
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHk4kXWrHeTEYNwW — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 1619
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHk4kXWrHeTEYNwd — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1481 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 131
      Summary: Remove the unused local variable 'tool'.
- [ ] Issue AaDbpHk4kXWrHeTEYNwe — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1481 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 137
      Summary: Remove the unused local variable 'group'.
- [ ] Issue AaDpllQ1TboaTa85QyoE — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 276
      Summary: Loop should be simplified by calling Select(container => container.Name)
- [ ] Issue AaDtEWM7B79WDrchFHf5 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — Line: 661
      Summary: Loops should be simplified using the "Where" LINQ method

### `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` (21 issues)
- [ ] Issue AaDbpHkWkXWrHeTEYNvi — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4487 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 149
      Summary: Remove this unread private field '_diff' or refactor the code to use its value.
- [ ] Issue AaDbpHkWkXWrHeTEYNvm — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 196
      Summary: Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed.
- [ ] Issue AaDbpHkWkXWrHeTEYNvl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 187
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNv1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 234
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNv2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 275
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 294
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHkWkXWrHeTEYNvk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 307
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHkWkXWrHeTEYNvr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 334
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 339
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 377
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 390
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 394
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 408
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 422
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 435
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 455
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvz — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 466
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNv0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 471
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 479
      Summary: Await CancelAsync instead.
- [ ] Issue AaDbpHkWkXWrHeTEYNvw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 483
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHkWkXWrHeTEYNvs — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — Line: 249
      Summary: Loops should be simplified using the "Where" LINQ method

### `src/Taskboard.Blazor/Components/Pages/Terminal.razor` (1 issues)
- [ ] Issue AaDbpHj8kXWrHeTEYNvf — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Blazor/Components/Pages/Terminal.razor` — Line: 160
      Summary: Refactor this method to reduce its Cognitive Complexity from 17 to the 15 allowed.

### `src/Taskboard.Blazor/Services/SelectedRepositoryService.cs` (1 issues)
- [ ] Issue AaDbpHomkXWrHeTEYNyU — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3217 — File: `src/Taskboard.Blazor/Services/SelectedRepositoryService.cs` — Line: 139
      Summary: Either change the type of 'handler' to 'Delegate' or iterate on a generic collection of type 'Func<T

### `src/Taskboard.Cli/Program.cs` (15 issues)
- [ ] Issue AaDtDTMUod7YJFS1ySKl — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4015 — File: `src/Taskboard.Cli/Program.cs` — Line: 143
      Summary: This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.EmptySettings>.ExecuteAsync(Spectr
- [ ] Issue AaDtDTMUod7YJFS1ySKh — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4015 — File: `src/Taskboard.Cli/Program.cs` — Line: 161
      Summary: This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.CloudLoginSettings>.ExecuteAsync(S
- [ ] Issue AaDtDTMUod7YJFS1ySKg — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4015 — File: `src/Taskboard.Cli/Program.cs` — Line: 187
      Summary: This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.CloudStatusSettings>.ExecuteAsync(
- [ ] Issue AaDtDTMUod7YJFS1ySKi — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4015 — File: `src/Taskboard.Cli/Program.cs` — Line: 201
      Summary: This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.CloudLogoutSettings>.ExecuteAsync(
- [ ] Issue AaDtDTMUod7YJFS1ySKj — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4015 — File: `src/Taskboard.Cli/Program.cs` — Line: 219
      Summary: This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.ContextCurrentSettings>.ExecuteAsy
- [ ] Issue AaDtDTMUod7YJFS1ySKk — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4015 — File: `src/Taskboard.Cli/Program.cs` — Line: 250
      Summary: This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.GitHubIssueHistorySettings>.Execut
- [ ] Issue AaDtDTMUod7YJFS1ySKn — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4015 — File: `src/Taskboard.Cli/Program.cs` — Line: 274
      Summary: This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.GitHubIssueCommentListSettings>.Ex
- [ ] Issue AaDtDTMUod7YJFS1ySKm — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4015 — File: `src/Taskboard.Cli/Program.cs` — Line: 310
      Summary: This member hides 'Spectre.Console.Cli.AsyncCommand<Taskboard.Cli.GitHubIssueCommentAddSettings>.Exe
- [ ] Issue AaDbpH5wkXWrHeTEYN3_ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2094 — File: `src/Taskboard.Cli/Program.cs` — Line: 181
      Summary: Remove this empty class, write its code or make it an "interface".
- [ ] Issue AaDbpH5wkXWrHeTEYN4A — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2094 — File: `src/Taskboard.Cli/Program.cs` — Line: 195
      Summary: Remove this empty class, write its code or make it an "interface".
- [ ] Issue AaDbpH5wkXWrHeTEYN4B — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2094 — File: `src/Taskboard.Cli/Program.cs` — Line: 213
      Summary: Remove this empty class, write its code or make it an "interface".
- [ ] Issue AaDbpH5wkXWrHeTEYN4D — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Cli/Program.cs` — Line: 242
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH5wkXWrHeTEYN4C — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Cli/Program.cs` — Line: 263
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH5wkXWrHeTEYN4F — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Cli/Program.cs` — Line: 299
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH5wkXWrHeTEYN4E — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Cli/Program.cs` — Line: 305
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain.Shared/Agents/AgentCliArgsTemplate.cs` (1 issues)
- [ ] Issue AaDplk9_TboaTa85Qyn9 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Domain.Shared/Agents/AgentCliArgsTemplate.cs` — Line: 46
      Summary: Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed.

### `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` (6 issues)
- [ ] Issue AaDbpHgpkXWrHeTEYNuQ — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S2365 — File: `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` — Line: 182
      Summary: Refactor 'All' into a method, properties should not copy collections.
- [ ] Issue AaDbpHgpkXWrHeTEYNuL — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1075 — File: `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` — Line: 93
      Summary: Refactor your code not to use hardcoded absolute paths or URIs.
- [ ] Issue AaDbpHgpkXWrHeTEYNuM — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1075 — File: `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` — Line: 101
      Summary: Refactor your code not to use hardcoded absolute paths or URIs.
- [ ] Issue AaDbpHgpkXWrHeTEYNuN — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1075 — File: `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` — Line: 109
      Summary: Refactor your code not to use hardcoded absolute paths or URIs.
- [ ] Issue AaDbpHgpkXWrHeTEYNuO — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1075 — File: `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` — Line: 117
      Summary: Refactor your code not to use hardcoded absolute paths or URIs.
- [ ] Issue AaDbpHgpkXWrHeTEYNuP — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1075 — File: `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` — Line: 165
      Summary: Refactor your code not to use hardcoded absolute paths or URIs.

### `src/Taskboard.Domain.Shared/Agents/CliDatabaseMap.cs` (1 issues)
- [ ] Issue AaDbpHgfkXWrHeTEYNuK — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S2365 — File: `src/Taskboard.Domain.Shared/Agents/CliDatabaseMap.cs` — Line: 96
      Summary: Refactor 'All' into a method, properties should not copy collections.

### `src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs` (3 issues)
- [ ] Issue AaDbpHs8kXWrHeTEYNzl — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs` — Line: 17
      Summary: Refactor this method to reduce its Cognitive Complexity from 21 to the 15 allowed.
- [ ] Issue AaDbpHs8kXWrHeTEYNzm — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs` — Line: 26
      Summary: Loops should be simplified using the "Where" LINQ method
- [ ] Issue AaDbpHs8kXWrHeTEYNzn — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs` — Line: 41
      Summary: Loops should be simplified using the "Where" LINQ method

### `src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs` (4 issues)
- [ ] Issue AaDbpH5FkXWrHeTEYN31 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs` — Line: 135
      Summary: Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed.
- [ ] Issue AaDbpH5FkXWrHeTEYN32 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6580 — File: `src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs` — Line: 140
      Summary: Use a format provider when parsing date and time.
- [ ] Issue AaDbpH5FkXWrHeTEYN33 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1905 — File: `src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs` — Line: 234
      Summary: Remove this unnecessary cast to 'long'.
- [ ] Issue AaDbpH5FkXWrHeTEYN34 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1905 — File: `src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs` — Line: 246
      Summary: Remove this unnecessary cast to 'long'.

### `src/Taskboard.EntityFrameworkCore/Migrations/20260922204740_RepairPipelineStageTriedAgentsDefault.cs` (1 issues)
- [ ] Issue AaDbpH46kXWrHeTEYN30 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S1186 — File: `src/Taskboard.EntityFrameworkCore/Migrations/20260922204740_RepairPipelineStageTriedAgentsDefault.cs` — Line: 20
      Summary: Add a nested comment explaining why this method is empty, throw a 'NotSupportedException' or complet

### `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` (5 issues)
- [ ] Issue AaDbpH3DkXWrHeTEYN3D — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` — Line: 52
      Summary: Refactor this method to reduce its Cognitive Complexity from 49 to the 15 allowed.
- [ ] Issue AaDbpH3DkXWrHeTEYN3C — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` — Line: 145
      Summary: Refactor this method to reduce its Cognitive Complexity from 42 to the 15 allowed.
- [ ] Issue AaDbpH3DkXWrHeTEYN3E — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` — Line: 120
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH3DkXWrHeTEYN3B — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` — Line: 125
      Summary: Define a constant instead of using this literal 'agent' 4 times.
- [ ] Issue AaDbpH3DkXWrHeTEYN3F — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` — Line: 205
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Integrations/Agents/AcpProtocolParser.cs` (1 issues)
- [ ] Issue AaDbpH0CkXWrHeTEYN2S — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpProtocolParser.cs` — Line: 73
      Summary: Refactor this method to reduce its Cognitive Complexity from 20 to the 15 allowed.

### `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs` (4 issues)
- [ ] Issue AaDbpH26kXWrHeTEYN2- — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S927 — File: `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs` — Line: 75
      Summary: Rename parameter 'p' to 'updateParams' to match the interface declaration.
- [ ] Issue AaDbpH26kXWrHeTEYN2_ — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs` — Line: 142
      Summary: Refactor this method to reduce its Cognitive Complexity from 32 to the 15 allowed.
- [ ] Issue AaDbpH26kXWrHeTEYN3A — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs` — Line: 193
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH26kXWrHeTEYN29 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs` — Line: 85
      Summary: Define a constant instead of using this literal 'session/update' 12 times.

### `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` (6 issues)
- [ ] Issue AaDbpH3NkXWrHeTEYN3J — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S927 — File: `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` — Line: 97
      Summary: Rename parameter 'p' to 'updateParams' to match the interface declaration.
- [ ] Issue AaDbpH3NkXWrHeTEYN3K — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` — Line: 281
      Summary: Refactor this method to reduce its Cognitive Complexity from 32 to the 15 allowed.
- [ ] Issue AaDbpH3NkXWrHeTEYN3L — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` — Line: 288
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH3NkXWrHeTEYN3M — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` — Line: 329
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH3NkXWrHeTEYN3H — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` — Line: 104
      Summary: Define a constant instead of using this literal 'session/update' 12 times.
- [ ] Issue AaDbpH3NkXWrHeTEYN3I — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` — Line: 123
      Summary: Define a constant instead of using this literal 'assistant' 4 times.

### `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs` (4 issues)
- [ ] Issue AaDukLvmkK9GZAB4jb_e — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs` — Line: 11
      Summary: Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed.
- [ ] Issue AaDukLvmkK9GZAB4jb_g — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs` — Line: 60
      Summary: Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed.
- [ ] Issue AaDukLvmkK9GZAB4jb_h — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs` — Line: 81
      Summary: Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed.
- [ ] Issue AaDukLvmkK9GZAB4jb_f — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1075 — File: `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs` — Line: 52
      Summary: Refactor your code not to use hardcoded absolute paths or URIs.

### `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` (4 issues)
- [ ] Issue AaDbpHxFkXWrHeTEYN1L — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` — Line: 156
      Summary: Refactor this method to reduce its Cognitive Complexity from 23 to the 15 allowed.
- [ ] Issue AaDbpHxFkXWrHeTEYN1K — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S6667 — File: `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` — Line: 149
      Summary: Logging in a catch clause should pass the caught exception as a parameter.
- [ ] Issue AaDbpHxFkXWrHeTEYN1M — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S6667 — File: `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` — Line: 216
      Summary: Logging in a catch clause should pass the caught exception as a parameter.
- [ ] Issue AaDbpHxFkXWrHeTEYN1N — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S6667 — File: `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` — Line: 222
      Summary: Logging in a catch clause should pass the caught exception as a parameter.

### `src/Taskboard.Integrations/CliDb/Extractors/AntigravityCliDbExtractor.cs` (2 issues)
- [ ] Issue AaDbpHw6kXWrHeTEYN1I — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S2365 — File: `src/Taskboard.Integrations/CliDb/Extractors/AntigravityCliDbExtractor.cs` — Line: 43
      Summary: Refactor 'Sources' into a method, properties should not copy collections.
- [ ] Issue AaDbpHw6kXWrHeTEYN1J — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/CliDb/Extractors/AntigravityCliDbExtractor.cs` — Line: 57
      Summary: Define a constant instead of using this literal 'rowid' 5 times.

### `src/Taskboard.Integrations/Harness/Context/ProjectContextCompiler.cs` (1 issues)
- [ ] Issue AaDbpHxzkXWrHeTEYN1c — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Harness/Context/ProjectContextCompiler.cs` — Line: 47
      Summary: Refactor this method to reduce its Cognitive Complexity from 17 to the 15 allowed.

### `src/Taskboard.Integrations/Skills/FrontmatterReader.cs` (1 issues)
- [ ] Issue AaDbpHzBkXWrHeTEYN16 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Skills/FrontmatterReader.cs` — Line: 7
      Summary: Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed.

### `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` (5 issues)
- [ ] Issue AaDbpHzmkXWrHeTEYN2E — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S927 — File: `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` — Line: 61
      Summary: Rename parameter 'sourceName' to 'source' to match the interface declaration.
- [ ] Issue AaDbpHzmkXWrHeTEYN2F — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S927 — File: `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` — Line: 86
      Summary: Rename parameter 'sourceName' to 'source' to match the interface declaration.
- [ ] Issue AaDbpHzmkXWrHeTEYN2G — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` — Line: 209
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHzmkXWrHeTEYN2H — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3220 — File: `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` — Line: 188
      Summary: Review this call, which partially matches an overload without 'params'. The partial match is 'string
- [ ] Issue AaDbpHzmkXWrHeTEYN2I — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3220 — File: `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` — Line: 203
      Summary: Review this call, which partially matches an overload without 'params'. The partial match is 'string

### `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs` (4 issues)
- [ ] Issue AaDbpHzUkXWrHeTEYN2A — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs` — Line: 139
      Summary: Refactor this method to reduce its Cognitive Complexity from 25 to the 15 allowed.
- [ ] Issue AaDbpHzUkXWrHeTEYN1_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs` — Line: 48
      Summary: Constructor has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHzUkXWrHeTEYN2B — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs` — Line: 162
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHzUkXWrHeTEYN1- — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Skills/SkillsInstallerService.cs` — Line: 178
      Summary: Define a constant instead of using this literal 'install-sh' 6 times.

### `src/Taskboard.Integrations/Skills/SkillsSyncService.cs` (3 issues)
- [ ] Issue AaDbpHzKkXWrHeTEYN18 — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Skills/SkillsSyncService.cs` — Line: 218
      Summary: Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed.
- [ ] Issue AaDbpHzKkXWrHeTEYN19 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Skills/SkillsSyncService.cs` — Line: 244
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHzKkXWrHeTEYN17 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Skills/SkillsSyncService.cs` — Line: 291
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Integrations/Specs/MarkdigSpecParser.cs` (3 issues)
- [ ] Issue AaDbpH4XkXWrHeTEYN3v — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Integrations/Specs/MarkdigSpecParser.cs` — Line: 269
      Summary: Refactor this method to reduce its Cognitive Complexity from 34 to the 15 allowed.
- [ ] Issue AaDbpH4XkXWrHeTEYN3u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6580 — File: `src/Taskboard.Integrations/Specs/MarkdigSpecParser.cs` — Line: 36
      Summary: Use a format provider when parsing date and time.
- [ ] Issue AaDbpH4XkXWrHeTEYN3w — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1125 — File: `src/Taskboard.Integrations/Specs/MarkdigSpecParser.cs` — Line: 317
      Summary: Remove the unnecessary Boolean literal(s).

### `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` (6 issues)
- [ ] Issue AaDbpHz5kXWrHeTEYN2M — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S4487 — File: `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` — Line: 40
      Summary: Remove this unread private field '_lastStartFailed' or refactor the code to use its value.
- [ ] Issue AaDbpHz5kXWrHeTEYN2N — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` — Line: 43
      Summary: Constructor has 10 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHz5kXWrHeTEYN2R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` — Line: 394
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHz5kXWrHeTEYN2O — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S6667 — File: `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` — Line: 299
      Summary: Logging in a catch clause should pass the caught exception as a parameter.
- [ ] Issue AaDbpHz5kXWrHeTEYN2P — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` — Line: 326
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHz5kXWrHeTEYN2Q — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2486 — File: `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` — Line: 393
      Summary: Handle the exception or explain in a comment why it can be ignored.

### `src/Taskboard.Server/Services/AcpClientToolHandler.cs` (7 issues)
- [ ] Issue AaDbpHiYkXWrHeTEYNub — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S927 — File: `src/Taskboard.Server/Services/AcpClientToolHandler.cs` — Line: 64
      Summary: Rename parameter 'p' to 'params' to match the interface declaration.
- [ ] Issue AaDbpHiYkXWrHeTEYNue — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Server/Services/AcpClientToolHandler.cs` — Line: 184
      Summary: Refactor this method to reduce its Cognitive Complexity from 25 to the 15 allowed.
- [ ] Issue AaDbpHiYkXWrHeTEYNuf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Server/Services/AcpClientToolHandler.cs` — Line: 184
      Summary: Remove this unused method parameter 'sessionId'.
- [ ] Issue AaDbpHiYkXWrHeTEYNuZ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Services/AcpClientToolHandler.cs` — Line: 77
      Summary: Define a constant instead of using this literal 'terminal/create' 4 times.
- [ ] Issue AaDbpHiYkXWrHeTEYNuc — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2325 — File: `src/Taskboard.Server/Services/AcpClientToolHandler.cs` — Line: 131
      Summary: Make 'ReadTextFile' a static method.
- [ ] Issue AaDbpHiYkXWrHeTEYNua — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Services/AcpClientToolHandler.cs` — Line: 165
      Summary: Define a constant instead of using this literal 'allow' 6 times.
- [ ] Issue AaDbpHiYkXWrHeTEYNud — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Server/Services/AcpClientToolHandler.cs` — Line: 177
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Server/Services/AcpSessionModelCatalog.cs` (1 issues)
- [ ] Issue AaDbpHiEkXWrHeTEYNuX — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Server/Services/AcpSessionModelCatalog.cs` — Line: 32
      Summary: Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed.

### `src/Taskboard.Server/Services/AgentControlService.cs` (7 issues)
- [ ] Issue AaDbpHjPkXWrHeTEYNvC — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Server/Services/AgentControlService.cs` — Line: 62
      Summary: Refactor this method to reduce its Cognitive Complexity from 35 to the 15 allowed.
- [ ] Issue AaDbpHjPkXWrHeTEYNvD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Server/Services/AgentControlService.cs` — Line: 41
      Summary: Constructor has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHjPkXWrHeTEYNu9 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Services/AgentControlService.cs` — Line: 96
      Summary: Define a constant instead of using this literal 'steer' 5 times.
- [ ] Issue AaDbpHjPkXWrHeTEYNu- — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Services/AgentControlService.cs` — Line: 131
      Summary: Define a constant instead of using this literal 'no-such-run' 5 times.
- [ ] Issue AaDbpHjPkXWrHeTEYNu_ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Services/AgentControlService.cs` — Line: 174
      Summary: Define a constant instead of using this literal 'no-active-session' 4 times.
- [ ] Issue AaDbpHjPkXWrHeTEYNvA — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Services/AgentControlService.cs` — Line: 269
      Summary: Define a constant instead of using this literal 'stage:' 4 times.
- [ ] Issue AaDbpHjPkXWrHeTEYNvB — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Server/Services/AgentControlService.cs` — Line: 349
      Summary: Define a constant instead of using this literal 'running' 4 times.

### `src/Taskboard.Server/Services/PromptQueue.cs` (1 issues)
- [ ] Issue AaDbpHiOkXWrHeTEYNuY — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S2365 — File: `src/Taskboard.Server/Services/PromptQueue.cs` — Line: 18
      Summary: Refactor 'Pending' into a method, properties should not copy collections.

### `src/Taskboard.Server/Services/ThreadPtyResolver.cs` (2 issues)
- [ ] Issue AaDpllKNTboaTa85Qyn- — Sonar type: CODE_SMELL — Severity: CRITICAL — Rule: csharpsquid:S3776 — File: `src/Taskboard.Server/Services/ThreadPtyResolver.cs` — Line: 33
      Summary: Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed.
- [ ] Issue AaDtDRfHod7YJFS1ySKf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Server/Services/ThreadPtyResolver.cs` — Line: 53
      Summary: Extract this nested ternary operation into an independent statement.

### `install-cli.sh` (1 issues)
- [ ] Issue AaDbpH7FkXWrHeTEYN4J — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `install-cli.sh` — Line: 146
      Summary: Assign this positional parameter to a local variable.

### `install.sh` (34 issues)
- [ ] Issue AaDbpH-TkXWrHeTEYOIm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 12
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 49
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 53
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 70
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIq — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 80
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIr — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 89
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIs — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 109
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 121
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 127
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 133
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 160
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 173
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 173
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOIz — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 180
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 190
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 196
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 200
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 204
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 209
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 214
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 235
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 239
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 340
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 349
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 406
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOI_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 410
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOJA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 425
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOJB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 425
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOJC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 428
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOJD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 435
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOJE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 444
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOJF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 450
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOJG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 463
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.
- [ ] Issue AaDbpH-TkXWrHeTEYOJH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7688 — File: `install.sh` — Line: 478
      Summary: Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.

### `scripts/tests/check-spec-status.test.sh` (16 issues)
- [ ] Issue AaDbpH7OkXWrHeTEYN4K — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 15
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4L — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 15
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4M — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 15
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4N — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 15
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4O — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 15
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4P — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 15
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4Q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 17
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4R — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 17
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4S — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 17
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 17
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4U — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 17
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4V — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 18
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4W — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 18
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4X — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 18
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4Y — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 18
      Summary: Assign this positional parameter to a local variable.
- [ ] Issue AaDbpH7OkXWrHeTEYN4Z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: shelldre:S7679 — File: `scripts/tests/check-spec-status.test.sh` — Line: 18
      Summary: Assign this positional parameter to a local variable.

### `src/Taskboard.Application.Contracts/AiChat/ProblemDetailReader.cs` (1 issues)
- [ ] Issue AaDbpHqSkXWrHeTEYNyv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Application.Contracts/AiChat/ProblemDetailReader.cs` — Line: 55
      Summary: Either remove or fill this block of code.

### `src/Taskboard.Application.Contracts/CliDb/ICliDatabaseReader.cs` (2 issues)
- [ ] Issue AaDbpHrSkXWrHeTEYNzA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Application.Contracts/CliDb/ICliDatabaseReader.cs` — Line: 36
      Summary: Method has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHrSkXWrHeTEYNzB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Application.Contracts/CliDb/ICliDatabaseReader.cs` — Line: 55
      Summary: Method has 8 parameters, which is greater than the 7 authorized.

### `src/Taskboard.Application.Contracts/CliMetrics/ICliMetricsRepository.cs` (1 issues)
- [ ] Issue AaDbpHrKkXWrHeTEYNy_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Application.Contracts/CliMetrics/ICliMetricsRepository.cs` — Line: 25
      Summary: Method has 13 parameters, which is greater than the 7 authorized.

### `src/Taskboard.Application/Harness/FinOpsAggregator.cs` (2 issues)
- [ ] Issue AaDbpHu3kXWrHeTEYN0K — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Application/Harness/FinOpsAggregator.cs` — Line: 72
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHu3kXWrHeTEYN0L — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/Harness/FinOpsAggregator.cs` — Line: 101
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Blazor/Components/AiChat/ThreadRail.razor` (2 issues)
- [ ] Issue AaDpXQ6ZyeT6D8TPfvBH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/AiChat/ThreadRail.razor` — Line: 126
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDpXQ6ZyeT6D8TPfvBI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/AiChat/ThreadRail.razor` — Line: 127
      Summary: Extract this nested ternary operation into an independent statement.

### `src/Taskboard.Blazor/Components/AiChat/ToolCallCard.razor` (1 issues)
- [ ] Issue AaDbpHoAkXWrHeTEYNyF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Blazor/Components/AiChat/ToolCallCard.razor` — Line: 119
      Summary: Extract this nested ternary operation into an independent statement.

### `src/Taskboard.Blazor/Components/GitHub/AgentSelectionModal.razor` (1 issues)
- [ ] Issue AaDbpHmfkXWrHeTEYNxk — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/AgentSelectionModal.razor` — Line: 106
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/GitHub/IssueCommentsTab.razor` (2 issues)
- [ ] Issue AaDbpHnakXWrHeTEYNx8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/IssueCommentsTab.razor` — Line: 129
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnakXWrHeTEYNx9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/IssueCommentsTab.razor` — Line: 134
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` (6 issues)
- [ ] Issue AaDbpHnHkXWrHeTEYNxw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` — Line: 367
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnHkXWrHeTEYNxx — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` — Line: 373
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnHkXWrHeTEYNxy — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` — Line: 386
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnHkXWrHeTEYNxz — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` — Line: 440
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnHkXWrHeTEYNx0 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` — Line: 446
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnHkXWrHeTEYNx1 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` — Line: 454
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor` (2 issues)
- [ ] Issue AaDbpHm6kXWrHeTEYNxu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor` — Line: 92
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHm6kXWrHeTEYNxv — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor` — Line: 97
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` (6 issues)
- [ ] Issue AaDbpHnQkXWrHeTEYNx2 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` — Line: 221
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnQkXWrHeTEYNx3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` — Line: 226
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnQkXWrHeTEYNx4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` — Line: 246
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnQkXWrHeTEYNx5 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` — Line: 251
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnQkXWrHeTEYNx6 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` — Line: 266
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHnQkXWrHeTEYNx7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` — Line: 271
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` (5 issues)
- [ ] Issue AaDbpHmokXWrHeTEYNxl — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` — Line: 37
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmokXWrHeTEYNxm — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` — Line: 49
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmokXWrHeTEYNxn — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` — Line: 54
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmokXWrHeTEYNxo — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` — Line: 75
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmokXWrHeTEYNxp — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` — Line: 80
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` (5 issues)
- [ ] Issue AaDbpHlvkXWrHeTEYNxN — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` — Line: 236
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlvkXWrHeTEYNxL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` — Line: 275
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlvkXWrHeTEYNxM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` — Line: 280
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlvkXWrHeTEYNxO — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` — Line: 294
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlvkXWrHeTEYNxP — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` — Line: 299
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/Pages/Agents.razor` (12 issues)
- [ ] Issue AaDpllTpTboaTa85QyoH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 415
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDpllToTboaTa85QyoF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 429
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDpllTpTboaTa85QyoG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 433
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHllkXWrHeTEYNxC — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 451
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHllkXWrHeTEYNxD — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 465
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHllkXWrHeTEYNxE — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 470
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHllkXWrHeTEYNxF — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 484
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHllkXWrHeTEYNxG — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 489
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHllkXWrHeTEYNxH — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 609
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHllkXWrHeTEYNxI — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 613
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHllkXWrHeTEYNxJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 617
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHllkXWrHeTEYNxK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Agents.razor` — Line: 624
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` (17 issues)
- [ ] Issue AaDbpHmMkXWrHeTEYNxS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S2933 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 269
      Summary: Make '_cts' 'readonly'.
- [ ] Issue AaDbpHmMkXWrHeTEYNxT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 344
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 361
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHmMkXWrHeTEYNxV — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 394
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHmMkXWrHeTEYNxW — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 413
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxa — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 431
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 446
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxd — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 458
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxb — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 499
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxX — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 532
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxY — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 555
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxZ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 563
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 600
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1121 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 609
      Summary: Extract the assignment of '_stageOptions[stageKey]' from this expression.
- [ ] Issue AaDbpHmMkXWrHeTEYNxh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 653
      Summary: Await CancelAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 655
      Summary: Await CancelAsync instead.
- [ ] Issue AaDbpHmMkXWrHeTEYNxg — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — Line: 659
      Summary: Either remove or fill this block of code.

### `src/Taskboard.Blazor/Components/Pages/FinOps.razor` (3 issues)
- [ ] Issue AaDbpHlRkXWrHeTEYNw7 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/FinOps.razor` — Line: 411
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlRkXWrHeTEYNw8 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/FinOps.razor` — Line: 423
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlRkXWrHeTEYNw9 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/FinOps.razor` — Line: 439
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/Pages/Jobs.razor` (7 issues)
- [ ] Issue AaDt34v780awsUzQdyod — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Components/Pages/Jobs.razor` — Line: 179
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDt34v780awsUzQdyoe — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Jobs.razor` — Line: 230
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDt34v780awsUzQdyof — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Jobs.razor` — Line: 234
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDt34v780awsUzQdyog — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Jobs.razor` — Line: 253
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDt34v780awsUzQdyoj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Jobs.razor` — Line: 258
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDt34v780awsUzQdyoh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Jobs.razor` — Line: 267
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDt34v780awsUzQdyoi — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Jobs.razor` — Line: 271
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/Pages/Prompts.razor` (1 issues)
- [ ] Issue AaDbpHkfkXWrHeTEYNv3 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Prompts.razor` — Line: 92
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/Pages/RunAgentDialog.razor` (1 issues)
- [ ] Issue AaDbpHmBkXWrHeTEYNxR — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/RunAgentDialog.razor` — Line: 97
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/Pages/Specs.razor` (4 issues)
- [ ] Issue AaDbpHlakXWrHeTEYNw- — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Specs.razor` — Line: 230
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlakXWrHeTEYNxA — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Specs.razor` — Line: 233
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlakXWrHeTEYNxB — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Specs.razor` — Line: 242
      Summary: Await NotifyAsync instead.
- [ ] Issue AaDbpHlakXWrHeTEYNw_ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Pages/Specs.razor` — Line: 263
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Components/Pages/Workflow.razor` (1 issues)
- [ ] Issue AaDbpHkpkXWrHeTEYNv4 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1854 — File: `src/Taskboard.Blazor/Components/Pages/Workflow.razor` — Line: 301
      Summary: Remove this useless assignment to local variable '_'.

### `src/Taskboard.Blazor/Components/Shared/SkillDetailDialog.razor` (1 issues)
- [ ] Issue AaDbpHmWkXWrHeTEYNxj — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Blazor/Components/Shared/SkillDetailDialog.razor` — Line: 117
      Summary: Await NotifyAsync instead.

### `src/Taskboard.Blazor/Services/TaskboardClient.cs` (6 issues)
- [ ] Issue AaDbpHpFkXWrHeTEYNyf — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Blazor/Services/TaskboardClient.cs` — Line: 856
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHpFkXWrHeTEYNye — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2486 — File: `src/Taskboard.Blazor/Services/TaskboardClient.cs` — Line: 855
      Summary: Handle the exception or explain in a comment why it can be ignored.
- [ ] Issue AaDukKqWkK9GZAB4jb_B — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/TaskboardClient.cs` — Line: 879
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukKqWkK9GZAB4jb_C — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/TaskboardClient.cs` — Line: 887
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukKqWkK9GZAB4jb_D — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/TaskboardClient.cs` — Line: 901
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukKqWkK9GZAB4jb_E — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/TaskboardClient.cs` — Line: 921
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Client/wwwroot/css/site.css` (3 issues)
- [ ] Issue AaDbpHwakXWrHeTEYN0z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: css:S4666 — File: `src/Taskboard.Client/wwwroot/css/site.css` — Line: 1285
      Summary: Duplicate selector ".filter-chip", first used at line 555
- [ ] Issue AaDbpHwakXWrHeTEYN00 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: css:S4666 — File: `src/Taskboard.Client/wwwroot/css/site.css` — Line: 1315
      Summary: Duplicate selector ".task-card", first used at line 412
- [ ] Issue AaDbpHwakXWrHeTEYN01 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: css:S4666 — File: `src/Taskboard.Client/wwwroot/css/site.css` — Line: 1326
      Summary: Duplicate selector ".kanban-column", first used at line 312

### `src/Taskboard.Domain.Shared/Specs/LivingSpecification.cs` (1 issues)
- [ ] Issue AaDbpHeikXWrHeTEYNuJ — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain.Shared/Specs/LivingSpecification.cs` — Line: 11
      Summary: Constructor has 15 parameters, which is greater than the 7 authorized.

### `src/Taskboard.Domain/Agents/AgentRunEvent.cs` (1 issues)
- [ ] Issue AaDbpHtVkXWrHeTEYNzw — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Agents/AgentRunEvent.cs` — Line: 53
      Summary: Constructor has 16 new parameters, which is greater than the 7 authorized.

### `src/Taskboard.Domain/Entities/AiChatThread.cs` (8 issues)
- [ ] Issue AaDbpHrpkXWrHeTEYNzL — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/AiChatThread.cs` — Line: 40
      Summary: Constructor has 9 new parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHrpkXWrHeTEYNzK — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/AiChatThread.cs` — Line: 70
      Summary: Method has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHrpkXWrHeTEYNzM — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/AiChatThread.cs` — Line: 94
      Summary: Method has 9 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHrpkXWrHeTEYNzF — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AiChatThread.cs` — Line: 12
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHrpkXWrHeTEYNzG — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AiChatThread.cs` — Line: 13
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHrpkXWrHeTEYNzH — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AiChatThread.cs` — Line: 14
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHrpkXWrHeTEYNzI — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AiChatThread.cs` — Line: 15
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHrpkXWrHeTEYNzJ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AiChatThread.cs` — Line: 16
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/Chat/ChatMessage.cs` (4 issues)
- [ ] Issue AaDukK4nkK9GZAB4jb_N — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/Chat/ChatMessage.cs` — Line: 35
      Summary: Constructor has 12 new parameters, which is greater than the 7 authorized.
- [ ] Issue AaDukK4nkK9GZAB4jb_L — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Chat/ChatMessage.cs` — Line: 14
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukK4nkK9GZAB4jb_K — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Chat/ChatMessage.cs` — Line: 15
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukK4nkK9GZAB4jb_M — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Chat/ChatMessage.cs` — Line: 16
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` (5 issues)
- [ ] Issue AaDbpHsAkXWrHeTEYNzU — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` — Line: 39
      Summary: Constructor has 13 new parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHsAkXWrHeTEYNzS — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` — Line: 67
      Summary: Method has 13 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHsAkXWrHeTEYNzT — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` — Line: 77
      Summary: Method has 9 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHsAkXWrHeTEYNzR — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` — Line: 14
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHsAkXWrHeTEYNzQ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` — Line: 16
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` (7 issues)
- [ ] Issue AaDbpHtFkXWrHeTEYNzu — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` — Line: 42
      Summary: Constructor has 8 new parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHtFkXWrHeTEYNzt — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` — Line: 69
      Summary: Method has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHtFkXWrHeTEYNzp — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` — Line: 14
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHtFkXWrHeTEYNzo — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` — Line: 15
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHtFkXWrHeTEYNzq — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` — Line: 16
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHtFkXWrHeTEYNzr — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` — Line: 17
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHtFkXWrHeTEYNzs — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` — Line: 19
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/Harness/RunCostMetric.cs` (1 issues)
- [ ] Issue AaDbpHsakXWrHeTEYNze — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/Harness/RunCostMetric.cs` — Line: 45
      Summary: Constructor has 11 new parameters, which is greater than the 7 authorized.

### `src/Taskboard.Domain/Entities/Harness/VerificationReport.cs` (3 issues)
- [ ] Issue AaDbpHsskXWrHeTEYNzh — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/Harness/VerificationReport.cs` — Line: 50
      Summary: Method has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHsskXWrHeTEYNzf — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/VerificationReport.cs` — Line: 12
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHsskXWrHeTEYNzg — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/VerificationReport.cs` — Line: 17
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs` (6 issues)
- [ ] Issue AaDbpHsSkXWrHeTEYNzd — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S107 — File: `src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs` — Line: 73
      Summary: Method has 8 parameters, which is greater than the 7 authorized.
- [ ] Issue AaDbpHsSkXWrHeTEYNzY — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs` — Line: 12
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHsSkXWrHeTEYNza — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs` — Line: 13
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHsSkXWrHeTEYNzZ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs` — Line: 14
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHsSkXWrHeTEYNzb — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs` — Line: 15
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHsSkXWrHeTEYNzc — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs` — Line: 16
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.EntityFrameworkCore/ValueConverters/NullableJsonValueConverter.cs` (1 issues)
- [ ] Issue AaDbpH5UkXWrHeTEYN37 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S2743 — File: `src/Taskboard.EntityFrameworkCore/ValueConverters/NullableJsonValueConverter.cs` — Line: 10
      Summary: A static field in a generic type is not shared among instances of different close constructed types.

### `src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs` (1 issues)
- [ ] Issue AaDbpH0KkXWrHeTEYN2T — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs` — Line: 63
      Summary: Remove this unused method parameter 'sandbox'.

### `src/Taskboard.Integrations/Agents/StreamingProcessRunner.cs` (1 issues)
- [ ] Issue AaDbpH2ykXWrHeTEYN27 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S6966 — File: `src/Taskboard.Integrations/Agents/StreamingProcessRunner.cs` — Line: 65
      Summary: Await WaitForExitAsync instead.

### `src/Taskboard.Integrations/Execution/ExecutableCommand.cs` (2 issues)
- [ ] Issue AaDbpHywkXWrHeTEYN14 — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Execution/ExecutableCommand.cs` — Line: 50
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHywkXWrHeTEYN13 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2486 — File: `src/Taskboard.Integrations/Execution/ExecutableCommand.cs` — Line: 50
      Summary: Handle the exception or explain in a comment why it can be ignored.

### `src/Taskboard.Integrations/GitHub/GitHubService.cs` (3 issues)
- [ ] Issue AaDbpH3zkXWrHeTEYN3Z — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Integrations/GitHub/GitHubService.cs` — Line: 457
      Summary: Remove this unused method parameter 'cancellationToken'.
- [ ] Issue AaDbpH3zkXWrHeTEYN3a — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1172 — File: `src/Taskboard.Integrations/GitHub/GitHubService.cs` — Line: 477
      Summary: Remove this unused method parameter 'repositoryFullName'.
- [ ] Issue AaDbpH3zkXWrHeTEYN3Y — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S4136 — File: `src/Taskboard.Integrations/GitHub/GitHubService.cs` — Line: 449
      Summary: All 'MapToDto' method overloads should be adjacent.

### `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` (6 issues)
- [ ] Issue AaDbpHyMkXWrHeTEYN1s — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` — Line: 89
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpHyMkXWrHeTEYN1t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` — Line: 101
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHyMkXWrHeTEYN1u — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` — Line: 104
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpHyMkXWrHeTEYN1r — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2325 — File: `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` — Line: 18
      Summary: Make 'Validate' a static method.
- [ ] Issue AaDbpHyMkXWrHeTEYN1v — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` — Line: 89
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHyMkXWrHeTEYN1w — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Harness/Security/PathJailValidator.cs` — Line: 96
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Integrations/Harness/Verification/CompilerErrorParser.cs` (2 issues)
- [ ] Issue AaDbpHxpkXWrHeTEYN1a — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1118 — File: `src/Taskboard.Integrations/Harness/Verification/CompilerErrorParser.cs` — Line: 12
      Summary: Add a 'private' constructor or the 'static' keyword to the class declaration.
- [ ] Issue AaDbpHxpkXWrHeTEYN1b — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Integrations/Harness/Verification/CompilerErrorParser.cs` — Line: 22
      Summary: Loop should be simplified by calling Select(match => match.Groups)

### `src/Taskboard.Integrations/Mcp/JsonConfigMerger.cs` (2 issues)
- [ ] Issue AaDbpH37kXWrHeTEYN3b — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Mcp/JsonConfigMerger.cs` — Line: 71
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH37kXWrHeTEYN3c — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Mcp/JsonConfigMerger.cs` — Line: 122
      Summary: Either remove or fill this block of code.

### `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` (13 issues)
- [ ] Issue AaDbpH4NkXWrHeTEYN3n — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 166
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH4NkXWrHeTEYN3h — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 271
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH4NkXWrHeTEYN3k — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 345
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH4NkXWrHeTEYN3q — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 482
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH4NkXWrHeTEYN3r — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 483
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH4NkXWrHeTEYN3t — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 535
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH4NkXWrHeTEYN3o — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1871 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 584
      Summary: Either merge this case with the identical one on line 567 or change one of the implementations.
- [ ] Issue AaDbpH4NkXWrHeTEYN3i — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 227
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH4NkXWrHeTEYN3j — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 265
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH4NkXWrHeTEYN3l — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 300
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH4NkXWrHeTEYN3m — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 339
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH4NkXWrHeTEYN3p — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 366
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH4NkXWrHeTEYN3s — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — Line: 482
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs` (4 issues)
- [ ] Issue AaDbpH4DkXWrHeTEYN3d — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs` — Line: 65
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH4DkXWrHeTEYN3e — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S3358 — File: `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs` — Line: 84
      Summary: Extract this nested ternary operation into an independent statement.
- [ ] Issue AaDbpH4DkXWrHeTEYN3g — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs` — Line: 142
      Summary: Either remove or fill this block of code.
- [ ] Issue AaDbpH4DkXWrHeTEYN3f — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs` — Line: 40
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Mcp/Program.cs` (2 issues)
- [ ] Issue AaDbpH6KkXWrHeTEYN4G — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S1118 — File: `src/Taskboard.Mcp/Program.cs` — Line: 10
      Summary: Add a 'protected' constructor or the 'static' keyword to the class declaration.
- [ ] Issue AaDbpH6KkXWrHeTEYN4H — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1075 — File: `src/Taskboard.Mcp/Program.cs` — Line: 22
      Summary: Refactor your code not to use hardcoded absolute paths or URIs.

### `src/Taskboard.Server/Services/AdminUser.cs` (1 issues)
- [ ] Issue AaDbpHixkXWrHeTEYNui — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S2589 — File: `src/Taskboard.Server/Services/AdminUser.cs` — Line: 82
      Summary: Remove this unnecessary check for null.

### `src/Taskboard.Server/Services/CockpitEventStream.cs` (1 issues)
- [ ] Issue AaDbpHigkXWrHeTEYNug — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Server/Services/CockpitEventStream.cs` — Line: 38
      Summary: Either remove or fill this block of code.

### `src/Taskboard.Server/Services/ManagedJobService.cs` (1 issues)
- [ ] Issue AaDt34ju80awsUzQdyoc — Sonar type: CODE_SMELL — Severity: MAJOR — Rule: csharpsquid:S108 — File: `src/Taskboard.Server/Services/ManagedJobService.cs` — Line: 73
      Summary: Either remove or fill this block of code.

### `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` (6 issues)
- [ ] Issue AaDbpHqZkXWrHeTEYNyw — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S101 — File: `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` — Line: 3
      Summary: Rename interface 'ILLMProvider' to match pascal case naming rules, consider using 'ILlmProvider'.
- [ ] Issue AaDbpHqZkXWrHeTEYNyx — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S101 — File: `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` — Line: 18
      Summary: Rename record 'LLMMessage' to match pascal case naming rules, consider using 'LlmMessage'.
- [ ] Issue AaDbpHqZkXWrHeTEYNyz — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S101 — File: `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` — Line: 23
      Summary: Rename record 'LLMOptions' to match pascal case naming rules, consider using 'LlmOptions'.
- [ ] Issue AaDbpHqZkXWrHeTEYNyy — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S101 — File: `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` — Line: 29
      Summary: Rename record 'LLMResponse' to match pascal case naming rules, consider using 'LlmResponse'.
- [ ] Issue AaDbpHqZkXWrHeTEYNy0 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S101 — File: `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` — Line: 34
      Summary: Rename record 'LLMUsage' to match pascal case naming rules, consider using 'LlmUsage'.
- [ ] Issue AaDbpHqZkXWrHeTEYNy1 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S101 — File: `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` — Line: 39
      Summary: Rename record 'LLMStreamChunk' to match pascal case naming rules, consider using 'LlmStreamChunk'.

### `src/Taskboard.Application.Contracts/GitHub/GitHubBoardColumnExtensions.cs` (2 issues)
- [ ] Issue AaDbpHq4kXWrHeTEYNy9 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Application.Contracts/GitHub/GitHubBoardColumnExtensions.cs` — Line: 126
      Summary: Loops should be simplified using the "FirstOrDefault" LINQ method
- [ ] Issue AaDbpHq4kXWrHeTEYNy8 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Application.Contracts/GitHub/GitHubBoardColumnExtensions.cs` — Line: 143
      Summary: Loops should be simplified using the "Where" LINQ method

### `src/Taskboard.Application.Contracts/GitHub/GitHubBoardGrouper.cs` (1 issues)
- [ ] Issue AaDbpHqukXWrHeTEYNy7 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Application.Contracts/GitHub/GitHubBoardGrouper.cs` — Line: 62
      Summary: Loops should be simplified using the "Where" LINQ method

### `src/Taskboard.Application.Contracts/GitHub/WorkflowRunBadge.cs` (1 issues)
- [ ] Issue AaDbpHrBkXWrHeTEYNy- — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application.Contracts/GitHub/WorkflowRunBadge.cs` — Line: 33
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Application.Contracts/Operations/OperationLog.cs` (2 issues)
- [ ] Issue AaDbpHqGkXWrHeTEYNyt — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2094 — File: `src/Taskboard.Application.Contracts/Operations/OperationLog.cs` — Line: 50
      Summary: Remove this empty class, write its code or make it an "interface".
- [ ] Issue AaDbpHqGkXWrHeTEYNyu — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2094 — File: `src/Taskboard.Application.Contracts/Operations/OperationLog.cs` — Line: 55
      Summary: Remove this empty class, write its code or make it an "interface".

### `src/Taskboard.Application/Agents/AgentEligibilityService.cs` (1 issues)
- [ ] Issue AaDbpHuWkXWrHeTEYN0A — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/Agents/AgentEligibilityService.cs` — Line: 32
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Application/Agents/StaleRunReaper.cs` (1 issues)
- [ ] Issue AaDbpHudkXWrHeTEYN0B — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Application/Agents/StaleRunReaper.cs` — Line: 34
      Summary: Loop should be simplified by calling Select(run => run.Id)

### `src/Taskboard.Application/AiChat/MockLLMProvider.cs` (1 issues)
- [ ] Issue AaDbpHtwkXWrHeTEYNz1 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S101 — File: `src/Taskboard.Application/AiChat/MockLLMProvider.cs` — Line: 5
      Summary: Rename class 'MockLLMProvider' to match pascal case naming rules, consider using 'MockLlmProvider'.

### `src/Taskboard.Application/GitHub/TimelineMetricsService.cs` (2 issues)
- [ ] Issue AaDbpHtokXWrHeTEYNz0 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/GitHub/TimelineMetricsService.cs` — Line: 199
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHtokXWrHeTEYNzz — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/GitHub/TimelineMetricsService.cs` — Line: 266
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Application/Harness/PipelineTemplates.cs` (5 issues)
- [ ] Issue AaDbpHuvkXWrHeTEYN0F — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application/Harness/PipelineTemplates.cs` — Line: 17
      Summary: Define a constant instead of using this literal 'architect' 4 times.
- [ ] Issue AaDbpHuvkXWrHeTEYN0G — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application/Harness/PipelineTemplates.cs` — Line: 21
      Summary: Define a constant instead of using this literal 'builder' 8 times.
- [ ] Issue AaDbpHuvkXWrHeTEYN0H — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application/Harness/PipelineTemplates.cs` — Line: 21
      Summary: Define a constant instead of using this literal 'Builder' 4 times.
- [ ] Issue AaDbpHuvkXWrHeTEYN0I — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application/Harness/PipelineTemplates.cs` — Line: 23
      Summary: Define a constant instead of using this literal 'verifier' 5 times.
- [ ] Issue AaDbpHuvkXWrHeTEYN0J — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Application/Harness/PipelineTemplates.cs` — Line: 23
      Summary: Define a constant instead of using this literal 'Verifier' 4 times.

### `src/Taskboard.Application/Settings/SettingsService.cs` (1 issues)
- [ ] Issue AaDbpHuNkXWrHeTEYNz_ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Application/Settings/SettingsService.cs` — Line: 162
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Blazor/Components/BoardView.razor` (1 issues)
- [ ] Issue AaDbpHoTkXWrHeTEYNyS — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2325 — File: `src/Taskboard.Blazor/Components/BoardView.razor` — Line: 63
      Summary: Make 'GetPrioritySwatchStyle' a static method.

### `src/Taskboard.Blazor/Components/Cockpit/RunTimeline.razor` (1 issues)
- [ ] Issue AaDbpHn1kXWrHeTEYNyE — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1481 — File: `src/Taskboard.Blazor/Components/Cockpit/RunTimeline.razor` — Line: 19
      Summary: Remove the unused local variable 'tool'.

### `src/Taskboard.Blazor/Components/Pages/Gantt.razor` (1 issues)
- [ ] Issue AaDbpHkFkXWrHeTEYNvg — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2325 — File: `src/Taskboard.Blazor/Components/Pages/Gantt.razor` — Line: 224
      Summary: Make 'GetBarClass' a static method.

### `src/Taskboard.Blazor/Layout/MainLayout.razor` (1 issues)
- [ ] Issue AaDbpHodkXWrHeTEYNyT — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3220 — File: `src/Taskboard.Blazor/Layout/MainLayout.razor` — Line: 138
      Summary: Review this call, which partially matches an overload without 'params'. The partial match is 'string

### `src/Taskboard.Blazor/Services/HttpGitHubService.cs` (7 issues)
- [ ] Issue AaDbpHo4kXWrHeTEYNyX — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/HttpGitHubService.cs` — Line: 69
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHo4kXWrHeTEYNyY — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/HttpGitHubService.cs` — Line: 86
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHo4kXWrHeTEYNyZ — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/HttpGitHubService.cs` — Line: 119
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHo4kXWrHeTEYNya — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/HttpGitHubService.cs` — Line: 135
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHo4kXWrHeTEYNyb — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/HttpGitHubService.cs` — Line: 151
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHo4kXWrHeTEYNyc — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/HttpGitHubService.cs` — Line: 184
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHo4kXWrHeTEYNyd — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Blazor/Services/HttpGitHubService.cs` — Line: 249
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Client/wwwroot/js/boot.js` (5 issues)
- [ ] Issue AaDbpHwKkXWrHeTEYN0u — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7758 — File: `src/Taskboard.Client/wwwroot/js/boot.js` — Line: 32
      Summary: Prefer `String#codePointAt()` over `String#charCodeAt()`.
- [ ] Issue AaDbpHwKkXWrHeTEYN0v — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S4138 — File: `src/Taskboard.Client/wwwroot/js/boot.js` — Line: 46
      Summary: Expected a `for-of` loop instead of a `for` loop with this simple iteration.
- [ ] Issue AaDbpHwKkXWrHeTEYN0w — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S4138 — File: `src/Taskboard.Client/wwwroot/js/boot.js` — Line: 58
      Summary: Expected a `for-of` loop instead of a `for` loop with this simple iteration.
- [ ] Issue AaDbpHwKkXWrHeTEYN0x — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7758 — File: `src/Taskboard.Client/wwwroot/js/boot.js` — Line: 59
      Summary: Prefer `String.fromCodePoint()` over `String.fromCharCode()`.
- [ ] Issue AaDbpHwKkXWrHeTEYN0y — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7765 — File: `src/Taskboard.Client/wwwroot/js/boot.js` — Line: 120
      Summary: Use `.includes()`, rather than `.indexOf()`, when checking for existence.

### `src/Taskboard.Client/wwwroot/js/terminal.js` (3 issues)
- [ ] Issue AaDbpHv2kXWrHeTEYN0m — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6582 — File: `src/Taskboard.Client/wwwroot/js/terminal.js` — Line: 12
      Summary: Prefer using an optional chain expression instead, as it's more concise and easier to read.
- [ ] Issue AaDbpHv2kXWrHeTEYN0n — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S6582 — File: `src/Taskboard.Client/wwwroot/js/terminal.js` — Line: 239
      Summary: Prefer using an optional chain expression instead, as it's more concise and easier to read.
- [ ] Issue AaDbpHv2kXWrHeTEYN0o — Sonar type: CODE_SMELL — Severity: MINOR — Rule: javascript:S7747 — File: `src/Taskboard.Client/wwwroot/js/terminal.js` — Line: 299
      Summary: `for…of` can iterate over iterable, it's unnecessary to convert to an array.

### `src/Taskboard.Cloud/Services/CloudflareProxyService.cs` (1 issues)
- [ ] Issue AaDbpH6UkXWrHeTEYN4I — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1450 — File: `src/Taskboard.Cloud/Services/CloudflareProxyService.cs` — Line: 29
      Summary: Remove the field '_apiToken' and declare it as a local variable in the relevant methods.

### `src/Taskboard.Domain.Shared/Json/StringIdJsonConverterFactory.cs` (2 issues)
- [ ] Issue AaDbpHg7kXWrHeTEYNuS — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain.Shared/Json/StringIdJsonConverterFactory.cs` — Line: 18
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHg7kXWrHeTEYNuT — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain.Shared/Json/StringIdJsonConverterFactory.cs` — Line: 37
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain.Shared/Json/StringValueObjectJsonConverterFactory.cs` (2 issues)
- [ ] Issue AaDbpHhxkXWrHeTEYNuU — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain.Shared/Json/StringValueObjectJsonConverterFactory.cs` — Line: 18
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHhxkXWrHeTEYNuV — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain.Shared/Json/StringValueObjectJsonConverterFactory.cs` — Line: 37
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain.Shared/Mcp/AgentMcpConfigMap.cs` (1 issues)
- [ ] Issue AaDbpHgzkXWrHeTEYNuR — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Domain.Shared/Mcp/AgentMcpConfigMap.cs` — Line: 69
      Summary: Define a constant instead of using this literal 'mcpServers' 10 times.

### `src/Taskboard.Domain/Entities/AiChatEvent.cs` (3 issues)
- [ ] Issue AaDbpHrgkXWrHeTEYNzE — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AiChatEvent.cs` — Line: 8
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHrgkXWrHeTEYNzC — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AiChatEvent.cs` — Line: 9
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHrgkXWrHeTEYNzD — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AiChatEvent.cs` — Line: 11
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/AiChatRun.cs` (1 issues)
- [ ] Issue AaDbpHtMkXWrHeTEYNzv — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/AiChatRun.cs` — Line: 8
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/Chat/ChatConversation.cs` (3 issues)
- [ ] Issue AaDukK5QkK9GZAB4jb_R — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Chat/ChatConversation.cs` — Line: 16
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukK5QkK9GZAB4jb_S — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Chat/ChatConversation.cs` — Line: 17
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukK5QkK9GZAB4jb_T — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Chat/ChatConversation.cs` — Line: 18
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/Chat/ChatProvider.cs` (3 issues)
- [ ] Issue AaDukK49kK9GZAB4jb_O — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Chat/ChatProvider.cs` — Line: 12
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukK49kK9GZAB4jb_P — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Chat/ChatProvider.cs` — Line: 13
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDukK49kK9GZAB4jb_Q — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Chat/ChatProvider.cs` — Line: 14
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/CliMetrics/CliDailyUsageAggregate.cs` (1 issues)
- [ ] Issue AaDbpHr4kXWrHeTEYNzP — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/CliMetrics/CliDailyUsageAggregate.cs` — Line: 17
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/CliMetrics/CliMetricSource.cs` (2 issues)
- [ ] Issue AaDbpHrxkXWrHeTEYNzN — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/CliMetrics/CliMetricSource.cs` — Line: 14
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHrxkXWrHeTEYNzO — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/CliMetrics/CliMetricSource.cs` — Line: 15
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/Harness/PipelineStageExecution.cs` (3 issues)
- [ ] Issue AaDbpHsJkXWrHeTEYNzV — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/PipelineStageExecution.cs` — Line: 12
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHsJkXWrHeTEYNzX — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/PipelineStageExecution.cs` — Line: 13
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHsJkXWrHeTEYNzW — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/PipelineStageExecution.cs` — Line: 14
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Domain/Entities/Harness/ProjectMemoryItem.cs` (3 issues)
- [ ] Issue AaDbpHs0kXWrHeTEYNzk — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/ProjectMemoryItem.cs` — Line: 12
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHs0kXWrHeTEYNzi — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/ProjectMemoryItem.cs` — Line: 13
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpHs0kXWrHeTEYNzj — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Domain/Entities/Harness/ProjectMemoryItem.cs` — Line: 14
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.EntityFrameworkCore/ValueConverters/JsonValueConverter.cs` (1 issues)
- [ ] Issue AaDbpH5ckXWrHeTEYN38 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.EntityFrameworkCore/ValueConverters/JsonValueConverter.cs` — Line: 17
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.EntityFrameworkCore/ValueConverters/ListStringValueComparer.cs` (1 issues)
- [ ] Issue AaDqlr5bbPV0wMWtGOm6 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.EntityFrameworkCore/ValueConverters/ListStringValueComparer.cs` — Line: 20
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.EntityFrameworkCore/ValueConverters/StringIdValueConverter.cs` (2 issues)
- [ ] Issue AaDbpH5NkXWrHeTEYN35 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.EntityFrameworkCore/ValueConverters/StringIdValueConverter.cs` — Line: 19
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH5NkXWrHeTEYN36 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.EntityFrameworkCore/ValueConverters/StringIdValueConverter.cs` — Line: 34
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.EntityFrameworkCore/ValueConverters/StringValueObjectConverter.cs` (2 issues)
- [ ] Issue AaDbpH5kkXWrHeTEYN39 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.EntityFrameworkCore/ValueConverters/StringValueObjectConverter.cs` — Line: 19
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH5kkXWrHeTEYN3- — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.EntityFrameworkCore/ValueConverters/StringValueObjectConverter.cs` — Line: 34
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Integrations/Agents/JsonRpcAcpClient.cs` (2 issues)
- [ ] Issue AaDbpH3gkXWrHeTEYN3T — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2737 — File: `src/Taskboard.Integrations/Agents/JsonRpcAcpClient.cs` — Line: 95
      Summary: Add logic to this catch clause or eliminate it and rethrow the exception automatically.
- [ ] Issue AaDbpH3gkXWrHeTEYN3S — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S4136 — File: `src/Taskboard.Integrations/Agents/JsonRpcAcpClient.cs` — Line: 186
      Summary: All 'GetParamsText' method overloads should be adjacent.

### `src/Taskboard.Integrations/Chat/Tools/CodeInterpreterTool.cs` (1 issues)
- [ ] Issue AaDukLvJkK9GZAB4jb_d — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Chat/Tools/CodeInterpreterTool.cs` — Line: 20
      Summary: Define a constant instead of using this literal '{file}' 4 times.

### `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` (5 issues)
- [ ] Issue AaDuh0-EnAunJMVSQPzr — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` — Line: 67
      Summary: Define a constant instead of using this literal 'rowid' 6 times.
- [ ] Issue AaDuh0-EnAunJMVSQPzs — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` — Line: 67
      Summary: Define a constant instead of using this literal 'title' 4 times.
- [ ] Issue AaDuh0-EnAunJMVSQPzt — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` — Line: 67
      Summary: Define a constant instead of using this literal 'created_at' 4 times.
- [ ] Issue AaDuh0-EnAunJMVSQPzu — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` — Line: 67
      Summary: Define a constant instead of using this literal 'last_activity_at' 4 times.
- [ ] Issue AaDuh0-EnAunJMVSQPzv — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` — Line: 67
      Summary: Define a constant instead of using this literal 'model' 4 times.

### `src/Taskboard.Integrations/Harness/GitWorktreeManager.cs` (2 issues)
- [ ] Issue AaDbpHyfkXWrHeTEYN10 — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S3267 — File: `src/Taskboard.Integrations/Harness/GitWorktreeManager.cs` — Line: 128
      Summary: Loops should be simplified using the "Where" LINQ method
- [ ] Issue AaDbpHyfkXWrHeTEYN1z — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S1192 — File: `src/Taskboard.Integrations/Harness/GitWorktreeManager.cs` — Line: 277
      Summary: Define a constant instead of using this literal 'worktree' 4 times.

### `src/Taskboard.Integrations/Harness/Security/SecretScrubber.cs` (1 issues)
- [ ] Issue AaDbpHx7kXWrHeTEYN1d — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2325 — File: `src/Taskboard.Integrations/Harness/Security/SecretScrubber.cs` — Line: 17
      Summary: Make 'Scrub' a static method.

### `src/Taskboard.Integrations/Harness/Verification/DotNetVerificationEngine.cs` (1 issues)
- [ ] Issue AaDbpHxakXWrHeTEYN1Y — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S2325 — File: `src/Taskboard.Integrations/Harness/Verification/DotNetVerificationEngine.cs` — Line: 100
      Summary: Make 'ParseNewestTrx' a static method.

### `src/Taskboard.Integrations/Harness/Verification/VerificationLoop.cs` (1 issues)
- [ ] Issue AaDbpHxikXWrHeTEYN1Z — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Harness/Verification/VerificationLoop.cs` — Line: 48
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.

### `src/Taskboard.Integrations/Jira/JiraService.cs` (2 issues)
- [ ] Issue AaDbpH4fkXWrHeTEYN3x — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Jira/JiraService.cs` — Line: 43
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
- [ ] Issue AaDbpH4fkXWrHeTEYN3y — Sonar type: CODE_SMELL — Severity: MINOR — Rule: csharpsquid:S8970 — File: `src/Taskboard.Integrations/Jira/JiraService.cs` — Line: 47
      Summary: Remove this null-forgiving operator; nullable warnings are disabled here.
