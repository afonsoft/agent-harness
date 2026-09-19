# taskctl CLI

`taskctl` emits JSON for normal commands. Add `--json` when making the output contract explicit. Built-in help is the only successful stdout exception: it writes plain text, exits with code `0`, and does not request the Taskboard service.

```bash
taskctl --help
```

> Local projects and issues (`project:*`, `issue:*`, `comment:*`, `attachment:*`) were removed — the board's source of truth is GitHub. See `.specs/SPEC-20260918-projects-removal.md`.

## Context

```bash
taskctl context:current [--json]
```

Returns the current workspace context (repository, branch, path) resolved for the running agent.

Set `TASKBOARD_URL` to override the default local API origin, `http://127.0.0.1:47823`.

## GitHub board issues

GitHub-board cards live on `/api/github/...`, identified by the GitHub issue id and `owner/repo` + issue number.

```bash
taskctl ghissue:history <issueId> [--take 50] [--json]
taskctl ghissue:comments owner/name <issueNumber> [--take 50] [--json]
taskctl ghissue:comment owner/name <issueNumber> "<body>" [--json]
```

- `ghissue:history` returns the unified timeline (newest first): column moves, edits, closes, and agent runs for that issue.
- `ghissue:comments` lists GitHub issue comments chronologically (author, body, timestamps, `htmlUrl`).
- `ghissue:comment` publishes a comment to GitHub. Comments are the **agent handoff channel**: read history + comments before taking an issue, and post a summary comment when finishing a stage so the next agent or step keeps the context.

## Cloud session

For a shared cloud board, keep `taskctl` pointed at the **loopback companion** and configure the upstream HTTPS origin through it:

```bash
taskctl cloud:login --url HTTPS_ORIGIN --actor-name NAME [--json]
taskctl cloud:status [--json]
taskctl cloud:logout [--json]
```

`cloud:login` reads the shared password from a private `Shared key:` prompt. The actor name is the display attribution sent through Basic Authentication. The local companion stores its configuration with mode `0600`.
