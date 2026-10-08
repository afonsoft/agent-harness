# SPEC-20261008-s2583-dead-code-paths

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Remove provably-dead conditions (SonarQube S2583) |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Blazor (ChatGitBar) + Server (CacheInspectorService) |
| Change type | Bugfix |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `devin/20261008-sonarqube-autofix` |
| Status | `Approved` |
| SonarQube | Issues `AaEWjL3G2OmXm3WLQGPt` + `AaEOBB1V4CMLCseAO3WX` — rule `csharpsquid:S2583` (BUG, MAJOR ×2) |

## 1. User Story

**As a** Harness maintainer
**I want** dead conditional branches removed
**So that** the code doesn't advertise code paths that can never execute (misleading logic → latent bugs).

**Problem context:** S2583 flags two spots. `ChatGitBar.razor:137` — `ConversationId is null ? null : await …` where the null branch is unreachable (the early return at line ~130 already exits on null). `CacheInspectorService.cs:76` — `instanceName ?? string.Empty` inside `if (redisConfigured && …)` where `instanceName` is provably non-null (lines ~51–54 guarantee it when `redisConfigured`).

## 2. Scope

**In scope:**
- `src/Taskboard.Blazor/Components/Chat/ChatGitBar.razor` — drop the dead null ternary (and the now-redundant `!` on `ConversationId` at line ~138).
- `src/Taskboard.Server/Services/CacheInspectorService.cs` — replace the dead `??` with the null-forgiving assertion `instanceName!` (the compiler cannot correlate `redisConfigured` → non-null `instanceName`).

**Out of scope:** restructuring the configuration-read block beyond the flagged lines.

## 3. Technical Context

- `ChatGitBar.OnParametersSetAsync`: `if (ConversationId is null || …) return;` makes `ConversationId` not-null afterwards; the ternary's null arm is dead.
- `CacheInspectorService.GetStatsAsync`: when `redisConfigured` is true, `instanceName` is either already non-whitespace or set to `"harness:"`; inside the `redisConfigured && redis is not null` branch the `??` never engages. `string.IsNullOrWhiteSpace` is `[NotNullWhen(false)]`-annotated, but the compiler can't prove the correlation across the two conditions, so `!` is required instead of plain removal.

**Files to read before implementing:**
- `src/Taskboard.Blazor/Components/Chat/ChatGitBar.razor` (~lines 128–140)
- `src/Taskboard.Server/Services/CacheInspectorService.cs` (~lines 40–87)
- `tests/Taskboard.Tests.Unit/Server/CacheInspectorServiceTests.cs`

**Files to modify:**
```text
src/Taskboard.Blazor/Components/Chat/ChatGitBar.razor
src/Taskboard.Server/Services/CacheInspectorService.cs
```

## 4. Requirements

### RF-001: ChatGitBar loads status unconditionally post-guard
- **Description:** `_status = await Client.GetChatGitStatusAsync(ConversationId);` — no ternary; `GetChatBranchesAsync(ConversationId)` without `!` if the compiler proves non-null.

### RF-002: CacheInspectorService passes instanceName without dead coalesce
- **Description:** `ProbeRedisCachedAsync(redis, instanceName!, cancellationToken)` — with a short comment stating non-null is guaranteed by the `redisConfigured` normalization above.

## 5. Acceptance Criteria

- **Given** existing `CacheInspectorServiceTests` (memory provider, redis-configured, probe caching), **when** run, **then** all stay green.
- **Given** a null `ConversationId`, **when** `OnParametersSetAsync` runs, **then** it still early-returns before loading.
- `dotnet build` clean under TreatWarningsAsErrors.
- SonarQube no longer reports `AaEWjL3G2OmXm3WLQGPt` and `AaEOBB1V4CMLCseAO3WX` (S2583).

## 6. Task Plan

1. Apply the two edits; verify no CS86xx warnings (build Release).
2. Run `dotnet test tests/Taskboard.Tests.Unit --filter CacheInspector`.
3. Check `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`.
