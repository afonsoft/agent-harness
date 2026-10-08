# AD-0003 — Dedicated Git worktree per agent run

## Status

Accepted (retroactive record — delivered by Epic E6, SPEC-20260919-harness-workspace-isolation)

## Context

Agent CLIs (Claude Code, Codex, OpenCode, Devin, …) execute against a repository and mutate files. Running them directly on the user's checkout would mix agent output with in-progress human work, make rollback impossible and allow concurrent agents to fight over the same index/branch. Runs also need to be inspectable (diff, files, commits) and reversible (teardown).

Options considered:

- Run on the live checkout with `git stash`/restore — fragile, loses untracked files, no isolation between concurrent runs.
- Clone per run — correct isolation but expensive (full clone per run) and loses the user's local ref database.
- **Git worktree per run** — cheap (shared object database), isolated index/branch per run, trivially removable.

## Decision

Every agent run executes inside a **dedicated Git worktree** created under `Taskboard:WorktreeRoot` (default `~/repos`, so run worktrees sit next to user clones) as `{worktreeRoot}/{runId}`. Lifecycle is exposed via `POST|GET|DELETE /api/harness/worktrees` (create / inspect+diff / teardown with `git worktree prune`). Mid-run state can be snapshotted and restored through `harness-checkpoint:`-prefixed commits. The agent's cwd is always the worktree — never the live checkout.

## Consequences

- Concurrent runs on the same repository are safe: each has its own index, branch and working directory.
- The cockpit's Diff tab and `GET .../worktrees/{runId}/diff` compute `git diff`/`--numstat` inside the run worktree; create-PR pushes the worktree branch.
- Disk usage grows with concurrent runs — teardown (`DELETE`) is explicit and prunes; stale worktrees are visible and cleanable.
- Path confinement (path-jail + symlink-escape enforcement in the security gateway) is anchored to the worktree root, which makes the jail a single well-defined boundary.
