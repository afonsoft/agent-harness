# SPEC-20261008-s8949-propagate-cancellation-token

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Propagate CancellationToken in preview proxy + PR card cache (SonarQube S8949) |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Server (preview proxy) + Application (ConversationGitService) |
| Change type | Bugfix |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `devin/20261008-sonarqube-autofix` |
| Status | `Approved` |
| SonarQube | Issues `AaEWogqUS94bQqEFT0sw`, `AaEWogqUS94bQqEFT0sx`, `AaEWjNbZ2OmXm3WLQGP1` — rule `csharpsquid:S8949` (BUG, MAJOR ×3) |

## 1. User Story

**As a** Harness operator
**I want** request-abort tokens propagated to every async call on the flagged paths
**So that** disconnected clients/cancelled runs actually stop the work instead of leaving writes and cache fills running.

**Problem context:** S8949 flags three call sites that ignore an available token: `ChatPreviewProxy.ForwardAsync` `Response.WriteAsync` ×2 (`context.RequestAborted` not passed) and `ConversationGitService.GetPrCardAsync` `cache.GetOrCreateAsync` (`cancellationToken` not passed to the HybridCache call).

## 2. Scope

**In scope:**
- `src/Taskboard.Server/Chat/ChatPreviewProxy.cs` — lines 80, 108: pass `context.RequestAborted` to `WriteAsync`.
- `src/Taskboard.Application/Chat/ConversationGitService.cs` — line ~297: pass `cancellationToken` to `HybridCache.GetOrCreateAsync`.

**Out of scope:** other unflagged token gaps (Sonar reported only these).

## 3. Technical Context

`ForwardAsync` already propagates `context.RequestAborted` to `SendAsync` and `CopyToAsync`; the two error-path `WriteAsync` calls are the gap. `GetPrCardAsync` passes `cancellationToken` into the factory lambda but not into `GetOrCreateAsync` itself (5th parameter).

**Files to read before implementing:**
- `src/Taskboard.Server/Chat/ChatPreviewProxy.cs` (`ForwardAsync`)
- `src/Taskboard.Application/Chat/ConversationGitService.cs` (`GetPrCardAsync`, ~line 287–305)
- `tests/Taskboard.Tests.Unit/Chat/ConversationPreviewTests.cs`

**Files to modify:**
```text
src/Taskboard.Server/Chat/ChatPreviewProxy.cs
src/Taskboard.Application/Chat/ConversationGitService.cs
```

## 4. Requirements

### RF-001: Proxy error writes honor RequestAborted
- **Description:** both `context.Response.WriteAsync` calls in `ForwardAsync` take `context.RequestAborted` as the cancellation token.

### RF-002: HybridCache call honors the caller token
- **Description:** `cache.GetOrCreateAsync(key, factory, options, cancellationToken: cancellationToken)` in `GetPrCardAsync`.

## 5. Acceptance Criteria

- **Given** a cancelled/aborted request hitting the 400/502 write paths, **when** `ForwardAsync` runs, **then** writes observe the token (compiles with the token param; no new warnings under TreatWarningsAsErrors).
- **Given** `GetPrCardAsync` is cancelled before the cache fill, **when** called, **then** `GetOrCreateAsync` observes the token.
- SonarQube no longer reports `AaEWogqUS94bQqEFT0sw`, `AaEWogqUS94bQqEFT0sx`, `AaEWjNbZ2OmXm3WLQGP1` (S8949).

## 6. Task Plan

1. Add the token argument at the three call sites.
2. `dotnet build Taskboard.sln -c Release` (TreatWarningsAsErrors catches signature mistakes).
3. Run existing tests touching the proxy/classifier; check `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`.
