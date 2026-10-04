# SPEC-20261004-session-scanner-more-clis — Session history for gemini/agy/devin

## Context

`AgentSessionScanner` (SPEC-20261006 RF-002/RF-003) lists resumable on-disk CLI
sessions on the Agents page and powers `GET /api/agents/sessions`. It covers
`claude`, `codex` and `opencode`; `gemini` (Gemini CLI), `agy` (Antigravity CLI)
and `devin` (Devin CLI) also persist sessions locally but are not scanned.

## Requirements

- **RF-001 — Gemini CLI.** Scan `~/.gemini/tmp/<project-hash>/chats/session-*.json`.
  The session id lives in the `sessionId` field near the top of the file; the
  scanner reads a bounded 8 KB prefix to extract it (an index field, never
  message contents). `WorkingDirectory` stays null — `<project-hash>` is an
  opaque hash of the cwd and is not decodable. `ResumeCommand` is
  `gemini --resume <id>` (Gemini CLI is not an `AgentCliKind`, so the literal is
  emitted by the scanner).
- **RF-002 — Antigravity (agy).** Union the per-conversation stores
  `~/.gemini/antigravity-cli/brain/<uuid>/` (directories) and
  `~/.gemini/antigravity-cli/conversations/<uuid>.db` (file stems), deduplicated
  by conversation uuid. `WorkingDirectory` resolves from the append-only index
  `~/.gemini/antigravity-cli/history.jsonl` (`{"conversationId","workspace"}`
  per line, later lines win), falling back to
  `cache/last_conversations.json` (inverted workspace→latest-conversation map).
  `ResumeCommand` is `agy --conversation <id>` via
  `AgentCliSpec.ResumeArgs: ["--conversation", "{id}"]` on the Antigravity spec.
  The `cli` filter accepts `agy` and `antigravity`.
- **RF-003 — Devin CLI.** Read `~/.local/share/devin/cli/sessions.db` through
  `SqliteCliDatabaseReader` (read-only + WAL-safe temp copy, whitelist-enforced
  `sessions` table via `CliDatabaseMap`). Selected columns are metadata only —
  `id`, `title`, `working_directory`, `last_activity_at` (epoch seconds);
  message content is never read. `ResumeCommand` is `devin --resume <id>` via
  `AgentCliSpec.ResumeArgs: ["--resume", "{id}"]` on the Devin spec.
- **RF-004 — Never errors.** Missing directories, missing DBs, unreadable files
  and corrupt/torn DBs all degrade to an empty result for that CLI — the
  endpoint contract (`GET /api/agents/sessions`) is unchanged.
- **RF-005 — Ordering.** `ScanAsync` returns all CLIs' sessions merged,
  `ModifiedAtUtc` descending, up to `takePerCli` entries per CLI (unchanged).

## Non-goals

- No new `AgentCliKind`/`AgentType` for Gemini CLI — orchestration types and
  the CLI Agents picker are unchanged.
- No transcript parsing (titles/previews stay out of scope; `title` is selected
  for devin only as a byproduct of the whitelisted metadata query and is not
  exposed on the DTO).
- Locked/hidden-session handling beyond what `last_activity_at` exposes.

## Test evidence

- Gemini: `session-*.json` fixture yields Cli=`gemini`, parsed `sessionId`,
  resume `gemini --resume <id>`.
- agy: `brain/<uuid>` + `history.jsonl` yields Cli=`agy`, workspace, resume
  `agy --conversation <uuid>`; `conversations/*.db`-only uuid also listed.
- Devin: real `sessions.db` yields Cli=`devin`, `WorkingDirectory` and
  `devin --resume <id>`; epoch seconds map to `ModifiedAtUtc`.
- Filter `cli=agy`/`cli=antigravity` narrows to agy sessions.
- `BuildResumeArgs`: Devin `["--resume", id]`, Antigravity `["--conversation", id]`.
