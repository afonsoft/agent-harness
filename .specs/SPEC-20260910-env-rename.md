# SPEC-20260910-env-rename

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `env-rename` |
| Type | `Refactor` |
| Stack | `.NET` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260910-env-rename` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |

## 1. User Story

**As a** developer and operator of `agent-harness`
**I want** environment variables to use the `TASKBOARD_` prefix instead of `CODEX_TASKBOARD_`
**So that** configuration is shorter, consistent, and free of legacy Codex branding.

**Problem context:**
The codebase still references legacy `CODEX_TASKBOARD_PORT` and `CODEX_TASKBOARD_DATA_DIR` environment variables. The new installation documentation will document `TASKBOARD_PORT` and `TASKBOARD_DATA_DIR`. If the code is not updated first, the documentation will be inconsistent with the runtime behavior.

## 2. Scope

**In scope:**
- Rename `CODEX_TASKBOARD_PORT` to `TASKBOARD_PORT` everywhere it is read.
- Rename `CODEX_TASKBOARD_DATA_DIR` to `TASKBOARD_DATA_DIR` everywhere it is read.
- Update `install.sh` to emit the new variable names.
- Update `.specs/SPEC-002-rest-api.md` to reference the new variable names.
- Explicitly document the breaking change (no fallback to `CODEX_*` variables).
- Update any inline comments, logs, or documentation strings that mention the old names.

**Out of scope:**
- Adding new environment variables.
- Changing ports, defaults, or data directory semantics.
- Migrating user `.env` files automatically.
- Modifying CI/CD workflows.

## 3. Technical Context

**Where the change happens** (architecture, layers, integrations):
Environment variable reads are scattered across the Server host configuration, EF Core design-time factory, and the install script. The change is a mechanical rename with a documented breaking change (no fallback).

**Files to read before implementing:**
- `CLAUDE.md`
- `.claude/rules/global-rules.md`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.EntityFrameworkCore/Data/TaskboardDbContextFactory.cs`
- `install.sh`
- `.specs/SPEC-002-rest-api.md`

**Files to create or modify:**
```text
src/Taskboard.Server/Program.cs
src/Taskboard.EntityFrameworkCore/Data/TaskboardDbContextFactory.cs
install.sh
.specs/SPEC-002-rest-api.md
```

## 4. Requirements

### RF-001: Rename server port variable
- **Description:** `src/Taskboard.Server/Program.cs` must read `TASKBOARD_PORT` instead of `CODEX_TASKBOARD_PORT`.
- **Rules:** Keep the same default value `47823`. No fallback to `CODEX_TASKBOARD_PORT`.
- **Input → Output:** `TASKBOARD_PORT=8080` → server listens on `8080`; `CODEX_TASKBOARD_PORT=9000` alone → server uses default `47823`.

### RF-002: Rename data directory variable
- **Description:** `src/Taskboard.Server/Program.cs` and `src/Taskboard.EntityFrameworkCore/Data/TaskboardDbContextFactory.cs` must read `TASKBOARD_DATA_DIR` instead of `CODEX_TASKBOARD_DATA_DIR`.
- **Rules:** Keep the same default (`.data` under content root / current directory). No fallback to `CODEX_TASKBOARD_DATA_DIR`.
- **Input → Output:** `TASKBOARD_DATA_DIR=/var/taskboard` → SQLite file created at `/var/taskboard/taskboard.sqlite`; `CODEX_TASKBOARD_DATA_DIR=/old` alone → default `.data` is used.

### RF-003: Update install script
- **Description:** `install.sh` must export `TASKBOARD_DATA_DIR` instead of `CODEX_TASKBOARD_DATA_DIR` in the generated environment file.
- **Rules:** Do not change the data directory path logic. Update only the exported variable name.

### RF-004: Update existing spec references
- **Description:** `.specs/SPEC-002-rest-api.md` must reference `TASKBOARD_PORT` and `TASKBOARD_DATA_DIR` instead of the `CODEX_*` names.
- **Rules:** Preserve the semantic meaning of the sentences.

**Business rules / invariants:**
- Defaults must remain unchanged (`47823` and `.data`).
- No fallback to `CODEX_*` variables; the breaking change must be documented.
- Secrets/credentials (`TASKBOARD_ADMIN_PASSWORD`) must not be affected.

## 5. API Contract

> Not applicable for this refactor. Public HTTP contracts remain unchanged.

## 6. Acceptance Criteria

- [ ] **Given** `TASKBOARD_PORT=8080` is set **when** the server starts **then** it listens on `http://127.0.0.1:8080`.
- [ ] **Given** `TASKBOARD_DATA_DIR=/tmp/taskboard-data` is set **when** the server starts **then** `taskboard.sqlite` is created under `/tmp/taskboard-data`.
- [ ] **Given** `dotnet ef migrations add` is run **when** `TASKBOARD_DATA_DIR` is set **then** the design-time factory uses that directory.
- [ ] **Given** `install.sh` is executed **when** the environment file is generated **then** it exports `TASKBOARD_DATA_DIR` and not `CODEX_TASKBOARD_DATA_DIR`.
- [ ] **Given** the repository is built **when** `dotnet build Taskboard.sln` runs **then** no warnings or errors related to the old variables remain.
- [ ] **Given** `CODEX_TASKBOARD_PORT=9000` is set **when** the server starts **then** it listens on default `47823` (breaking change).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Legacy variable still set | `CODEX_TASKBOARD_PORT=9000` with no `TASKBOARD_PORT` | Uses default `47823` (breaking change) |
| Both old and new variables set | `TASKBOARD_PORT=8080` and `CODEX_TASKBOARD_PORT=9000` | New variable wins (`8080`) |
| Empty new variable | `TASKBOARD_PORT=` | Treated as unset and uses default `47823` |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** confirm all occurrences of `CODEX_TASKBOARD_PORT` and `CODEX_TASKBOARD_DATA_DIR` in the repository.
- [x] **T2 — Update README/spec docs:** document the breaking change (no `CODEX_*` fallback).
- [x] **T3 — Refactor:** rename variables in `Program.cs`, `TaskboardDbContextFactory.cs`, `install.sh`, and `.specs/SPEC-002-rest-api.md`.
- [x] **T4 — Verification:** run `dotnet build Taskboard.sln` and `dotnet test Taskboard.sln`.
- [x] **T5 — Smoke test:** start the server with `TASKBOARD_PORT` and `TASKBOARD_DATA_DIR` set and verify `/health` responds.
- [x] **T6 — Done + PR:** fill DoD, set `Status = Done`, and open the PR.

**7.1 Validation strategy by type/stack**

| Type / Stack | Required evidence |
| --- | --- |
| **.NET** | `dotnet build` passes, `dotnet test` passes, no references to `CODEX_TASKBOARD_*` remain in `src/` or `install.sh`. |

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** never commit to `main`, `master` or `develop`. Use `feature/devin-20260910-env-rename`.
- **Workflows:** do not modify `.github/workflows/` (protected by branch rule).
- **Registries:** use only approved corporate/organization registries — never public registries unless explicitly authorized.
- **API headers:** not applicable.
- **Security:** do not log PII/tokens/identifiers; no `.env` in commit.
- **Scope:** do not invent requirements or expand scope. Stop and ask on ambiguity.
- **Architecture:** no business logic in controllers/components; domain does not access infrastructure.

## 9. Definition of Done

> Filled **during and after implementation**, not at approval time.

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests or equivalent validation evidence for the stack (section 7.1).
- [x] Edge cases handled.
- [x] Build, lint and tests pass locally; minimum coverage or equivalent quality gate reached.
- [x] Guardrails in section 8 respected.
- [x] Logs contain no PII/tokens; errors use the organization's generic error format.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/devin-20260910-env-rename`.

## Open Questions / Pending Ambiguity

- [x] Should the new variables fall back to the old `CODEX_*` names for backward compatibility, or is a clean breaking change acceptable? → **Decisão: breaking change limpo (opção A).**
