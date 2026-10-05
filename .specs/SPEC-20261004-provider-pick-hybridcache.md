# SPEC-20261004 — AI Code: provider auto-select + HybridCache catalog

Status: Approved (requested inline by afonsoft)
Scope: `ai-chat` provider chat UX + server-side catalog caching

## Problem

1. `ProviderChat` defaulted `_providerId = _providers[0]` — the catalog is
   ordered by name, so the first entry can be a disabled or keyless provider
   the composer can't actually send with.
2. Every page load re-hit SQLite for the provider list, re-probed the
   provider's `GET /models` HTTP endpoint, and re-read the custom agent defs.

## Requirements

- **RF-001 — pick a usable provider.** New `ChatProviderPick.PreferAvailable`
  (Application.Contracts): first `Enabled && HasApiKey`, then `Enabled`, then
  `providers[0]` as before (a fully-unavailable list keeps old behavior — the
  disabled-provider error still surfaces on send). `ProviderChat` init uses it.
- **RF-002 — HybridCache for the chat catalog.** `ChatService` takes
  `HybridCache`: `ListProvidersAsync` cached 60s under key `chat-providers`;
  `ListModelsAsync(providerId)` cached 5min under `chat-provider-models-{id}`
  (the `Cached` flag on `ChatModelListDto` now reports the cache layer —
  `true`). Writes (`Create`/`Update`/`DeleteProviderAsync`) evict tag
  `chat-providers`; update/delete also remove that provider's models key.
- **RF-003 — agent defs cached.** `GET /api/agents/custom` caches
  `defs.ListAsync` 30s under `agent-custom-defs` (tag `agent-defs`); the
  per-def `Resolved` PATH probe stays per-request (cheap, must be fresh).
  POST/PUT/DELETE custom defs evict the tag.
- **RF-004 — Redis L2.** `AddHybridCache()` registered once in Program.cs;
  when `Taskboard:Cache:Redis` is set, `AddStackExchangeRedisCache` adds the
  `IDistributedCache` HybridCache picks up as L2. Unset → L1 memory only.
- **RF-005 — composer unificado.** The Agent-mode toolbar (`ai-chat-toolbar`)
  moves inside `provider-chat-composer-footer` via a `RenderFragment
  AgentTools` slot on `ProviderChat` (AiChat keeps owning state/handlers).
  Provider+model collapse into ONE pill: icon + model-name label → dropdown
  panel with two stages — provider list (skipped when a single provider is
  registered) then model list. Disabled providers show "disabled", keyless
  "no key"; the pill text is just the model name.
- **Non-goals.** CLI model probes (`builtin/{type}/models`,
  `custom/{id}/models`) already TTL-cache in `CliProbeSnapshotService`
  (memory + disk snapshot + single-flight subprocess refresh) — that owner
  owns bounded subprocess semantics, not duplicated into HybridCache.
  Conversation list/messages stay uncached (user data, high churn).

## Tests

- `ProviderPickTests` — enabled+key over alphabetical first; enabled over
  disabled; no-key over disabled; all-disabled → first; empty → null; razor
  source guard (`PreferAvailable` wired in `ProviderChat`).
- `ChatServiceTests` — models served from cache without a 2nd HTTP call;
  direct-context insert stays invisible until a write invalidates; provider
  update invalidates the models cache.
