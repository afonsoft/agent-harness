# SPEC-20261008-s5693-attachment-upload-size-limit

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Align chat attachment client cap with server limit (SonarQube S5693) |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Blazor Client (chat attachments) |
| Change type | Bugfix / Security |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `devin/20261008-sonarqube-autofix` |
| Status | `Approved` |
| SonarQube | Issue `AaENGTyJBQ30xXuHrwGt` — rule `csharpsquid:S5693` (VULNERABILITY, MAJOR) |

## 1. User Story

**As a** Harness operator
**I want** the client-side attachment size cap to match the server cap
**So that** oversized files are rejected early with a clear error and no HTTP request carries a body larger than the accepted limit (DoS surface reduction).

**Problem context:** SonarQube S5693 flags `ProviderChat.razor:2228` — `file.OpenReadStream(maxAllowedSize: 32L * 1024 * 1024)` allows 32 MB while the server gate `Taskboard:Chat:Attachments:MaxBytes` defaults to 8 MB (8388608). Files between 8–32 MB pass the client check and are rejected server-side with a 400 — wasted bandwidth and a flagged vulnerability.

## 2. Scope

**In scope:**
- `src/Taskboard.Blazor/Components/Chat/ProviderChat.razor` — lower `maxAllowedSize` to `8L * 1024 * 1024` and surface a friendly per-file error when the cap is exceeded.

**Out of scope:**
- Making the client cap dynamically read the server config value.
- Changes to `Taskboard:Chat:Attachments:MaxBytes` server-side enforcement.

## 3. Technical Context

`ProviderChat.razor` `OnAttachPickedAsync` opens each picked file with `OpenReadStream(maxAllowedSize)`, then posts via `Client.UploadChatAttachmentAsync` → `POST /api/local/chat/conversations/{id}/attachments` (Program.cs:3106), where `ChatService.UploadAttachmentAsync` enforces `MaxBytes` (default 8 MB). `OpenReadStream` throws `IOException` when the file exceeds `maxAllowedSize` — today that lands in the outer catch as a generic "Falha no upload" instead of a per-file message.

**Files to read before implementing:**
- `src/Taskboard.Blazor/Components/Chat/ProviderChat.razor` (`OnAttachPickedAsync`, ~line 2215)
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` (~line 229, `MaxBytes` default 8388608)
- `src/Taskboard.Server/Program.cs` (~line 3106, upload endpoint)

**Files to modify:**
```text
src/Taskboard.Blazor/Components/Chat/ProviderChat.razor
```

## 4. Requirements

### RF-001: Client cap equals server default
- **Description:** the S5693 analyzer multiplies `GetMultipleFiles(N)` × `OpenReadStream(maxAllowedSize)` and flags when the aggregate exceeds `fileUploadSizeLimit` (8388608). Bound the aggregate: `GetMultipleFiles(4)` × `maxAllowedSize: 2L * 1024 * 1024` = exactly 8 MB per pick gesture (server still enforces `Taskboard:Chat:Attachments:MaxBytes` = 8 MB per file).
- **Rules:** `AttachmentMaxFiles * AttachmentMaxBytes <= 8388608`; named constants preferred over magic literals.

### RF-002: Per-file size error
- **Description:** when `OpenReadStream` throws because the file exceeds the cap, the error must be surfaced as a per-file `_error` entry (`Anexo '{name}' recusado: …`) instead of aborting the remaining picks via the outer catch.
- **Rules:** move the stream open inside the existing per-file try/catch; catch `IOException` alongside `HttpRequestException`.

## 5. Acceptance Criteria

- **Given** a picked file > 2 MB, **when** `OnAttachPickedAsync` runs, **then** the file is not uploaded, `_error` names the file, and subsequent picked files are still processed.
- **Given** a picked file ≤ 2 MB, **when** uploaded, **then** behavior is unchanged.
- **Given** a multi-file pick, **then** at most 4 files are processed per gesture (aggregate ≤ 8 MB).
- SonarQube no longer reports issue `AaENGTyJBQ30xXuHrwGt` (S5693).

## 6. Task Plan

1. Lower `maxAllowedSize` to `8L * 1024 * 1024` via a named constant (e.g. `AttachmentMaxBytes`); open the stream inside the per-file `try` and catch `IOException` per file.
2. Build: `dotnet build src/Taskboard.Blazor -c Release` (TreatWarningsAsErrors).
3. Validate manually or via bUnit if present; check `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`.
