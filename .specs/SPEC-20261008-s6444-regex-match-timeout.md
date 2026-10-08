# SPEC-20261008-s6444-regex-match-timeout

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Regex match timeout in chat risk classifier (SonarQube S6444) |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Application (chat tool risk classification) |
| Change type | Bugfix / Security |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `devin/20261008-sonarqube-autofix` |
| Status | `Approved` |
| SonarQube | Issues `AaEWcf5U8iNdX_vWte9c` + `AaEWcf518iNdX_vWte9i` — rule `csharpsquid:S6444` (VULNERABILITY, MINOR ×2) |

## 1. User Story

**As a** Harness operator
**I want** every `Regex.IsMatch` in the risk classifier to carry a match timeout
**So that** a pathological pattern/input combination cannot stall the classification path (ReDoS surface).

**Problem context:** S6444 flags two `Regex.IsMatch` calls without `matchTimeout`: `ChatRiskRules.cs:15` (`ChatRiskRule.Matches`, dynamic rule patterns) and `StaticChatToolRiskClassifier.cs:186` (Windows drive-letter check in `EscapesWorkspace`).

## 2. Scope

**In scope:**
- `src/Taskboard.Application/Chat/ChatRiskRules.cs` — `Matches` gains a bounded timeout.
- `src/Taskboard.Application/Chat/StaticChatToolRiskClassifier.cs` — drive-letter `IsMatch` gains a bounded timeout.

**Out of scope:**
- Converting patterns to `GeneratedRegex` (patterns are runtime data).
- Catching `RegexMatchTimeoutException` (timeout on these inputs/patterns is unreachable; surfacing it is preferable to silently mis-classifying).

## 3. Technical Context

`ChatRiskRule.Matches` runs each rule pattern against tool arguments during `StaticChatToolRiskClassifier.Classify`. `EscapesWorkspace` uses a fixed drive-letter pattern. Both are on the tool-call approval path — an unbounded regex could hang a run.

**Files to read before implementing:**
- `src/Taskboard.Application/Chat/ChatRiskRules.cs` (line 15)
- `src/Taskboard.Application/Chat/StaticChatToolRiskClassifier.cs` (line ~186, `EscapesWorkspace`)
- `tests/Taskboard.Tests.Unit/Chat/ChatRiskClassifierTests.cs` (classifier coverage)

**Files to modify:**
```text
src/Taskboard.Application/Chat/ChatRiskRules.cs
src/Taskboard.Application/Chat/StaticChatToolRiskClassifier.cs
tests/Taskboard.Tests.Unit/Chat/ChatRiskClassifierTests.cs  (new assertions)
```

## 4. Requirements

### RF-001: Timeout on `ChatRiskRule.Matches`
- **Description:** use the 4-arg overload `Regex.IsMatch(value, Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeout)` with a shared bounded timeout (e.g. `TimeSpan.FromMilliseconds(250)` exposed as a `private static readonly TimeSpan`).

### RF-002: Timeout on `EscapesWorkspace` drive-letter check
- **Description:** use `Regex.IsMatch(path, @"^[a-zA-Z]:[\\/]", matchTimeout)` — same timeout value.

## 5. Acceptance Criteria

- **Given** the existing `ChatRiskClassifierTests`, **when** run, **then** all stay green (behavior unchanged for well-formed inputs).
- **Given** `EscapesWorkspace` inputs (relative, rooted-in/outside workspace, `..`, `~`, `spill:`/`attach:`), **when** classified, **then** verdicts unchanged.
- SonarQube no longer reports issues `AaEWcf5U8iNdX_vWte9c` and `AaEWcf518iNdX_vWte9i` (S6444).

## 6. Task Plan

1. Add `matchTimeout` to both call sites (shared const per file is acceptable).
2. Add a unit assertion that `Matches`/`EscapesWorkspace` still behave for representative inputs (existing tests largely cover this — add coverage only for modified lines if missing).
3. `dotnet test tests/Taskboard.Tests.Unit --filter ChatRisk`.
4. Check `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`.
