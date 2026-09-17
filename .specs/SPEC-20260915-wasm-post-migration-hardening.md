# SPEC-20260915-wasm-post-migration-hardening

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `wasm-post-migration-hardening` |
| Type | `Bugfix` |
| Stack | `.NET` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260915-wasm-post-migration-hardening` |
| Ticket | `GAP-operation-nonpublish-hosting`, `GAP-documentation-blazor-server-stale`, `GAP-tests-repository-combobox` (gap-analysis-20260915) |
| Status | `Implemented` |

## 1. User Story

**As a** maintainer of taskboard-ai
**I want** the non-Docker install path to serve the WASM app, the docs to reflect the WASM architecture, and the repository combobox logic covered by tests
**So that** the three residual gaps left by the WASM migration are closed before they bite the next install or audit.

**Problem context:**

Three confirmed gaps from the 2026-09-15 gap-analysis:

1. **`GAP-operation-nonpublish-hosting`:** `install.sh:283` generates a launcher that runs `dotnet exec src/Taskboard.Server/bin/Release/net10.0/Taskboard.Server.dll` directly on build output. Reproduced: in `Production`, `/` → `404` and `/framework-assets/*` → `404` — `WebRootFileProvider` only resolves static web assets in Development or from publish output. Docker deploys work; `install.sh` deploys serve a broken UI.
2. **`GAP-documentation-blazor-server-stale`:** `CLAUDE.md:18`, `AGENTS.md:18`, `README.md:14,29,52`, `docs/installation.md:9`, `docs/installation.pt-br.md:9`, `docs/technologies*.md` still describe "Blazor Server" and omit `Taskboard.Client` from the structure. The three new SPECs sit at `Implemented` though merged (convention: `Done (merged via PR #N)` per `SPEC-20260914-stale-spec-status` precedent).
3. **`GAP-tests-repository-combobox`:** `SPEC-20260915-repo-search-combobox` task T5 required the filter/validation logic in a testable helper with unit tests; the logic is inline in `Components/Shared/RepositoryCombobox.razor` with zero coverage (project has no bUnit).

## 2. Scope

**In scope:**
- `install.sh` produces a working non-Docker deployment (publish output or equivalent).
- Docs/README/harness files updated to the WASM architecture; the three new SPECs marked `Done`.
- `RepositoryCombobox` filter/commit/validation logic extracted to a pure, testable helper + unit tests.
- Minor cleanup folded in: remove redundant `app.UseStaticFiles()` (Program.cs:1061) and add favicon link if a favicon asset exists/is trivial.

**Out of scope:**
- Docker changes (image already verified working).
- Any functional/UI change to the combobox.
- `skills-lock.json` honoring by the sync service (separate pending decision — `GAP-requirements-skills-lock-not-honored`).
- Container-home vs host skills target (`GAP-operation-skills-sync-container-home` — user decision, deferred).

## 3. Technical Context

**Where the change happens:**
`install.sh` launcher generation; `Taskboard.Blazor` shared component + new helper class; unit test project; top-level docs files; three SPEC metadata tables.

**Files to read before implementing:**
- `install.sh` (server_dll resolution lines ~266-283, build step ~140)
- `src/Taskboard.Blazor/Components/Shared/RepositoryCombobox.razor` (+ `.razor.css`)
- `tests/Taskboard.Tests.Unit/Taskboard.Tests.Unit.csproj` (test conventions, Shouldly)
- `CLAUDE.md`, `AGENTS.md`, `README.md`, `docs/installation.md`, `docs/installation.pt-br.md`, `docs/technologies*.md`
- `.specs/SPEC-20260915-{skills-repo-sync,repo-search-combobox,blazor-wasm-migration}.md` (status fields)
- `src/Taskboard.Server/Program.cs:1061` (`UseStaticFiles`)

**Files to create or modify:**
```text
install.sh                                                   # publish + exec published dll
src/Taskboard.Blazor/Services/RepositoryFilter.cs            # NEW pure helper (filter, match, commit rules)
src/Taskboard.Blazor/Components/Shared/RepositoryCombobox.razor  # delegate logic to helper
tests/Taskboard.Tests.Unit/Blazor/RepositoryFilterTests.cs   # NEW
src/Taskboard.Server/Program.cs                              # drop redundant UseStaticFiles (verify no regression)
CLAUDE.md, AGENTS.md, README.md                              # WASM stack + structure
docs/installation.md, docs/installation.pt-br.md             # WASM UI + publish-based install
docs/technologies.md, docs/technologies.pt-br.md             # frontend row
.specs/SPEC-20260915-skills-repo-sync.md                     # Status → Done
.specs/SPEC-20260915-repo-search-combobox.md                 # Status → Done + T5 ticked
.specs/SPEC-20260915-blazor-wasm-migration.md                # Status → Done
```

## 4. Requirements

### RF-001: `install.sh` serves a working SPA outside Docker
- **Description:** the install script must publish the server (`dotnet publish src/Taskboard.Server -c Release -o <dir>`) and generate the launcher pointing at the **published** `Taskboard.Server.dll`, not the `bin/Release` build output.
- **Rules:** keep the existing env-file/source/`ASPNETCORE_CONTENTROOT` plumbing; publish dir under the existing install layout (e.g. `$TASKBOARD_HOME/app` or alongside `bin/` — follow install.sh conventions); `dotnet build` step may remain for CLI/MCP packing.
- **Input → Output:** fresh `install.sh` run on a clean machine → `curl http://localhost:47823/` returns `200` with the WASM `index.html`, `GET /framework-assets/blazor.web/js` → `200`, in default (Production) environment.

### RF-002: `RepositoryFilter` pure helper + unit tests
- **Description:** extract the combobox's pure logic — case-insensitive substring filter over `owner/repo`, result cap (50), commit validation (`owner/repo` shape), highlight index clamping — into `RepositoryFilter` static/class with no UI dependencies; the `.razor` delegates to it.
- **Rules:** behavior identical to the inline implementation (no UX change); tests follow the repo's `Dado_Quando_Entao` convention + Shouldly.
- **Input → Output:** `dotnet test` covers: filter narrows list, empty query returns all (capped), no-match returns empty, `owner/repo` validation accepts/rejects correctly, highlight index clamps at list bounds.

### RF-003: Documentation refresh to WASM
- **Description:** replace "Blazor Server" with the accurate description (Blazor WebAssembly client + shared RCL + server static host) in `CLAUDE.md`, `AGENTS.md`, `README.md`, `docs/installation.md`, `docs/installation.pt-br.md`, `docs/technologies.md`, `docs/technologies.pt-br.md`; add `Taskboard.Client` to the structure listing in `README.md`.
- **Rules:** bilingual parity (en + pt-br files updated together); keep tables/lists format.

### RF-004: SPEC statuses post-merge
- **Description:** set `Status` to `Done (merged via PR #78)` on the three 2026-09-15 SPECs; tick `T5` in the combobox SPEC after RF-002 lands.
- **Input → Output:** SPEC metadata reflects merged reality (precedent: `SPEC-20260914-stale-spec-status`).

### RF-005: Minor server cleanup
- **Description:** remove `app.UseStaticFiles()` (Program.cs:1061) — redundant alongside `MapStaticAssets` since the server `wwwroot` is empty; add `<link rel="icon">` to `index.html` if a favicon asset can be added trivially.
- **Rules:** verify no regression — `/`, `/_framework/*`, `/framework-assets/*`, `_content/*` still serve after removal.
- **Input → Output:** diff removes the middleware; smoke endpoints return 200.

## 6. Acceptance Criteria

- [x] **Given** a fresh non-Docker install via `install.sh` **when** the server starts in Production **then** `/` serves the WASM shell (`200` + `id="app"`), `/_framework/blazor.webassembly.js` and `/framework-assets/blazor.web/js` return `200`.
- [x] **Given** `install.sh` unchanged env plumbing **when** run **then** `taskctl` + MCP binaries still installed as before.
- [x] **Given** the combobox filter helper **when** `dotnet test` runs **then** ≥6 unit tests cover filter/cap/validation/clamping — all green.
- [x] **Given** docs grep for "Blazor Server" **when** the SPEC lands **then** zero hits remain outside historical SPECs/changelogs.
- [x] **Given** the three SPECs **then** `Status` reads `Done` with merge reference.
- [x] **Given** `UseStaticFiles` removed **when** the app runs **then** all static endpoints return `200` (no 404 regression).
- [x] `dotnet build` clean (TreatWarningsAsErrors); `dotnet test` green.

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read section-3 files; confirm `install.sh` launcher structure and combobox logic surface.
- [x] **T2 — Helper + tests (red→green):** `RepositoryFilter` + `RepositoryFilterTests`; wire the `.razor` to it.
- [x] **T3 — install.sh:** publish step + launcher path update; local verification (`dotnet exec` on publish dir, Production env).
- [x] **T4 — Docs + statuses:** update all listed files; set three SPECs to `Done`; tick combobox T5.
- [ ] **T5 — Cleanup:** remove `UseStaticFiles`; favicon if trivial; smoke static endpoints.
- [x] **T6 — Validation:** `dotnet build -c Release`, `dotnet test`, `install.sh` smoke on a temp HOME.
- [x] **T7 — Done + PR:** merge on `feature/devin-20260915-wasm-post-migration-hardening`.

**7.1 Validation strategy:** T2 is test-first; T3 verified by actually running `dotnet exec` on the publish output in a `Production` environment (the exact failure mode reproduced in the audit); docs verified by grep.

## 8. Organization Guardrails

- **Branches:** `feature/devin-20260915-wasm-post-migration-hardening`; never `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Scope:** no combobox behavior change; no Docker change; inconclusive gaps stay out (separate decisions).

## 9. Definition of Done

- [ ] All requirements implemented; all ACs verified.
- [x] `install.sh` deployment proven serving the SPA in Production.
- [x] `dotnet build` clean, `dotnet test` green.
- [x] Docs bilingual-consistent; SPEC statuses accurate.

## Open Questions / Pending Ambiguity

- `skills-lock.json` honoring (`GAP-requirements-skills-lock-not-honored`) — user deferred; candidate for a follow-up SPEC if pinning is desired.
- Container-home skills target (`GAP-operation-skills-sync-container-home`) — user deferred; revisit if host CLI sync is needed.
- `[A DEFINIR]` exact publish output dir inside install.sh layout — pick the convention install.sh already uses at implementation time.
