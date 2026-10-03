# Process & CI/CD analysis — 2026-10-03

Requested: 「Analise o que pode melhorar nesse processo。 Veja também o ci/cd do GitHub」after the 7-slice audit epic (#433). Analysis of the dev/test process and GitHub CI/CD; implemented changes are in PR #452. Baseline measured locally on 2026-10-03: **80.22% line / 46.67% branch** coverage.

## Implemented (PR #452)

| Gap | Fix |
|---|---|
| `dotnet.yml` had `push.branches-ignore: [main, master, develop]` → tests/coverage never ran on main; no Codecov baseline, merges unvalidated, no badge possible | Removed the ignore: tests run on all pushes incl. main (path filters unchanged) |
| `COVERAGE_THRESHOLD: 77` while actual is 80.22% | Ratcheted to 80 (CLAUDE.md Hard Rule 6) |
| Branch coverage unmonitored | New `BRANCH_COVERAGE_THRESHOLD: 45` gate (actual 46.67%) |
| Test failures only visible as trx artifacts | Added `--logger "GitHubActions"` → inline PR annotations (requires `GitHubActionsTestLogger` NuGet — added to both test projects) |
| Codecov upload ran only on PRs | Now `if: success()` on every build — needed for the main baseline |
| Digest-pinned Docker base images (#447) had no update path | Dependabot `docker` ecosystem added (weekly, same Monday schedule) |

## Verified in CI itself

PR #452's own run exercises the new workflow: the first run failed because the `GitHubActions` logger requires the `GitHubActionsTestLogger` package (not built into VSTest) — fixed in the same PR.

## Process findings (no code change)

1. **Coverage gap is concentrated in UI**: `Taskboard.Client` 0%, `Taskboard.Blazor` 3.15% of line coverage; `taskctl` 60.7%, `Taskboard.Mcp` 53.4%. Server-side assemblies are 76–98%. The real "coverage increase" lever is **bUnit component tests** for `Taskboard.Blazor` (bunit is already a dependency of the unit test project) — candidates: `PageTitles` mapping, aria-state markup patterns (the `false`-attribute pitfall found in #442), KanbanBoard helpers, UiFormat callers. A Playwright/bUnit UI test slice would also regression-guard the a11y/mobile work that previously had zero automated coverage.
2. **Unit vs integration split**: considered and rejected for now — total suite is ~90 s; a split matrix would need per-leg coverage merging and doesn't pay off until the suite exceeds ~5 min.
3. **Ratchet mechanics are still manual**: `COVERAGE_THRESHOLD` is a hand-edited env value. Cheap improvement later: a scheduled job that computes current coverage and opens a PR to bump the threshold (or fails when coverage drops >1% without an explicit threshold update).
4. **Label hygiene**: the `in_progress → in_pullrequest → done` convention broke once this session (#438 kept both `todo`+`in_pullrequest`). Since labels are exclusive, automation that transitions labels must remove all other status labels — worth enforcing in the `manage-taskboard` flow/MCP `move_issue` docs.
5. **Dockerfile test stage** (added in #432) already runs the full suite in-container; it is not wired into GitHub Actions — could be used as the canonical test environment, or conversely the CI could build the image once and run tests inside it to keep "CI env == prod env". Not changed; medium effort.

## Not pursued

- Mutation testing (Stryker.NET) — heavy, low ROI at this suite size.
- `dorny/test-reporter` — superseded by the native `GitHubActions` logger (no third-party action needed).
- AOT for WASM — rejected in perf-pass SPEC with numbers.
