# gap-analysis run — 2026-09-14

## Source inventory

| Source | Status |
| --- | --- |
| `.specs/SPEC-*.md` (34 files) | present |
| `docs/` + `docs/architecture/` | present |
| `.claude/CONTEXT.md`, `MEMORY.md`, `memory/orchestrator_stats.md` | present |
| `CLAUDE.md` (= `AGENTS.md` symlink), `README.md` | present |
| `.claude/rules/`, `.claude/agents/` | present |
| `src/` (14 projects), `tests/` (89 unit + 9 integration, all green) | present |
| CI: `dotnet.yml`, `code-quality.yml`, `codeql.yml` | present |
| `gh` auth: `afonsoft/agent-harness` | ok; all existing issues closed |
| Sibling skills `write-specs`, `create-issues`, `orchestrator` | present |

## Candidates and verdicts

| Key | Verdict | Evidence |
| --- | --- | --- |
| `GAP-implementation-blazor-web-js` | CONFIRMADO → fixed | `_framework/blazor.web.js` 404 (live + local). Root cause: `RequiresAspNetWebAssets` auto-set only when web project has `.razor` files (all live in `Taskboard.Blazor` RCL). Without blazor.web.js, Bootstrap `data-bs-dismiss` `preventDefault()` killed all NavLink clicks. Fix: `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` in `Taskboard.Server.csproj` — verified 200 (200,645 B) after rebuild. SPEC: `.specs/SPEC-20260914-blazor-web-assets.md`. |
| `GAP-implementation-env-var-precedence` | CONFIRMADO | `TaskboardEnvironment` reads `Taskboard:*` config before `TASKBOARD_*` env vars; appsettings always sets both → env vars dead. Same inversion in `AdminUser.CreateFromConfiguration` (`Admin:Username` beats `TASKBOARD_ADMIN_USERNAME`). Contradicts `docs/installation.md:64-106`. SPEC: `.specs/SPEC-20260914-env-var-precedence.md`. |
| `GAP-documentation-stale-specs` | CONFIRMADO | board-repo-selector merged (c6dc888/PR #51) still `Approved`; tailwind-theme-refresh superseded still `Approved`; MudBlazor ACs stale in 2 specs; devin-antigravity-harness AC references removed `.devin/skills`/`.agent/skills`. SPEC: `.specs/SPEC-20260914-stale-spec-status.md`. |
| `GAP-ux-login-sidebar` | CONFIRMADO | `MainLayout` renders full NavMenu on `/login` for unauthenticated users; auth gate loops clicks back to `/login`. SPEC: `.specs/SPEC-20260914-login-sidebar.md`. |
| `GAP-implementation-blazor-feature-parity` | CONFIRMADO (user-approved scope) | SPEC-008 TO-BE lists gantt/filters/comments/attachments/workflow visual; Blazor UI has none. SPEC: `.specs/SPEC-20260914-blazor-feature-parity.md` (phased A–D). |
| `context:current` duplicate (followups.md) | REJEITADO | Single registration at `src/Taskboard.Cli/Program.cs:17`; already fixed. |
| Coverage gate vs hard rule | REJEITADO (documented) | `COVERAGE_THRESHOLD=45%` in `dotnet.yml` vs CLAUDE.md "≥80%" — CLAUDE.md itself documents "hoje 45%, meta 80%"; known, intentional ramp. |

## Notes

- Deployed instance on `:47823` (root, build 2026-09-13) still exhibits the menu bug until rebuilt/reinstalled.
- `.env` contains a live `GITHUB_TOKEN` — gitignored, not tracked (verified `git ls-files`, `git check-ignore`).

## Outputs

- Branch `feature/devin-20260914-menu-links-gap-analysis`
- Fix: `src/Taskboard.Server/Taskboard.Server.csproj` (+1 line)
- Draft SPECs: `SPEC-20260914-blazor-web-assets`, `-env-var-precedence`, `-stale-spec-status`, `-login-sidebar`, `-blazor-feature-parity`
- PR: https://github.com/afonsoft/agent-harness/pull/59
- Epic: https://github.com/afonsoft/agent-harness/issues/60
- Slices: #61 (blazor-web-assets, fixed in PR #59), #62 (env-var-precedence), #63 (stale-spec-status), #64 (login-sidebar), #65 (blazor-feature-parity)

## Execution result (2026-09-14, post-approval)

All slices implemented, tested, merged, and issues closed:

| Slice | Issue | PR | Result |
| --- | --- | --- | --- |
| S1 blazor-web-assets | #61 | #59 | MERGED — `RequiresAspNetWebAssets`; regression test added |
| S2 env-var-precedence | #62 | #66 | MERGED — `TASKBOARD_*`/`TASKBOARD_ADMIN_*` now beat appsettings; conflict tests added |
| S3 stale-spec-status | #63 | #67 | MERGED — 5 SPECs synced (Done/Deprecated statuses + AC updates) |
| S4 login-sidebar | #64 | #68 | MERGED — `MinimalLayout` + `@layout` on Login; verified no nav on /login |
| S5 blazor-feature-parity | #65 | #69 | MERGED — LocalTaskDetailDialog (comments+attachments), board filters, `/gantt`, `/workflow`; added `GET /api/tasks/{id}/attachments`; fixed latent upload bugs (invalid default kind `"file"`→`"attachment"`, antiforgery 400 via `.DisableAntiforgery()`) |

Epic #60 closed. Final state: `dotnet build` 0 warnings; 93 unit + 13 integration tests green.

Known remaining note: `dotnet run` in Production environment serves `_framework/*` from `wwwroot` (published layout) — 500 from source; Development env and `dotnet publish` deployments are unaffected.
