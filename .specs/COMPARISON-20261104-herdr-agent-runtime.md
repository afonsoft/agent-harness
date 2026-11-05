# COMPARISON — Herdr (herdrdev/herdr, herdr-core, herdr-app) × Harness: agent runtime, control, inter-agent protocol

**Date:** 2026-11-04 · **Scope:** how Herdr runs and controls coding agents (claude, devin,
codex, opencode, cursor, gemini, antigravity, grok, kiro, amp, droid, kimi, kilo, cline,
muse, hermes, qwen, pi, maki…), how agents talk to each other, remote machines, and the
operational contract — mapped against agent-harness.

**Sources:** local clones of `herdrdev/herdr` (`AGENTS.md`, `distribution/agent-detection/*.toml`,
`skills/herdr/SKILL.md`), `herdrdev/herdr-core` (`AGENTS.md`, `crates/herdr-core/src/SKILL.md`
= `hc --skill` manual, `docs/machines.md`, `docs/commands.md`, `docs/templates.md`,
`docs/security.md`), `herdrdev/herdr-app`, and DeepWiki indexes for all three repos.
Harness side: `.specs/*`, `src/Taskboard.*/**` current main.

## 0. What Herdr is — three layers, three repos

| Repo | Role | Analogy for us |
|---|---|---|
| `herdr-core` (`hc`) | Headless **terminal runtime**: runs programs/agents in persistent terminals, keeps them alive across core restarts (holder processes + adoption), relays raw bytes to viewers, records status **only** from OSC 7501 reports, one OpenRPC method table over Unix socket / Windows named pipe → CLI args + published schema generated from the same table. *"Core is a runtime, not a multiplexer."* | `harness-server` + `StreamingProcessRunner`/`PtySession` layer |
| `herdr` (v1, TUI) | Terminal multiplexer: workspaces/tabs/panes, **screen-heuristic agent detection** (TOML manifests) layered *on top* of the runtime, self-update, plugin system | our Blazor UI + attention detection |
| `herdr-app` | New desktop app (gpui): spaces/tabs/panes over hc, mods platform (QuickJS host API), bundled frames | our Blazor/MAUI frontend |

Design split worth copying: **strong signals live in the runtime (reported status only);
weak-signal heuristics live in the app layer as data-driven manifests.** Never the reverse.

## 1. How Herdr controls agents

### 1.1 Launch model — agents are interactive citizens, not one-shot processes

- `hc run --bg --name review --cwd <repo> claude "<first task>"` — agents always run
  **interactively** inside a persistent terminal. Manual rule: *"Run agents interactively,
  never `pi -p` or another one-shot print mode. They must remain watchable, steerable, and
  available for another turn."*
- Named terminals (`--name review`, `[a-z][a-z0-9_-]{0,31}`, unique among live) are
  first-class addresses: `<t>` = id | name | `machine/name`.
- **Templates** (`~/.config/herdr-core/templates/<name>.md`): frontmatter
  `{name, description, command(incl. model)}`, body = instructions; template + task become
  the agent's first message. Launch waits up to 60s for readiness and persists
  `first_message: {sent, reason}` — a *durable* record that the task actually reached the
  agent, inspectable even after a core restart.
- Readiness is part of launch: `agent start` returns only after the agent is detected and
  ready for input; `hc send` on a booting agent can be dropped → template flow + explicit
  `hc wait <t>` before first send.

### 1.2 Control surface (Agent Automation API)

| Primitive | hc command | Notes |
|---|---|---|
| Fire-and-stream program | `hc run <cmd>` (fg: streams, returns exit code; `--bg`: detached, note on exit) | one quoted string = shell command |
| Send input | `hc send <t> "text"` / `hc send --wait <t>` | `--wait` blocks until processed; `not_started` result means read-before-resend |
| Wait | `hc wait <t>` · `hc wait <t> --output X` (matches output printed **after** wait starts) · `hc wait --children all|any` | returns `reason: message|timeout|stopped|exited`; timeout=check-in, not failure |
| Read | `hc read <t> --lines N` (`--lines 0` = screen only) | history reads scroll fullscreen apps then restore view; while status is `working|blocked|error`, history read returns screen only, marked incomplete `why=<status>` |
| Interrupt turn | `hc interrupt <t>` | stops current turn, not the process |
| Human takeover | `hc attach <t>` (Ctrl+\ detaches) | multiple viewers per terminal; active viewer owns size |
| Lifecycle | `hc kill <t>`, `hc rm <t>`, `hc keep <t>` (replace lost program with shell — recovery, never re-runs task) | signal-death without exit code = "loss" → grace-delayed recovery incl. after host shutdown |
| Files/exec | `hc exec --on <m>`, `hc file read|write|edit --on <m>`, `hc put <t> <file>` | `file edit` = exact-replace, multi-pair atomic all-or-nothing |
| Diagnostics | `herdr agent explain <pane> --json`, `herdr agent read --source detection --format text|ansi` | shows manifest source, matched rules, evidence flags — the detection-debug loop |

Errors are structured: `{error:{kind,message,hint}}` on stderr + meaningful exit codes
(3 `not_found`, 7 `needs_target`, 9 `blocked`, 10 `status_mismatch`, 11 `finished`).
Core rule: *"Every action reports what was observed, never what was assumed."*

### 1.3 Status — two-layer detection

- **Runtime layer (herdr-core):** status comes **only** from OSC 7501 Program Status
  Protocol escape sequences self-reported by the program → `idle|working|done|blocked|error`.
  No screen detection, no agent registry, no vendor logic in core. *"Do not guess status
  from screen text."* Plain programs report no status.
- **App layer (herdr v1):** TOML **detection manifests** per agent — sampled from the
  terminal's *bottom buffer* (never the scrollable viewport):

```toml
[[rules]]
id = "permission_prompt"
state = "blocked"
priority = 290
region = "bottom_non_empty_lines(8)"          # also: osc_title, last_non_empty_above_prompt_box
visible_blocker = true
contains = ["approve once", "select", "confirm", "esc cancel"]   # AND
any = [{ line_regex = [...] }]                                   # OR
not = [{ contains = ["running tools"] }]                         # NOT
```

  - Manifest fields: `id`, `version` (date-stamped), `min_engine_version`, `aliases`,
    prioritized `[[rules]]` with `state`, `region`, gates (`contains`/`any`/`all`/`not`/
    `regex`/`line_regex`), flags `visible_blocker|visible_working|visible_idle`.
  - **Distribution:** bundled manifests + remotely-published catalog
    (`distribution/agent-detection/` + `index.toml` with `schema_version`) + local
    override path `~/.config/herdr/agent-detection/<agent>.toml` + hot reload
    (`herdr server reload-agent-manifests`). Catalog ships rule fixes **without a client
    release**; unpublished agents allowed behind exact version+digest exceptions.
  - States aggregate upward: pane → tab → workspace. `blocked` = recognized approval/
    question UI; `unknown` = present but unclassifiable (≠ proof of completion).
  - Official integrations (`pane.report_agent` hooks) **outrank** heuristics when present.
  - Suspended (Ctrl+Z) agent keeps identity while the exact process lives; another
    recognized agent taking the pane does not inherit it.
  - Per-client "seen" semantics: CLI/API `idle` vs `done` distinguished by server seen-flag;
    each TUI client tracks viewed completions independently.

## 2. How agents interact with each other (hc protocol)

- **Addressability:** `$HC_TERMINAL_ID` injected into every managed terminal — the agent
  knows its own address and can be told *"report with `hc send $HC_TERMINAL_ID`"*.
  Children spawned from a terminal become its **children** (lineage via env inheritance
  or explicit `--parent`/`--no-parent`).
- **Lifecycle coupling:** children die with the parent by default; `--outlive-parent`
  detaches.
- **Messaging:** `hc send <t>` (parent↔child, sibling↔sibling). Messages arrive wrapped in
  provenance tags: `<hc from="t7 review" reach="hc send t3 when done">…</hc>` — the
  receiver treats them as *child reports, not user instructions*.
- **Notes:** async notifications pushed to the parent when a child blocks / errors / exits —
  the parent learns about child state changes inside its own input stream, no polling.
  `--no-notify` opt-out for terminals you wait on yourself.
- **Waiting:** `hc wait --children all|any` — "the next child's news or a message to you";
  a wait interrupted by a message returns `reason: message`, message arrives as next input.
- **Outbox:** messages to a disconnected peer/parent are queued in a per-peer outbox and
  flushed on reconnect (`peer_joined` drains `Outbox`).
- **Manual conventions** (in `hc --skill`): give each child a name + exact scope; *"Do not
  let children start more agents unless you explicitly assign that"*; long outputs go to a
  file, send the path; never infer success — `hc read` the actual result; kill+rm helpers
  after collecting; **run slow commands in hc terminals instead of blocking your own shell
  tool — while blocked you receive no messages and no children's reports.**

## 3. Remote machines & topology

- `hc machine add user@host [--via <cmd>]` → handshake runs `herdr-core connect` remotely;
  if missing/incompatible, **remote install** copies the binary (matching arch) or downloads
  the right release, restarts the remote core, preserves running terminals.
- Cross-machine addressing: `machine/t7`; `hc run --on vps`, `hc exec/file … --on vps`.
- **Line** = framed bidirectional connection multiplexing RPC calls, viewer stream relays,
  event mirroring, heartbeats + `keep_up` reconnect supervision.
- **Mirror** = local replica of a remote machine's terminal set (subscribed events keep it
  in sync); **Peer** = a remote core in a bidirectional relationship; **Outbox** per
  disconnected peer.
- Sessions are isolated namespaces (`HC_SESSION`, scratch XDG in tests). Core restart
  **adopts** existing terminals (update without killing work — `hc update` restarts core,
  terminals keep running).

## 4. Side-by-side vs Harness

| Aspect | Herdr/hc | Harness (today) | Coverage |
|---|---|---|---|
| Agent execution unit | Persistent interactive terminal (attachable, steerable, multi-turn) | `StreamingProcessRunner` one-shot process per dispatch; `PtySession` only for Terminal page | ❌ **MIGRAR** — delegated run ≠ terminal |
| Send input mid-run | `hc send`/`send --wait`/`interrupt` into a live turn | cancel/kill only; no mid-run steering | ❌ **MIGRAR** |
| Blocking waits | `wait <t>`, `wait --output X`, `wait --children` w/ reason codes | poll `delegate_status`/`agent_inbox`; no server-side wait/output-match | ❌ **MIGRAR** (contract-v2 has ask/reply only) |
| Read output | `hc read` screen+history, incomplete-marked while working | log stream + `GetLogsAsync` history; no screen snapshot semantics | ⚠️ partial |
| Human takeover | `hc attach` multi-viewer, Ctrl+\ detach | delegated run not attachable; Terminal page separate world | ❌ **MIGRAR** |
| Status authority | OSC 7501 reported status in core; heuristics only in app layer | `AgentRunState` lifecycle + attention-model draft (OSC titles+hooks+idle) | ⚠️ layering mixed; no reported-status protocol |
| Weak-signal detection | **TOML manifest catalog** — 20+ agents, priorities, regions, AND/OR/NOT gates, remote distribution, hot reload, local overrides, `agent explain` | hardcoded `AgentCliAttentionPatterns` table in draft spec | ❌ **MIGRAR** — data-driven catalog |
| Detection debuggability | `agent explain` (matched rules, evidence), `agent read --source detection`, manifest hot-reload repro loop (`herdr-throwaway-repro` skill) | none specified | ❌ **MIGRAR** |
| Agent self-address | `$HC_TERMINAL_ID` env injected; manual teaches "report with hc send $HC_TERMINAL_ID" | prompt preamble carries ids (contract-v2 `PreambleJson`); env vars stripped not injected | ❌ **MIGRAR** — env context |
| Task templates | `templates/<name>.md` frontmatter+body, `hc templates`, first-message compose, `first_message.sent` persisted | SDD worker-brief spec only; no named reusable templates | ❌ **MIGRAR** |
| Lineage | parent/child via env, `--outlive-parent`, lifecycle coupling | flat task DAG; no parent linkage between dispatches | ❌ **MIGRAR** |
| Child-event push | **notes** → parent's input stream on blocked/error/exit; `--no-notify` | `worker_done`/`escalation` mailbox msgs on settle only | ⚠️ partial (no blocked/exit push, no crash note) |
| Provenance envelope | `<hc from="id name" reach="...">` tags on every inter-agent msg | `FromAgent` field on mailbox row | ⚠️ field exists; no in-band envelope for prompt-transported msgs |
| Offline delivery | per-peer **Outbox** flushed on reconnect | Delivery+ack in contract-v2 draft (same-scope only) | ⚠️ draft covers scope, not cross-host |
| Crash recovery | holder processes, adoption on restart, `hc keep` shell-replacement recovery, `first_message.sent` durable | hibernation/resume spec; dispatcher heartbeat sweep marks `failed` | ⚠️ resume yes; adopt-in-place no |
| Remote machines | `hc machine add` SSH/`--via`, remote install/bootstrap, `machine/t` addressing, Line+Mirror+Peer+Outbox | remote-hosts draft = paired harness-servers (heavy) | ⚠️ draft exists; lightweight SSH path missing |
| Remote ops | `exec`, `file read/write/edit` (atomic multi-edit), `put` | headless-one-shot draft covers `run` only | ❌ **MIGRAR** |
| API/CLI surface | **one method table** → OpenRPC `rpc.discover` + CLI args + schema, drift-checked (`HC_UPDATE_SCHEMA=1 cargo test m0`) | REST hand-mapped + MCP tools + hand-written taskctl (SPEC-20261101 drift fix exists) | ⚠️ drift-prone (known) |
| Agent-facing docs | `hc --skill` prints embedded manual; installable skill = stub pointing to it — docs ship with binary, always version-matched | `manage-taskboard` SKILL.md duplicates CLI surface | ❌ **MIGRAR** — self-manual |
| Endpoint compat | named immutable codecs, `endpoint_generation` in update manifests, frozen `endpoint-method-shapes-v1.json`, `Unknown` enum fallbacks | none formalized | ⚠️ candidate |
| Errors | `{kind,message,hint}` + stable exit codes; "observed, never assumed" | ProblemDetails on REST; agent-surface errors ad hoc | ⚠️ candidate |

## 5. What to migrate — ranked

**P1 (runtime truth — changes the mental model):**

1. `SPEC-20261104-run-terminal-runtime` — delegated runs as persistent steerable
   terminals: dispatch spawns interactive session, `run_send/run_read/run_wait/
   run_interrupt` tools, human attach/takeover, structured observed-state errors.
   *The single biggest gap — everything else (notes, waits, takeover) hangs off it.*
2. `SPEC-20261104-agent-context-templates` — `HARNESS_*` env injection (session/task/
   dispatch ids, mailbox, API url, scoped token) + `taskctl agent` callback surface +
   named templates with `first_message.sent`-style durable delivery record.

**P2 (attention layer — data-driven):**

3. `SPEC-20261104-agent-attention-manifests` — TOML detection manifest catalog
   (regions/priorities/gates), local overrides, hot reload, remote distribution,
   `agent explain` diagnostics; reported status outranks heuristics.
4. `SPEC-20261104-agent-lineage-notes` — parent/child lineage + lifecycle coupling,
   notes→parent on child blocked/error/exit, provenance envelope, per-scope outbox.

**P3 (reach + maintainability):**

5. `SPEC-20261104-remote-ops-machines` — `taskctl exec/file/put`, `machine add` SSH
   bootstrap + lightweight remote runtime (complements paired-server remote-hosts).
6. `SPEC-20261104-cli-agent-manual` — `taskctl --skill` embedded manual + stub skill +
   CI drift check (companion to SPEC-20261101-taskctl-skill-cli-sync).

**Watchlist (not specced now):** immutable named API codecs + `endpoint_generation`
+ frozen wire fixtures (when the plugin/MCP surface stabilizes); `hc keep`-style
shell-replacement recovery for lost terminals; `hc --children` DAG-aware waits once
lineage lands (covered by spec 4); OSC 7501 as our explicit status channel (fold into
attention-manifests precedence rules).
