# SPEC-20261004-promote-leg-pr — GitHub PR from the promoted leg

## Context

`POST /api/local/delegation/tasks/{id}/promote` (SPEC-20261009 RF-002) commits
the leg's worktree and pushes its branch to `origin`; creating the GitHub PR
stayed manual ("the PR step stays manual in the cockpit"). This spec closes
that gap: `IGitHubService.CreatePullRequestAsync` already exists
(SPEC-20260919-ade-cockpit-hitl RF-005, cockpit "Create PR" action), so the
missing piece is resolving which repo/branch pair the promoted leg belongs to
and calling it.

`DelegationTask` stores only `RepositoryPath` (local clone path) — no
`owner/name` slug — so the slug is derived from the clone's `origin` remote at
promote time, reusing the same URL grammar `RemoteMatches` already accepts
(`https://github.com/o/n(.git)`, `git@github.com:o/n(.git)`,
`ssh://git@github.com/o/n(.git)`).

## Requirements

- **RF-001 — `GitRemoteSlug.TryParse`.** New helper
  (`Integrations/Harness/GitRemoteSlug`) mapping a remote URL → `owner/name`,
  or `null` for non-github.com/malformed URLs. Deliberately NOT fused with
  `RemoteMatches`: the clone-identity check accepts any host (GHE mirrors),
  while PR creation must restrict to github.com (Octokit targets
  api.github.com).
- **RF-002 — Promote request.** `POST tasks/{id}/promote` accepts optional
  query params `createPr`, `title`, `baseBranch` (query, not body: inferred
  `[FromBody]` would 404 the existing `content: null` callers — minimal APIs
  reject a missing JSON Content-Type before `EmptyBodyBehavior` applies).
  `createPr` absent/false → exact current behavior.
- **RF-003 — PR creation.** With `createPr=true`, after commit+push
  `IPromotedLegPrService.TryCreateAsync` resolves `owner/name` from the
  clone's stored `git config remote.origin.url` — NOT `remote get-url`, which
  resolves `url.insteadOf` rewrites (Devin VMs rewrite github.com into the
  proxy host and would never parse). github.com only — the Octokit call
  targets api.github.com. Picks the base as
  `baseBranch` (request) → `session.BaseBranch` normalized (`origin/x` → `x`)
  → the clone's `origin/HEAD` short name → `main`, and calls
  `IGitHubService.CreatePullRequestAsync(repo, title ?? $"delegation: {cli} leg — {prompt}",
  branch, base, body)`. Default body lists task id, CLI and worktree run.
  The service is injectable (`Integrations/Delegation/PromotedLegPrService`)
  so the endpoint stays thin and the step is unit-testable.
- **RF-004 — Failure policy.** `createPr=true` + unresolvable slug →
  400 `{error: "could not resolve repository slug: …", branch, commitSha}`;
  `createPr=true` + `CreatePullRequestAsync` failure →
  400 `{error: "pr creation failed: …", branch, commitSha}` — the worktree was
  still promoted and branch+sha stay in the body for a manual retry.
  `createPr=false` never touches GitHub.
- **RF-005 — Response shape.** `{ taskId, branch, commitSha,
  pullRequestUrl: string? }` — additive; `pullRequestUrl` is `null` when no
  PR was requested.
- **RF-006 — UI.** Compare/promote affordance gains "Promote + PR" when the
  leg's task exposes a `RepositoryPath`; the result shows the PR URL as a
  link (or falls back to the existing branch+sha display).

## Non-goals

- Storing `RepositoryFullName` on `DelegationTask` (entity field + migration)
  — remote-derived slug covers every leg, including tasks created before this
  field would exist.
- Auto-creating PRs on plain promote (opt-in flag only).
- Draft PRs, reviewers/labels wiring, or closing the source issue.

## Test evidence

- `TryParse` covers https/git@/ssh:// + `.git` suffix + case; returns null on
  non-GitHub hosts and malformed URLs.
- `PromotedLegPrService` (mocked `IGitHubService`/`IGitCommandRunner`/
  `IWorkspaceIsolationService`): resolvable remote → `CreatePullRequestAsync`
  called once with expected repo/head/base and `pullRequestUrl` returned;
  missing `RepositoryPath`/non-GitHub remote → `SlugUnresolved`; service
  throw → `GithubFailed`; `origin/develop` base → `develop`.
- Endpoint wiring: `?createPr=true` binds (400 "no worktree" still fires
  before any PR attempt); `content: null` POSTs keep working unchanged.
- `NormalizeBase`: `origin/develop` → `develop`; `HEAD` → origin/HEAD
  short name; explicit `base` wins.
