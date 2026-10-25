# SPEC-20261025-p2-product-decisions — P2 product-decision tranche

> Status: Draft (prioritization pending)
> Priority: P2 — product decisions, not gap fixes
> Source: `.specs/COMPARISON-20261023-hermes-openharness-ecc.md` §P2
> Completed waves: P0 (SPEC-20261023, merged) · P1 (SPEC-20261024, merged)

## 1. Context

P0+P1 closed every gap identified against hermes-agent/OpenHarness/ecc. What
remains are **product choices** — each item below is framed with a requirement
sketch, the decision it needs, and effort/risk notes. Slices are independent;
pick any subset, in any order.

## 2. Candidates

### C1 — Chat channels (Slack/Discord bridge)

- **RF sketch**: `IChatChannel` provider contract; inbound channel messages land
  as mailbox payloads (`agent_inbox`) or conversation user-messages; outbound
  run events (approvals, completions) post back to the channel. Slack first
  (Socket Mode — no public URL needed), Discord second.
- **Decision**: which platform first; whether approvals over chat are allowed
  (HITL from a phone is the killer feature — approve a `shell_exec` from Slack).
- **Effort**: M — new channel adapter + settings UI + secret storage for bot tokens.
- **Risk**: approval-over-channel must keep the same gate semantics (audit trail,
  actor = approving user, not the bot).

### C2 — Docker sandbox for chat tools

- **RF sketch**: opt-in `Taskboard:Chat:Sandbox=off|docker`; when on,
  `shell_exec`/`code_interpreter`/`run_tests` execute inside a container —
  worktree bind-mounted read-write, `$HOME`/`~/.agent-harness` not mounted,
  no network by default (`--network none` per-tool allowlist), resource caps
  (`--memory`, `--cpus`, `--pids-limit`).
- **Decision**: base image policy (a `harness-sandbox` image built from the repo
  vs. stock `mcr.microsoft.com/dotnet/runtime`); whether worktrees get network
  for `git`/`dotnet restore` (needed for realistic test runs).
- **Effort**: M — `SandboxedProcessRunner` next to `ChatProcessRunner`, preflight
  check for the docker socket, fallback to host with a visible warning.
- **Risk**: must not break the existing risk-tier/approval pipeline — sandbox is
  a second layer, not a replacement.

### C3 — Skills catalog v2 (pins + kill list)

- **RF sketch**: per-skill metadata — `pinned` (always in the ≤40 prompt cap,
  bypassing ordering) and `disabled` (kill list per environment via config);
  Settings UI shows pin state.
- **Decision**: pin count cap (keep ≤5 to protect the prompt); whether the kill
  list is global config or per-conversation.
- **Effort**: S — `SkillDiscoveryService` filter + config keys
  (`Taskboard:Chat:Skills:Pinned`, `:Disabled`) + Settings chips.
- **Risk**: low; isolated to skill listing.

### C4 — Tool LSP (language-server tool)

- **RF sketch**: `lsp` chat tool exposing `goto_definition|find_references|
  document_symbols|workspace_symbols|diagnostics` over an LSP client bound to
  the workspace (C# via `csharp-ls` or OmniSharp first; generic `server`
  config per language later).
- **Decision**: which language server ships first; session lifetime (one LSP
  process per workspace, idle-reaped like the browser pool).
- **Effort**: M/L — LSP client plumbing (JSON-RPC stdio) is the bulk.
- **Risk**: deferred-tool candidate on day one (schema in catalog, activated on
  demand) — keeps the default footprint flat.

### C5 — Plugin model decision (MCP-only vs in-process plugins)

- **RF sketch**: not code — an ADR. Compare MCP-only (status quo: process
  isolation, stdio safety, slower iteration, schema friction) vs. in-process
  plugins (NuGet-loaded `IChatTool` assemblies: faster iteration, full API
  surface, but the harness's stability now depends on plugin quality — the
  exact trade-off OpenHarness accepts and hermes rejects).
- **Decision**: pick one, or a hybrid (in-process for first-party optional
  packs only, MCP for anything third-party — the model hermes effectively
  uses for gateway adapters).
- **Effort**: S (write the ADR).
- **Risk**: the decision shapes every future extension point; worth deliberating.

## 3. Recommendation

Order by value/effort: **C3 (S, immediate) → C2 (M, hardens the exec surface
P0 already tightened) → C1 (M, unlocks mobile HITL — biggest UX lever) →
C4 (M/L) → C5 (ADR anytime)**. Suggested tranche: C3+C2 as "P2a hardening/
catalog", C1 as "P2b channels", C4+C5 as "P2c extensibility".

## 4. Acceptance criteria (per-slice, filled when picked)

- [x] C3: a pinned skill survives the ≤40 cap; a disabled skill never reaches
  the catalog; both toggleable via config + Settings.
- [x] C2: `Sandbox=docker` routes an `echo` through a container (asserted in
  test with a fake runner); worktree writes land on the host; `$HOME` files
  are unreachable; docker-socket absence falls back to host + WARN.
- [x] C1: a Slack message becomes a conversation user-message; a pending
  approval card renders an Approve/Reject action in-channel; the decision
  lands on the same `ChatApproval` row as a UI click.
- [x] C4: `lsp document_symbols` on the workspace returns symbols via the
  deferred-tool path (activate → schema → call).
- [ ] C5: ADR merged under `.specs/` recording the decision + consequences.

## 5. Task plan

- [x] **T10 (C3)** — skills pins + kill list.
- [x] **T11 (C2)** — docker sandbox runner + preflight + fallback.
- [x] **T12 (C1)** — Slack channel adapter + remote approvals.
- [x] **T13 (C4)** — LSP tool (deferred-catalog citizen).
- [ ] **T14 (C5)** — plugin model ADR.
