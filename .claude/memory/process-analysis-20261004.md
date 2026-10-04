# Process & CI/CD analysis — 2026-10-04

Review of the last 10 merged PRs (#449–#464), the GitHub Actions pipelines, current
test coverage and the README. Supersedes the 2026-10-03 note for coverage/process.

## 1. Last 10 PRs — what shipped and how it went

| PR | Scope | CI | Sonar new issues |
|---|---|---|---|
| #449 sqlite-backup | `taskctl backup/restore`, CLI in image | green | – |
| #450 mcp-tool-surface | board/specs/jobs/runs MCP tools (4→12) | green | – |
| #451 readme-screenshots | docs + screenshots section | green | – |
| #452 ci-coverage | 80% line + 45% branch gates, GH test logger, codecov | green | – |
| #459 codeql-sonar | 89-file CodeQL/Sonar cleanup batch | green | bulk-fixed |
| #460 agent-chat | Agent mode = provider chat + CLI delegation | green | – |
| #461 e2e-fixes | literal-binding + ACP-gate bugs from E2E run | green | – |
| #462 P1 | workspace picker, CLI registry, model discovery | green | 8 (accepted) |
| #463 P2 | task DAG + mailbox + dispatcher + fan-out | green | 16 |
| #464 P3 | dashboard + session resume + checkpoints + decide | green | 6 |

Process health is good: every PR has a SPEC in `.specs/`, tests land in the same
commit as the feature, coverage gates held, and E2E recordings caught real bugs
(#461) before users did.

**One real process failure:** the P-series was opened as a stacked chain
(#462→main, #463→p1-branch, #464→p2-branch) and the stacked PRs were merged
while their bases still pointed at the feature branches. P2+P3 landed on the
branch chain, not on `main` — recovered with #465. **Rule going forward:**
retarget stacked PRs to `main` (`git_update_pr_base`) as soon as their base
merges, or enable GitHub merge queue, which serializes exactly this case.

## 2. CI/CD — what exists vs what's missing

Existing (`dotnet.yml`): restore → `dotnet format --verify-no-changes` → Release
build → full test run w/ cobertura → line≥80% + branch≥45% gates → TRX artifact +
Codecov upload. Plus `code-quality.yml` (SonarCloud), `codeql.yml` (csharp +
actions, weekly), vulnerable-packages job, Dependabot.

Gaps, ordered by value:

1. **No UI-component tests.** `Taskboard.Blazor` is 20.5k lines at ~3% coverage,
   `Taskboard.Client` 0% — the aggregate 80% gate masks them. bUnit is already a
   test dependency; a `Taskboard.Tests.Blazor`-style suite (or a folder in
   Tests.Unit) for `Agents` dashboard, `ProviderChat` toolbar, `AiChat` pickers
   and `Terminal` would cover the regression class that just bit us (#461).
2. **Coverage gate is single-number.** Add a per-assembly floor for the
   well-covered projects (e.g. Integrations ≥70%, Application ≥80%) so a big
   uncovered UI/feature can't silently dilute the average. ReportGenerator can
   emit per-assembly Markdown; a small awk check per row suffices.
3. **Test results not published to PRs.** TRX uploads as an artifact only —
   add `dorny/test-reporter` (or the built-in GH test-logger annotations, already
   wired) so failures show inline on the Checks tab.
4. **No docker image smoke test.** The runtime image ships `taskctl` since #449;
   a `docker build --target test` + `taskctl --version` job would catch packaging
   drift before release.
5. **No merge-queue / stacked-PR protection.** Enable GitHub merge queue on
   `main`, or document "merge bottom-up after retarget" in CONTRIBUTING.
6. Optional: nightly `schedule:` run for the full suite + a Stryker.NET mutation
   pass on `Taskboard.Integrations` (the most bug-dense area) — cheap since it
   doesn't gate PRs.

## 3. Coverage — current state (local Debug run on main)

Merged: **~77% line / ~46.6% branch** (CI's Release run passes the 80% gate;
Debug instrumentation reads slightly lower — treat as directional).

| Assembly | Line cov | Lines |
|---|---|---|
| Taskboard.Client | 0% | 56 |
| Taskboard.Blazor | ~3% | 20,488 |
| Taskboard.Mcp | ~50% | 400 |
| taskctl | ~60% | 1,008 |
| Taskboard.Integrations | ~75% | 28,471 |
| Taskboard.Server | ~79% | 11,186 |
| Taskboard.Application | ~86% | 9,358 |
| Domain / Domain.Shared / Contracts | ~87–89% | ~14,600 |
| Taskboard.EntityFrameworkCore | ~98% | 65,417* |

*EFCore line count is inflated by generated migrations/snapshot — its number is
not meaningful; exclude generated files via `-classfilters:*.Migrations.*` in
the CI report for a truer picture.

**Plan to lift coverage:**
- bUnit suite for Blazor (top-value: `Agents`, `AiChat`, `ProviderChat`,
  `Terminal`, `Board`) — target Blazor 3%→30% over the next feature wave; every
  new component ships with bunit tests (same rule as server-side code).
- Raise `taskctl` (60%) via `CommandAppTester` for the newer commands.
- Raise `Taskboard.Mcp` (50%) — the 12 MCP tools added in #450 need handler tests.
- Keep the ratchet: bump `COVERAGE_THRESHOLD` 80→82 after the Blazor suite lands.

## 4. README — refreshed in this PR

- New screenshots: `agents-dashboard.jpg` (P3 dashboard + CLI sessions cards) and
  `workspace-picker.jpg` (P1 workspace picker modal).
- Feature bullets updated for the delegation wave (DAG/mailbox/dispatcher,
  fan-out, dashboard, session resume, checkpoints, decision gate, workspace
  picker, enriched CLI registry).
- Test counts refreshed (1,550+ unit / 315+ integration).

## 5. SonarCloud status

- 35 open issues on new code (12 MAJOR, 6 CRITICAL): top rules S1075 (8, URIs —
  accepted-by-design), S3358 (6, nested ternaries), S8970 (5, `!` false positives),
  S3776 (4, cognitive complexity), S107 (4, ctor params — DI).
- 1,209 total open issues repo-wide — mostly legacy debt; keep fixing on touch.
