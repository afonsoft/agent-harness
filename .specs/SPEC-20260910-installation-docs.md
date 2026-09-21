# SPEC-20260910-installation-docs

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `installation-docs` |
| Type | `Docs` |
| Stack | `Docs` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260910-installation-docs` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |

## 1. User Story

**As a** new developer or AI agent setting up `agent-harness`
**I want** a dedicated installation guide in English and Portuguese
**So that** I can clone, build, configure, and run the project without reading source code.

**Problem context:**
The current installation instructions live only as a short "Quick Start" section in `README.md` and `README.pt-br.md`. They do not cover environment variables, troubleshooting, or verification steps in depth. The project has grown to include a server, CLI, MCP server, Blazor UI, and optional GitHub integration, so a dedicated installation document is needed.

## 2. Scope

**In scope:**
- Create `docs/installation.md` (en-us) covering prerequisites, clone, build, test, configuration, run, and verification.
- Create `docs/installation.pt-br.md` (pt-br) as a faithful translation.
- Update `README.md` and `README.pt-br.md` to link to the new guide and keep only a minimal quick-start.
- Document environment variables `TASKBOARD_PORT`, `TASKBOARD_DATA_DIR`, `GITHUB_TOKEN`, and `TASKBOARD_URL`.
- Document how to run the server, the CLI (`taskctl`), and the MCP server.
- Document the `/health` endpoint and how to access the Blazor UI (`/github-board`).
- Add a troubleshooting section for common setup errors.

**Out of scope:**
- Production deployment or cloud setup (see `docs/plugins.md`, `SPEC-006`, `SPEC-010`).
- Advanced Jira/Cloudflare configuration (link to existing docs).
- Legacy React/Vite frontend build instructions.
- IDE-specific setup (VS Code, Rider, etc.).
- Renaming of environment variables (handled by `SPEC-20260910-env-rename`).

## 3. Technical Context

**Where the change happens** (architecture, layers, integrations):
Documentation only. The new files live under `docs/` and README files at repository root. No source code changes except minor README updates.

**Files to read before implementing:**
- `CLAUDE.md`
- `.claude/rules/global-rules.md`
- `README.md`
- `README.pt-br.md`
- `docs/README.md`
- `docs/technologies.md`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Cli/Program.cs`
- `src/Taskboard.Mcp/Program.cs`
- `.github/workflows/dotnet.yml`
- `.specs/SPEC-002-rest-api.md`
- `.specs/SPEC-003-cli.md`
- `.specs/SPEC-004-mcp.md`

**Files to create or modify:**
```text
docs/installation.md
docs/installation.pt-br.md
README.md
README.pt-br.md
docs/README.md
```

## 4. Requirements

### RF-001: English installation guide
- **Description:** `docs/installation.md` must provide a complete installation guide in English (en-us).
- **Rules:** Follow the repository's default language. Use copy-pasteable commands. Keep the document focused on local-first setup.
- **Input → Output:** A new reader with .NET 10 SDK installed → can build and run the project by following the document.

### RF-002: Portuguese installation guide
- **Description:** `docs/installation.pt-br.md` must be a faithful Portuguese (pt-br) translation of `docs/installation.md`.
- **Rules:** Keep technical terms consistent with `README.pt-br.md`. Do not translate CLI output or URLs.

### RF-003: README links
- **Description:** `README.md` and `README.pt-br.md` must link to the new installation guide and keep only a minimal quick-start.
- **Rules:** Do not duplicate content; reference `docs/installation.md` for details.

### RF-004: Prerequisites section
- **Description:** Document all prerequisites with minimum versions.
- **Rules:** Include .NET 10 SDK, Git, optional `dotnet-ef`, and optionally `jq` for CLI JSON examples.

### RF-005: Build and test section
- **Description:** Document the commands to restore, build, and test the solution.
- **Rules:** Use `dotnet restore Taskboard.sln`, `dotnet build Taskboard.sln`, `dotnet test Taskboard.sln`. Mention `--configuration Release` as optional.

### RF-006: Environment variables section
- **Description:** Document `TASKBOARD_PORT`, `TASKBOARD_DATA_DIR`, `GITHUB_TOKEN`, and `TASKBOARD_URL`.
- **Rules:** Use the new variable names agreed in `SPEC-20260910-env-rename`. Explain defaults and when each is needed.
- **Input → Output:** Reader knows how to set port, data directory, GitHub token, and CLI base URL.

### RF-007: Run server section
- **Description:** Document how to start the ASP.NET Core server.
- **Rules:** Command: `dotnet run --project src/Taskboard.Server`. Mention auto-migration and default URL `http://127.0.0.1:47823`.

### RF-008: Run CLI section
- **Description:** Document how to build and run the `taskctl` CLI.
- **Rules:** Command: `dotnet run --project src/Taskboard.Cli -- --help`. Mention `TASKBOARD_URL` override.

### RF-009: Run MCP server section
- **Description:** Document how to run the MCP server for STDIO transport.
- **Rules:** Command: `dotnet run --project src/Taskboard.Mcp`. Refer to `docs/plugins.md` and `SPEC-004` for client configuration.

### RF-010: Verification section
- **Description:** Provide steps to verify the installation.
- **Rules:** Include `dotnet test`, `curl http://127.0.0.1:<port>/health`, and opening `http://127.0.0.1:<port>/github-board`.

### RF-011: Troubleshooting section
- **Description:** Include common setup problems and solutions.
- **Rules:** Cover port in use, missing .NET 10 SDK, SQLite file locked, missing `GITHUB_TOKEN`, and Linux/macOS executable permissions.

**Business rules / invariants:**
- English is the default language; Portuguese is a translation.
- No secrets or default passwords may be printed in the documentation.
- The document must not contradict `SPEC-002`, `SPEC-003`, or `SPEC-004`.
- The document must not be generated until `SPEC-20260910-env-rename` is approved/implemented, because it documents the new variable names.

## 5. API Contract

> Not applicable for this documentation task.

## 6. Acceptance Criteria

- [ ] **Given** `docs/installation.md` exists **when** a new contributor follows it on a clean Linux environment **then** `dotnet test Taskboard.sln` passes and the server responds on `/health`.
- [ ] **Given** `docs/installation.pt-br.md` exists **when** compared to `docs/installation.md` **then** it covers the same sections and commands with equivalent meaning.
- [ ] **Given** `README.md` is updated **when** the quick-start section is read **then** it links to `docs/installation.md` and does not duplicate detailed steps.
- [ ] **Given** `README.pt-br.md` is updated **when** the quick-start section is read **then** it links to `docs/installation.pt-br.md` and does not duplicate detailed steps.
- [ ] **Given** `docs/installation.md` documents environment variables **when** searching for `CODEX_TASKBOARD` **then** no references to the legacy prefix remain.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Reader on Windows | Windows 11 + .NET 10 SDK | Commands work with `powershell`/`cmd` equivalents noted |
| Reader on macOS | macOS + .NET 10 SDK | `export` commands work; paths are POSIX |
| Missing `GITHUB_TOKEN` | No token set | Doc explains Kanban/GitHub board will be empty/error and points to setup |
| Port already in use | `TASKBOARD_PORT=47823` occupied | Doc explains how to set `TASKBOARD_PORT` to another value |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read context files (section 3) and confirm `SPEC-20260910-env-rename` is approved (or implement first if not).
- [x] **T2 — Outline:** define sections for `docs/installation.md` and `docs/installation.pt-br.md`.
- [x] **T3 — Write English guide:** create `docs/installation.md` with all required sections.
- [x] **T4 — Write Portuguese guide:** create `docs/installation.pt-br.md` as a faithful translation.
- [x] **T5 — Update READMEs:** shorten quick-start in `README.md` and `README.pt-br.md` and add links.
- [x] **T6 — Update docs index:** add `docs/installation.md` link to `docs/README.md`.
- [x] **T7 — Verification:** follow the guide in a clean environment and confirm `dotnet build`, `dotnet test`, and server `/health` work.
- [x] **T8 — Link and spell check:** run markdown lint and verify internal links.
- [x] **T9 — Done + PR:** fill DoD, set `Status = Done`, and open the PR.

**7.1 Validation strategy by type/stack**

| Type / Stack | Required evidence |
| --- | --- |
| **Docs** | Markdown lint, link checking, spell check, peer review, and a successful smoke test following the documented steps. |

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** never commit to `main`, `master` or `develop`. Use `feature/devin-20260910-installation-docs`.
- **Workflows:** do not modify `.github/workflows/` (protected by branch rule).
- **Registries:** use only approved corporate/organization registries — never public registries unless explicitly authorized.
- **API headers:** not applicable.
- **Security:** do not log PII/tokens/identifiers; no `.env` in commit; do not print default passwords in docs.
- **Scope:** do not invent requirements or expand scope. Stop and ask on ambiguity.
- **Architecture:** no business logic in controllers/components; domain does not access infrastructure.

## 9. Definition of Done

> Filled **during and after implementation**, not at approval time.

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests or equivalent validation evidence for the stack (section 7.1).
- [x] Edge cases handled.
- [x] Build, lint and tests / docs lint pass locally; minimum coverage or equivalent quality gate reached.
- [x] Guardrails in section 8 respected.
- [x] Logs contain no PII/tokens; errors use the organization's generic error format.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/devin-20260910-installation-docs`.

## Open Questions / Pending Ambiguity

- [x] Is the backward-compatible fallback for `CODEX_*` variables required? (Depends on `SPEC-20260910-env-rename` decision.) → **Decisão: breaking change limpo, documentado na doc.**
- [x] Should the documentation include Docker setup or keep it strictly .NET SDK based? → **Decisão: manter .NET SDK based; Docker fora de escopo.**
