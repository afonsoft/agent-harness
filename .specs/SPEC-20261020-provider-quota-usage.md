# SPEC-20261020-provider-quota-usage: per-provider quota windows + warn@80% in status bar/FinOps

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Provider quota/usage — janelas por provider + alerta 80% |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Integrations (usage collectors) + Application + Blazor (status bar, FinOps page) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/provider-quota-usage` |
| Status | `Draft` |
| Depends on | FinOps/token metrics existentes (`AgentLog` token counts); SPEC-20261020-agent-attention-model para surfacing no status |
| Reference | Orca `docs/usage-tracking` + `docs/notifications` (warn @80%); COMPARISON-20261020-orca-delegation-board §1 |

## 1. Executive Summary

### Problem

FinOps atual conta tokens/custo por run, mas não mostra **quota de plano**
dos providers — Claude tem janelas 5h/daily/weekly, Codex rate limits,
OpenAI TPM/RPM. O usuário descobre o limite quando o agente falha
`rate_limited`. Orca expõe na status bar um roster por provider com janelas
(5h/daily/weekly), resets e warn a 80%.

### Solution

`ProviderUsageService` — coletor por provider que popula
`ProviderUsageWindow { Provider, WindowKind(fiveHour|daily|weekly|rate),
  Used, Limit, UsedPct, ResetsAtUtc, SampledAtUtc, Source(api|cli|local_estimate) }`:

- **Sources**: API oficial quando disponível (Anthropic `/usage`, OpenAI
  `/rate_limits` headers, GitHub Copilot); fallback CLI (`claude /usage`,
  `codex /limits` output parse); fallback local estimate (token count agregado
  das nossas `AgentLog`s vs limite conhecido).
- **Status bar roster**: strip no header das páginas Agents/Cockpit mostrando
  cada provider com `UsedPct` + warn badge ≥80% + tooltip com resets.
- **Warn event**: cruza 80% → feed item `quota_warning` + (opcional)
  notification; cruza 100% → `quota_exhausted` + sugestão de switch de CLI.
- **FinOps page**: seção "Quota & limits" com tabela por provider+window.

### Scope

In scope: Claude (Anthropic), Codex/OpenAI, OpenCode (local — sem quota,
marca `n/a`), Copilot; sources api+cli+local_estimate; roster + FinOps
section + warn feed. Out of scope: billing/account management, quota
prediction ML, multi-account pooling.

## 2. Requisitos

### Funcionais

- RF-001 `ProviderUsageWindow` entity: `Provider`, `WindowKind`, `Used`,
  `Limit`, `ResetsAtUtc`, `SampledAtUtc`, `Source`, `Confidence
  (measured|estimated)`, `RawJson?` (≤2KB para debug).
- RF-002 `IProviderUsageCollector` por provider:
  `CollectAsync() → ProviderUsageWindow[]`; Claude: parse `claude /usage`
  output (5h/daily/weekly) + Anthropic API se `ANTHROPIC_API_KEY` válida;
  Codex/OpenAI: rate-limit headers das últimas respostas (persistidos em
  `AgentLog.Metadata`) + `/usage` API; Copilot: API `copilot/usage`.
- RF-003 Fallback chain: `api` → `cli` → `local_estimate`; cada coletor
  retorna `WindowKind[]` com `Confidence`; `local_estimate` usa soma
  `AgentLog.Tokens` na janela vs limite conhecido do plano (tabela
  `KnownPlanLimits` — Claude Pro/Max, OpenAI tiers).
- RF-004 Sampling: coletores rodam a cada `SamplingIntervalMinutes`
  (default 5) via background job; `SampledAtUtc` >15min → UI marca
  "stale" no badge.
- RF-005 `GET /api/usage/providers` → roster atual `[{provider, windows[],
  sampledAtUtc, stale}]`; `GET /api/usage/providers/{provider}` → detail.
- RF-006 Status bar strip (`ProviderUsageStrip.razor`): badge por provider
  com `max(UsedPct)`; cor: <50% muted, 50-80% amber, ≥80% red pulsante;
  tooltip: janelas + resets + source/confidence.
- RF-007 Warn events: `UsedPct` cruza 80% (histerese 5% — precisa cair
  abaixo de 75% para re-armar) → feed `quota_warning` + sound se prefs;
  cruza 100% → `quota_exhausted` + Needs You no card afetado com
  "Switch to another CLI" action.
- RF-008 FinOps `/finops` seção "Quota & limits": tabela por provider com
  janelas, trend sparkline (últimos 10 samples), botão "Refresh now"
  (`POST /api/usage/refresh`).
- RF-009 `KnownPlanLimits` em config (`Taskboard:PlanLimits`) — usuário
  informa plano por provider; sem config → `local_estimate` usa
  limite conservador default + `confidence=estimated`.
- RF-010 Sem quota (OpenCode local, model self-hosted) → window
  `{windowKind: n/a, used: -1}` omitido do roster.

### Não-funcionais

- Coletor nunca loga tokens/keys — só números de uso; `RawJson` sanitizado
  (strip `Authorization`, `x-api-key` keys antes de persistir).
- CLI parse é read-only (`claude /usage` não muta estado); timeout 10s.
- Job tolerante a provider offline — falha → `SampledAtUtc` envelhece,
  badge fica "stale", nunca throw.
- Roster payload ≤4KB.

## 3. Arquitetura

```mermaid
flowchart LR
  Job[UsageCollectJob\n5min] --> C1[ClaudeCollector\n/cli usage + API]
  Job --> C2[OpenAICollector\nheaders + API]
  Job --> C3[CopilotCollector]
  C1 --> DB[(ProviderUsageWindow)]
  C2 --> DB
  C3 --> DB
  DB --> Strip[StatusBar strip]
  DB --> FinOps[/finops quota section]
  DB -->|≥80%| Warn[feed quota_warning]
```

- `ProviderUsageService` (Application) orquestra collectors + warn emit.
- `IProviderUsageCollector` em Integrations (um arquivo por provider,
  mesma família dos extractors).
- `KnownPlanLimits` via `IOptions<TaskboardOptions>`.

## 4. Fases

- **P1** — entity+job+Claude collector (cli parse) + strip + warn event.
- **P2** — OpenAI/Codex + Copilot collectors + FinOps section +
  local_estimate fallback.
- **P3** — Anthropic API direct + plan limits config UI + refresh manual.

## 5. Testes

- Unit: parse `claude /usage` output (fixtures — 5h/daily/weekly, formatos
  de reset "resets in 3h", "resets at 5pm"); fallback api→cli→local.
- Unit: warn hysteresis (79→81 emite, 81→79 não, 79→77→81 emite de novo).
- Integration: job persiste windows; roster endpoint retorna; stale marca;
  warn cruza → feed item.
- Integration: provider sem quota omitido; falha de coletor não quebra job.

## 6. Open questions

1. Quota por *conta* quando múltiplas API keys do mesmo provider?
   Proposta: v1 agrega por provider; multi-account é P4 com `ProviderKey`
   no window.
2. `quota_exhausted` deve auto-sugerir dispatch em outro CLI
   (`delegate_task --cli codex` quando claude 100%)? Proposta: v1 mostra
   ação no card, não executa; auto-switch é P4 opt-in.
3. Alertas via notification OS? Proposta: reutiliza
   `SPEC-20261020-agents-feed-notifications` (feed+sound+preferências).
