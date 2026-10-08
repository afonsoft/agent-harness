# SPEC-20261008-s5332-preview-url-http-literal

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Remove `http://` literal from chat preview URL builder (SonarQube S5332) |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Blazor Client (AiChat preview tab) |
| Change type | Bugfix / Security |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `devin/20261008-sonarqube-autofix` |
| Status | `Approved` |
| SonarQube | Issue `AaEWogwqS94bQqEFT0sy` — rule `csharpsquid:S5332` (VULNERABILITY, MINOR) |

## 1. User Story

**As a** Harness operator
**I want** the preview URL builder free of hardcoded `http://` literals
**So that** SonarQube's insecure-protocol rule stops flagging the loopback preview path.

**Problem context:** `ChatPreviewTab.razor:145` interpolates `$"http://{host}{raw}"` for bare host/port input. The URL is loopback-only by construction (input is prefixed with `localhost:` unless it already starts with `localhost`/`127.`) — http is the correct scheme for local dev servers — but the literal trips S5332.

## 2. Scope

**In scope:**
- `src/Taskboard.Blazor/Components/AiChat/ChatPreviewTab.razor` — build the scheme via `Uri.UriSchemeHttp` instead of an `"http://"` literal.

**Out of scope:**
- Changing which inputs are accepted or forcing https (loopback dev servers are http).

## 3. Technical Context

`ApplyUrlAsync` normalizes the URL input: `/preview/...` passes through, `x://y` passes through, anything else is assumed loopback and gets the `http://` + `localhost:` prefix. `Uri.UriSchemeHttp` (the `"http"` framework constant) produces the identical string without the flagged literal.

**Files to read before implementing:**
- `src/Taskboard.Blazor/Components/AiChat/ChatPreviewTab.razor` (~line 131–158, `ApplyUrlAsync`)
- `tests/Taskboard.Tests.Unit/Chat/ConversationPreviewTests.cs` (preview URL conventions)

**Files to modify:**
```text
src/Taskboard.Blazor/Components/AiChat/ChatPreviewTab.razor
```

## 4. Requirements

### RF-001: No `http://` literal
- **Description:** the fallback branch must produce the same URL without a `"http://"` string literal — use `$"{Uri.UriSchemeHttp}://…"`.
- **Rules:** byte-identical output for the same inputs; no behavior change.

## 5. Acceptance Criteria

- **Given** input `5021`, **when** normalized, **then** the result is still `http://localhost:5021`.
- **Given** input `localhost:5021/x` or `127.0.0.1:5021`, **when** normalized, **then** output is unchanged.
- SonarQube no longer reports issue `AaEWogwqS94bQqEFT0sy` (S5332).

## 6. Task Plan

1. Replace the `"http://"` literal with `Uri.UriSchemeHttp` interpolation.
2. Build: `dotnet build src/Taskboard.Blazor -c Release`.
3. Check `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`.
