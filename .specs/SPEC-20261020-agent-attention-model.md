# SPEC-20261020-agent-attention-model: unified agent state detection (working/waiting/done/idle/blocked)

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Agent attention model — detecção unificada de estado por sessão |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Domain + Application + Integrations (CLI adapters) + Server (hub) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/agent-attention-model` |
| Status | `Draft` |
| Depends on | — (fundamento para SPEC-20261020-agent-board-v2 e SPEC-20261020-agents-feed-notifications) |
| Reference | Orca `docs/model/agents-sessions` (OSC titles + managed hooks + idle heuristics); `src/main/agent-hooks/**`; COMPARISON-20261020-orca-delegation-board §1 |

## 1. Executive Summary

### Problem

Estado de atenção hoje é fragmentado: `AgentRunState` cobre lifecycle de execução
(running/succeeded/failed) e "Needs You" só existe quando um `decision` chega na
mailbox — ou seja, só agentes *delegados* que explicitamente pedem decisão.
Um CLI rodando na Terminal page pedindo permissão, ou um worker travado numa
pergunta do próprio CLI (Claude permission prompt, Codex approval), não gera
sinal nenhum. O Orca resolve isso com uma camada de detecção por sessão:
hooks gerenciados instalados na config do CLI + OSC title sequences + heurística
de output-idle, tudo gravado num status store único que toda UI assina.

### Solution

`AgentSessionStateStore` — fonte única de verdade para estado de atenção de cada
sessão de agente (run delegado, sessão interativa da Terminal page, sessão de
chat). Estado derivado por uma pipeline de sinais, por CLI:

1. **Sinais fortes (hooks/structured)**: para CLIs com hooks nativos (Claude
   `~/.claude` hooks, Codex notify, OpenCode plugin) instalar hook gerenciado
   `harness-agent-hook` que POSTa `{sessionId, event(permission|question|
   turn_end|turn_start), payload}` no endpoint `/api/agent-hooks`. Onde não há
   hook nativo, parse de OSC title quando o emulador de terminal capturar
   sequências (`TerminalSessionManager`).
2. **Sinais fracos (heurística de saída)**: sem output por `IdleAfterMs`
   (default 30s) durante run → `idle`; último chunk de stdout match padrão de
   pergunta/permissão do CLI (tabela `AgentCliAttentionPatterns`: "Do you want
   to proceed?", "approve?", "(y/n)", "Press Enter to confirm") → `waiting_you`.
3. **Sinais do Harness**: mailbox `decision`/`question` → `waiting_you`;
   `worker_done` → `done`; `escalation` → `blocked`; heartbeat → `working`;
   run `Succeeded/Failed/BudgetExceeded` → `done/blocked`.

Estados publicados: `working`, `waiting_you`, `done`, `idle`, `blocked`,
`exited` (processo terminou), `unverifiable` (fonte perdida — nunca inferir
morte por silêncio em sessão remota/federada). Todo consumidor (board, menu
badge, feed, notificações) lê do store via SignalR `agent.state`.

### Scope

In scope: estado por sessão delegada + interativa local; hook gerenciado para
Claude e Codex (v1), padrões de texto por CLI; pipeline de transição com
histerese. Out of scope: hooks para todos os 13 CLIs (registry extensível),
sessões remotas/SSH (`unverifiable` apenas), detecção de sub-agents aninhados.

## 2. Requisitos

### Funcionais

- RF-001 `AgentSessionState` entity: `SessionKey (kind:id)`, `Cli`, `State`,
  `AttentionReason?`, `StateChangedAtUtc`, `LastSignalAtUtc`,
  `LastSignalKind(hook|osc|output_pattern|idle|mailbox|lifecycle)`,
  `Evidence?` (≤200c — ex.: texto da pergunta detectada), `Version` (optimistic
  concurrency).
- RF-002 Pipeline `IAgentAttentionEvaluator.Evaluate(signal)` → aplica regras
  de transição com histerese (≥2 sinais fracos consecutivos ou 1 forte antes de
  `waiting_you`; ≥1 heartbeat/output em `<10s` sai de `idle` para `working`).
- RF-003 `POST /api/agent-hooks` (auth token por sessão em `agent-hooks/
  endpoint.env` equivalente: `HARNESS_AGENT_HOOK_TOKEN` injetado no env do
  processo filho) aceita eventos de hook gerenciado; evento com token inválido
  → 401, nunca atualiza estado.
- RF-004 Hook installer `AgentHookInstaller.InstallFor(cli, sessionKey)`:
  Claude → grava/merge `SessionStart`+`PermissionRequest`+`Stop` hook em
  `.claude/settings.json` do worktree (managed block `"harness"`, nunca toca
  hooks do usuário); Codex → `notify` program + config merge em `config.toml`
  managed block. Uninstall/remove ao fim da sessão ou na saída limpa.
- RF-005 `AgentCliAttentionPatterns` (Domain.Shared registry por `AgentCliKind`)
  — lista de regex com tipo (`permission`,`question`,`prompt_input`) e
  prioridade; matching no último ≤4KB do buffer de saída, debounce 2s.
- RF-006 OSC title: `TerminalSessionManager` já captura título quando emissor;
  mapear títulos conhecidos por CLI (ex.: Claude muda título em tool use) para
  sinal `osc`; título desconhecido = sinal fraco `alive`.
- RF-007 Transições de lifecycle (`AgentRun`/`DelegationTask`/`TerminalSession`)
  publicam sinais `lifecycle` no evaluator — processo exit → `exited` (nunca
  `done` sem worker_done/run-state).
- RF-008 Store expõe `GET /api/agents/state?keys=...` (snapshot filtrado) +
  SignalR `agent.state` (patch `{key,state,reason,atUtc}`); consumidores
  subscrevem por sessão ou escopo.
- RF-009 `waiting_you` alimenta Needs You: `AgentSessionState.State ==
  waiting_you || blocked` entra na coluna Needs You do board independente de
  mailbox decision (com `AttentionReason` no card).
- RF-010 Sem falso-positivo de morte: ausência de sinal nunca vira `exited` —
  apenas `unverifiable` após `UnverifiableAfterMs` (default 5min) sem
  heartbeat/process-check; `exited` só de process wait/exit code.

### Não-funcionais

- Evaluator é single-threaded por SessionKey (channel serializado) — sem race
  entre heartbeat e output_pattern.
- Estado volátil em memória + persistência só de transições
  (`AgentSessionStateTransition` append-only p/ feed/auditoria); restore após
  restart = `unverifiable` até primeiro sinal.
- Overhead por sessão: ≤1 avaliação/2s em burst, regex compiled, buffer ring
  4KB — nunca varrer log inteiro.
- Token de hook escopo-sessão, não logado, rotaciona por sessão; payload de
  hook sanitizado (strip credenciais óbvias, ≤4KB).

## 3. Arquitetura

```mermaid
flowchart LR
  CLI[Agent CLI proc] -->|stdout ring buffer| Pat[Pattern matcher\nAgentCliAttentionPatterns]
  CLI -->|OSC title| Osc[OSC mapper]
  CLI -->|managed hook POST| HookAPI[/api/agent-hooks]
  MB[AgentMailboxMessage] --> Eval
  LC[Run/Task lifecycle] --> Eval
  Pat --> Eval[AttentionEvaluator\nhisterese]
  Osc --> Eval
  HookAPI --> Eval
  Eval --> Store[(AgentSessionStateStore)]
  Store -->|agent.state patch| Hub[SignalR]
  Hub --> Board[Board/Feed/Badge]
```

- `IAgentAttentionEvaluator` em Application; sinais via `Channel<AttentionSignal>`.
- `AgentHookInstaller` em Integrations (per-CLI adapter, mesma família do
  `IAgentAdapter`); unit-testable via in-memory fs abstraction existente.
- Persistência: `AgentSessionState` row por sessão + `AgentSessionStateTransition`
  events (EF Core, migration nova).

## 4. Fases

- **P1** — Estados + evaluator + sinais mailbox/lifecycle/idle + endpoint
  snapshot + SignalR patch (sem hooks externos ainda).
- **P2** — Pattern matcher por CLI + OSC mapper + managed hook installer
  (Claude + Codex) + `/api/agent-hooks`.
- **P3** — histerese tuning + `unverifiable` sweep job + persistência de
  transições + integração board Needs You.

## 5. Testes

- Unit: máquina de transição (working→waiting_you→working), histerese
  (1 fraco não basta; 2 fracos ou 1 forte sim), `unverifiable` nunca de 1
  silence, `exited` só de lifecycle exit.
- Unit: pattern table match (Claude permission prompt, Codex approval, genérico
  y/n) — fixtures de transcript (sanitizadas, sem conteúdo real).
- Unit: hook installer merge — preserva hooks de usuário, idempotente,
  remove managed block no uninstall.
- Integration: POST /api/agent-hooks com token válido atualiza estado; token
  inválido 401; patch SignalR emitido uma vez por transição.
- Integration: mailbox decision → waiting_you no board snapshot; worker_done →
  done.

## 6. Open questions

1. Instalar hook no worktree vs global do CLI? Proposta: worktree-scoped
   (`.claude/settings.json` do worktree) — não polui config global do usuário.
2. Claude hook precisa de aprovação do usuário no primeiro uso (settings.json
   muda)? Proposta: sim, via attention model próprio — detectar o prompt de
   aprovação e deixar Needs You (hábito Orca: escrever hook só quando muda).
3. `idle` depois de `done` sem interação deve auto-hibernar
   (SPEC-20261020-agent-hibernation)? Proposta: publicar evento
   `agent.idle_timeout` que a hibernação consome.
