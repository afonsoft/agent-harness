# SPEC-20260913-board-repo-selector

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | board-repo-selector |
| Type | Frontend / Feature |
| Stack | .NET 10 / Blazor Server / Blazor.Bootstrap 4.0.0 / Octokit |
| Repository | taskboard-ai |
| Branch | `feature/devin-20260913-bootstrap-modernization` |
| Ticket | N/A |
| Status | Done (merged via PR #51, commit c6dc888) |

## 1. User Story

**As a** Taskboard user,
**I want** the board's "Project Name" field to be a dropdown listing my local Taskboard projects and all GitHub repositories I can access,
**So that** selecting a repository loads all of its issues grouped by their board status columns, without leaving the main board.

**Problem context:**

- The main board (`/`, `BoardView.razor`) shows "Project Name" as a read-only field bound to the first local project (`projects.FirstOrDefault()`).
- GitHub repository selection exists only on `/github-board` (`RepositorySelector` + `KanbanBoard`), requiring a separate page and a manual token field.
- The GitHub token can already be configured server-side via the `GITHUB_TOKEN` environment variable (`GitHubService` reads it at startup) — when present, no token UI is needed.

## 2. Goals / Non-Goals

**Goals:**

- "Project Name" becomes a `<select>` with two groups: local Taskboard projects and GitHub repositories (when `GITHUB_TOKEN` is configured).
- Selecting a GitHub repository loads all its issues and renders them in status columns reusing the existing `KanbanBoard` component (drag-and-drop, agent selection, task detail — all preserved).
- Selecting a local project keeps the current task-board behavior unchanged.
- GitHub token supplied via `.env` / environment — no token input on the main board.

**Out of scope:**

- No new API endpoints; `IGitHubService` is injected directly (server-side Blazor).
- No changes to `/github-board` page or `RepositorySelector` (left as-is).
- No persistence of the last selected board (can be a follow-up).
- No changes to `IGitHubService` contract.

## 3. Requirements

### RF-001: Project/repository selector

- **Description:** Replace the read-only "Project Name" value in `BoardView` with a `<select class="form-select">` (labeled accessibly). Options grouped with `<optgroup>`:
  - `Projects` — local projects from `TaskboardClient.GetProjectsAsync()`.
  - `GitHub` — repositories from `IGitHubService.GetRepositoriesAsync()`, displayed as `FullName` (`owner/repo`), sorted alphabetically.
- **Acceptance:** The select lists all local projects and, when a token is configured, all accessible GitHub repos.

### RF-002: Repository selection loads issues with statuses

- **Description:** When a GitHub repository is selected, load `IGitHubService.GetIssuesAsync(repoFullName, GitHubBoardColumnExtensions.GetAllLabels())` and render the existing `KanbanBoard` component with `RepositoryFullName` bound — columns `Backlog`, `In Progress`, `Review`, `Done` come from issue labels (`backlog`, `in-progress`, `review`, `done`).
- **Acceptance:** Issues appear in their status columns; drag-and-drop, agent selection modal, task detail modal and "Nova Tarefa" all work exactly as on `/github-board`.

### RF-003: Local project selection unchanged

- **Description:** Selecting a local project renders the current task board (statuses backlog/todo/in_progress/in_review/blocked/done/canceled + archived column), with dates, duration and priority legend.
- **Acceptance:** No regression in the existing board for local projects.

### RF-004: Error and empty states

- **Description:**
  - `GITHUB_TOKEN` absent → the GitHub optgroup is omitted; board works with local projects only. If the user has a way to detect it, a hint to configure `GITHUB_TOKEN` may be shown — optional.
  - Repo list load fails → `alert alert-warning` in the header, local projects still usable.
  - Issue load fails → `alert alert-danger`, select keeps current value.
- **Acceptance:** Failures never blank the board; a local selection always works without GitHub.

### RF-005: `.env` configuration

- **Description:** Create `.env` at repo root (already gitignored) containing `GITHUB_TOKEN=<token>` copied from `GH_TOKEN` in `~/.bashrc`. Docker runs with `--env-file .env`. Never commit the file.
- **Acceptance:** Container starts with `GITHUB_TOKEN` set; repository listing works without `SetToken` calls.

## 4. Technical Notes

- `BoardView.razor` injects `IGitHubService` in addition to `TaskboardClient`.
- Selection model: a single `string` bound value, e.g. `project:{id}` / `repo:{fullName}` prefixes, or a small discriminated union — implementation choice.
- `KanbanBoard` is reusable as-is: it already loads issues on `RepositoryFullName` change and renders status columns.
- Default selection: first local project (current behavior) — or the GitHub repo if no local project exists.
- While loading: keep the existing `Loading` component; changing selection shows a loading state in the board area only (header stays interactive).

## 5. Files

```text
src/Taskboard.Blazor/Components/BoardView.razor   # select + conditional KanbanBoard/task board
.env                                            # GITHUB_TOKEN (gitignored)
```

## 6. Definition of Done

- [ ] `dotnet build -c Release` → 0 warnings, 0 errors.
- [ ] `dotnet test` → suite green.
- [ ] Smoke: `/` shows the dropdown; selecting a repo lists its issues in status columns; selecting a project shows the local task board.
- [ ] `.env` present locally, NOT committed; container started with `--env-file .env`.
- [ ] `git status` shows no secrets staged.

## Open Questions / Pending Ambiguity

- Resolved: board unifies local projects + GitHub repos in one dropdown; repo issues render via existing `KanbanBoard` (full GitHub feature set on the main board).
