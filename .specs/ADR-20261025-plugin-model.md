# ADR-20261025 — Chat tool plugin model: MCP-only vs in-process

Status: **accepted** · Date: 2026-10-25 · Closes: SPEC-20261025 C5 / T14

## Context

The chat tool surface (`IChatTool`) grew through in-repo builtins plus MCP
adapters. Two extension models compete for where new capabilities should
live:

- **MCP-only** — every extension is an external MCP server; the host only
  knows `mcp_*` adapters.
- **In-process** — extensions ship as `IChatTool` implementations loaded in
  the host (today: DI-registered builtins; a plugin ABI would allow
  third-party assemblies).

## Options

### Option 1 — MCP-only (strict)

All non-core tools must be MCP servers.

- **Security**: hard process boundary — a hostile/buggy server can't read
  harness memory, credentials, or the DB; the sandbox/risk-tier pipeline
  already governs its tools.
- **Stability**: crashes contained; restart = reconnect.
- **Cost**: per-call JSON-RPC + stdio hop; server lifecycle to babysit;
  schema friction (MCP `inputSchema` is less expressive than our tool
  schema); slower iteration — a first-party idea needs a repo + server +
  registration before the agent can try it.
- **Fit**: already proven — `mcp_*` adapters, per-server health, deferred
  catalog integration all exist.

### Option 2 — In-process plugins (open ABI)

Any assembly implementing `IChatTool` gets loaded into the host.

- **Speed**: zero IPC; full DI (repositories, config, clock, redactor).
- **Risk**: a plugin runs with the server's identity — file system,
  credentials (`Taskboard:*` keys in `IConfiguration`, provider keys),
  DB access. Our path-jail/denylist protect *tool arguments*, not the
  plugin's own code. A crash or `Task` leak takes down every conversation.
- **Cost**: needs a real ABI (versioning, unload, isolation via
  `AssemblyLoadContext` — still not a security boundary in .NET).

### Option 3 — Hybrid (chosen)

**Trust boundary = code origin, not packaging.**

- **First-party packs** (code authored in this repo, same review/CI gate,
  shipped as part of the product) MAY be in-process `IChatTool`s —
  `lsp`, `browser_use`, `code_interpreter` are already this shape. They
  inherit the gateway, path-jail, denylist, sandbox, deferred catalog and
  kill list for free.
- **Third-party extensions** MUST be MCP. The process boundary is the
  security boundary; an MCP server receives only what its tools' schemas
  declare. No third-party assembly ever loads into the host.

## Decision

**Option 3 — hybrid.** Rationale:

- The isolation we actually need is *from code we didn't review* —
  exactly what MCP provides.
- Forcing first-party tools out-of-process buys no security (we already
  trust the code) and pays the IPC/lifecycle tax on every call — the
  measured cost that motivated the deferred-tool work in P0.
- An open in-process ABI is the worst quadrant: maximum capability with
  minimum isolation. We explicitly decline it.

## Consequences

- No plugin loader / `AssemblyLoadContext` work — out of scope.
- New first-party tools follow the existing pattern: `IChatTool` impl in
  `Integrations`, DI registration in `Program.cs`, deferred-catalog
  membership when the schema is heavy/conditional, tests.
- Third-party capabilities land as MCP servers + `mcp_*` adapter; they get
  health-probe gating and deferred catalog automatically.
- If a future need demands third-party *in-process* speed, the answer is a
  richer MCP server or a hosted first-party bridge — not an ABI.

## Boundaries reaffirmed

- `ChatCapabilityRules` kill list + pins apply to both sides.
- Approval/risk-tier pipeline is unaffected — MCP tools and builtins flow
  through the same coordinator.
- This ADR decides *extension packaging only*; it does not relax any P0/P1
  security mechanism.
