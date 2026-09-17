# Gap Analysis — 2026-09-17

Post-implementation audit of `SPEC-20260917-skills-installer` and
`SPEC-20260917-rag-mcp-provisioning` on branch
`feature/devin-20260917-skills-installer`.

## 1. Source inventory

| Source | State |
| --- | --- |
| `.specs/SPEC-20260917-*.md` (2) | present — TO-BE for this run |
| `docs/` (en + pt-br) | present — updated in this run |
| `docs/architecture/` | present — not in scope |
| `.claude/memory/gap-analysis-2026091{4,5}.md` | present — prior runs, dedup reference |
| `CLAUDE.md` / `AGENTS.md` / `.claude/rules/` | present |
| Build + tests | `dotnet build -c Release` clean (0 W/0 E); `dotnet test` 336/336 green (253 unit + 83 integration) |
| `gh` auth | present — afonsoft (GH_TOKEN) |

## 2. Candidates and verdicts

| Key | Category | Verdict | Priority | Evidence |
| --- | --- | --- | --- | --- |
| GAP-implementation-installsh-cache-failure | implementation | CONFIRMADO → **fixed in-run** | low | SPEC skills RF-003/edge "cache corrupt → install-sh = Failed, npx preserved". AS-IS: `SkillsInstallerService.InstallCoreAsync` let `EnsureCacheAsync` throw to the outer catch — `install-sh` step never recorded. Fix: per-step try/catch, step marked `Failed` independently (SkillsInstallerService.cs:181-205). |
| GAP-implementation-manifest-step-fields | implementation | CONFIRMADO → **fixed in-run** | low | SPEC RF-004 manifest shape `{repository, installedAtUtc, durationMs, npxStep, installShStep, skillCount}`. AS-IS: `InstallManifest` lacked per-step fields. Fix: `npxStep`/`installShStep` added (InstallManifest.cs:30-34). |
| GAP-tests-skills-install-endpoints | tests | CONFIRMADO → **fixed in-run** | medium | SPEC skills §3 lists `tests/.../SkillsInstallEndpointsTests.cs`; T6 requires endpoint auth/coalescing coverage. AS-IS: file absent. Fix: `tests/Taskboard.Tests.Integration/SkillsInstallEndpointsTests.cs` (6 tests; `ISkillsInstallerService` stubbed via `ConfigureTestServices` so no real `npx` spawns). |
| GAP-ui-status-polling | implementation | CONFIRMADO → **fixed in-run** | medium | SPEC skills RF-007 + RAG RF-008: "poll status while `state == Running`"; Install disabled "with tooltip". AS-IS: no timer in `Settings.razor`. Fix: `PeriodicTimer` loop (2 s) refreshing install/sync/MCP statuses while any is `Running`, cancelled via `IDisposable`; `title` tooltip lists missing prerequisites (Settings.razor:433-486, 127-129). |
| GAP-docs-settings-sections | documentation | CONFIRMADO → **fixed in-run** | medium | Both DoDs require `docs/` updates. AS-IS: no `api.md` section for settings/skills-install/mcp; no `TASKBOARD_RAG_*` env rows. Fix: `docs/api.md` + `api.pt-br.md` (Settings, Agent Skills, MCP Provisioning sections); `installation.md` + `.pt-br.md` env tables. |
| GAP-process-spec-checkboxes | documentation | CONFIRMADO → **fixed in-run** | low | AC + task-plan checkboxes empty, `Status: Approved` after green validation. Fix: all items checked, `Status: Done`. |
| skills-manifest-npx-version | implementation | REJEITADO | — | `npx skills --version` in manifest was `[A DEFINIR]` in the spec's Open Questions — optional, not required. |
| npxrunner-naming | implementation | REJEITADO | — | Spec file list mentions `NpxRunner.cs`; implemented as generic `ISkillsInstallRunner`/`ProcessSkillsInstallRunner` reused by git steps — equivalent seam, better coverage. |
| coverage-80-new-code | tests | INCONCLUSIVO | — | ≥80% on new code not measured locally; CI gate (`COVERAGE_THRESHOLD`, 45%) applies on PR. All new logic has unit/integration tests (38 skills-installer + 21 MCP unit, 12 integration). |

## 3. Totals

- Candidates: 9 | Confirmados: 6 (all fixed in-run, same approved-spec scope) | Rejeitados: 2 | Inconclusivos: 1
- No Draft SPECs or Issues generated: every confirmed gap was an incomplete requirement of an already-Approved spec, not new scope — fixed before delivery per user direction.

## 4. Validation evidence

- `dotnet build -c Release` → 0 warnings / 0 errors.
- `dotnet test -c Release` → `Taskboard.Tests.Unit` 253/253, `Taskboard.Tests.Integration` 83/83.
- Security: no API key in status payloads (integration test asserts absence); manifests store no output/tokens; config writes atomic + `0600` + `.bak`/`.corrupt-bak`.

## 5. Open pendencies

- Coverage percentage on new code — deferred to CI report.
- Prior-run inconclusive items remain open user decisions: `skills-lock.json` honoring, container-home sync target.
