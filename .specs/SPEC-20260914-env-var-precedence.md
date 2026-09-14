# SPEC-20260914-env-var-precedence

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `env-var-precedence` |
| Type | `Bugfix` |
| Stack | `.NET` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/{AgentLLM}-20260914-env-var-precedence` |
| Ticket | `GAP-implementation-env-var-precedence` |
| Status | `Draft` |

## 1. User Story

**As a** Taskboard operator
**I want** `TASKBOARD_PORT`, `TASKBOARD_DATA_DIR` and `TASKBOARD_ADMIN_*` environment variables to override `appsettings.json`
**So that** the documented configuration mechanism actually works.

**Problem context:**
`TaskboardEnvironment.GetPort()` and `GetDataDir()` read `_configuration["Taskboard:*"]` **first**, falling back to `TASKBOARD_*` env vars only when the config key is absent. Since `appsettings.json` always ships `Taskboard:Port` (`47823`) and `Taskboard:DataDir` (`.data`), the env vars are **dead code** — `TASKBOARD_PORT=8080 dotnet run` still binds 47823 (reproduced during gap-analysis). `AdminUser.CreateFromConfiguration` has the same inverted order: `Admin:Username` (always `admin` in appsettings) beats `TASKBOARD_ADMIN_USERNAME`. `docs/installation.md:64-74,106,175` documents the env vars as the override mechanism.

## 2. Scope

**In scope:**
- `TaskboardEnvironment.GetPort()`, `GetDataDir()`, `GetServerUrls()` — env var checked **before** `Taskboard:*` config.
- `AdminUser.CreateFromConfiguration` — `TASKBOARD_ADMIN_USERNAME`/`TASKBOARD_ADMIN_PASSWORD` checked before `Admin:*` config.
- Unit tests covering precedence in `TaskboardEnvironmentTests` and `AdminUserTests`.

**Out of scope:**
- `ASPNETCORE_URLS` handling in `GetServerUrls()` (already env-first; keep).
- Renaming variables or adding new ones.

## 3. Technical Context

**Files to read before implementing:**
- `src/Taskboard.Application.Contracts/Configuration/TaskboardEnvironment.cs` (lines 30-58, 75-79)
- `src/Taskboard.Server/Services/AdminUser.cs` (lines 92-97)
- `src/Taskboard.Server/appsettings.json`
- `tests/Taskboard.Tests.Unit/Configuration/TaskboardEnvironmentTests.cs`
- `tests/Taskboard.Tests.Unit/AdminUserTests.cs`
- `docs/installation.md` / `docs/installation.pt-br.md`

**Files to create or modify:**
```text
src/Taskboard.Application.Contracts/Configuration/TaskboardEnvironment.cs
src/Taskboard.Server/Services/AdminUser.cs
tests/Taskboard.Tests.Unit/Configuration/TaskboardEnvironmentTests.cs
tests/Taskboard.Tests.Unit/AdminUserTests.cs
```

## 4. Requirements

### RF-001: Env vars win over appsettings
- **Description:** When `TASKBOARD_PORT`/`TASKBOARD_DATA_DIR` are set, they must be used even if `Taskboard:*` keys exist in configuration.
- **Rules:** precedence `env var` → `config key` → built-in default.
- **Input → Output:** `TASKBOARD_PORT=8080` + `Taskboard:Port=47823` → server binds 8080.

### RF-002: Admin seeding honors env vars
- **Description:** `TASKBOARD_ADMIN_USERNAME`/`TASKBOARD_ADMIN_PASSWORD` take precedence over `Admin:*` config when seeding `admin.json`.
- **Rules:** once `admin.json` exists it remains source of truth (SPEC-20260911-admin-change-password).

## 6. Acceptance Criteria

- [ ] **Given** `Taskboard:Port=47823` in appsettings and `TASKBOARD_PORT=8080` exported **when** the server starts **then** it binds `http://127.0.0.1:8080`.
- [ ] **Given** `TASKBOARD_DATA_DIR=/tmp/x` exported **when** the server starts **then** `taskboard.sqlite` is created under `/tmp/x`.
- [ ] **Given** `TASKBOARD_ADMIN_USERNAME=ops` exported **when** `admin.json` does not exist **then** the seeded username is `ops`.
- [ ] **Given** no env vars **when** the server starts **then** behavior is unchanged (appsettings values apply).
- [ ] `dotnet build` + `dotnet test` green with `TreatWarningsAsErrors`.

## 7. Task Plan

- [ ] **T1 — Discovery:** re-read `TaskboardEnvironment`, `AdminUser`, existing tests.
- [ ] **T2 — Implementation:** invert precedence (env first) in both classes.
- [ ] **T3 — Tests:** add `Dado_Quando_Entao` unit tests for each precedence rule.
- [ ] **T4 — Validation:** `dotnet build` + `dotnet test`.
- [ ] **T5 — Done + PR.**

## 9. Definition of Done

- [ ] Env var precedence matches `docs/installation.md` contract.
- [ ] Tests cover all three getters + admin seeding.
- [ ] Build and tests green.
