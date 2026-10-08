# SPEC-20261008-s1751-single-iteration-loop

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Replace single-iteration loop in ChatAttachmentStore.Resolve (SonarQube S1751) |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Application.Contracts (chat attachment store) |
| Change type | Bugfix |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `devin/20261008-sonarqube-autofix` |
| Status | `Approved` |
| SonarQube | Issue `AaENGUdGBQ30xXuHrwG0` — rule `csharpsquid:S1751` (BUG, MAJOR) |

## 1. User Story

**As a** Harness maintainer
**I want** `ChatAttachmentStore.Resolve` to express "first match or null" directly
**So that** the code doesn't contain a loop that unconditionally returns on its first iteration.

**Problem context:** S1751 flags `ChatAttachmentStore.cs:63` — a `foreach` over `Directory.EnumerateFiles(Root, "{id}.*")` that returns inside the body, making the loop shape misleading (it can never iterate twice).

## 2. Scope

**In scope:**
- `src/Taskboard.Application.Contracts/Chat/ChatAttachmentStore.cs` — `Resolve` uses `FirstOrDefault` + null check instead of the loop.

**Out of scope:** anything else in the store.

## 3. Technical Context

`Resolve(attachmentId)` jail-checks the id, then searches `Root` for `{id}.*` (extension unknown at save time). `EnumerateFiles` + `FirstOrDefault` preserves semantics exactly, including laziness.

**Files to read before implementing:**
- `src/Taskboard.Application.Contracts/Chat/ChatAttachmentStore.cs` (~lines 47–67)
- `tests/Taskboard.Tests.Unit/Chat/ChatAttachmentsFeedbackTests.cs` (`Dado_Bytes_Quando_Save_Entao_CriaArquivoEResolvePorId`, `Dado_IdInexistente_Quando_Resolve_Entao_Null`)

**Files to modify:**
```text
src/Taskboard.Application.Contracts/Chat/ChatAttachmentStore.cs
```

## 4. Requirements

### RF-001: First-match-or-null
- **Description:** `var file = Directory.EnumerateFiles(Root, $"{attachmentId}.*").FirstOrDefault();` then `return file is null ? null : (file, MimeForExtension(Path.GetExtension(file)));`
- **Rules:** identical results for existing/missing/invalid ids; LINQ via implicit usings if enabled (check project).

## 5. Acceptance Criteria

- **Given** `ChatAttachmentsFeedbackTests`, **when** run, **then** all Resolve-related tests stay green.
- SonarQube no longer reports `AaENGUdGBQ30xXuHrwG0` (S1751).

## 6. Task Plan

1. Replace the loop with `FirstOrDefault`.
2. `dotnet test tests/Taskboard.Tests.Unit --filter ChatAttachments`.
3. Check `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`.
