# SPEC-20261020-session-history-parity: Sessions tab — transcript search, grouping/sort, resume actions, per-CLI toggles

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Session history v2 — paridade com Orca session list |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Integrations (AgentSessionScanner) + Application + Blazor (Sessions tab) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/session-history-v2` |
| Status | `Draft` |
| Depends on | AgentSessionScanner + CliDatabaseMap existentes; SPEC-20261020-orchestration-contract-v2 para `worker_start --terminal` reuse |
| Reference | Orca `docs/model/agents-sessions` + `docs/cli/overview` (`orca sessions`); COMPARISON-20261020-orca-delegation-board §1 |

## 1. Executive Summary

### Problem

A aba Sessions lista sessões por CLI com resume, mas: sem busca dentro de
transcripts, sem agrupamento (por projeto/data), sem ações de resume variadas
(resume no chat nativo vs CLI vs copiar comando), sem toggles por CLI e sem
"hide empty". O scanner lê metadados de todos os CLIs mas a UI não expõe o
nível de controle que o Orca dá.

### Solution

1. **Busca**: full-text no índice de sessões — título, prompt inicial, cwd,
   branch — + busca *dentro* de transcripts via `rg` no diretório de
   transcripts do CLI (resultado = match context, nunca transcript inteiro).
2. **Grouping + sort**: por CLI / projeto (cwd repo root) / dia; sort por
   modified desc (default), created, title, duration; persistido por usuário.
3. **Resume actions menu** por sessão: `Resume (interactive)` (abre terminal
   com resume cmd), `Resume in native chat` (se CLI tem suporte), `Copy resume
   command`, `Copy session id`, `Copy log path`, `Open log`, `Open cwd`,
   `Delete from history` (confirmação — remove arquivo do scanner do CLI,
   nunca dados do Harness).
4. **Per-CLI toggles + hide empty**: cada CLI descoberto tem toggle
   show/hide (persistido); "Hide CLIs with no sessions" default on.
5. **Session card rico**: título derivado (primeira linha do prompt/user
   msg), cwd→repo name, branch, age, message count, size badge, state glyph
   se sessão está viva no attention model.

### Scope

In scope: UI Sessions tab + scanner index + busca rg + ações. Out of scope:
editar/apagar em bulk, sync de histórico entre máquinas, transcript viewer
completo (usa `Open log` para o arquivo raw).

## 2. Requisitos

### Funcionais

- RF-001 `AgentSessionIndex` (cache server, rebuild 60s ou on-demand):
  `{SessionKey, Cli, Title, Cwd, RepoRoot?, Branch?, CreatedAtUtc,
  ModifiedAtUtc, MessageCount?, SizeBytes, TranscriptPath?, ResumeCommand,
  HasNativeChat}` — reconstruído do `AgentSessionScanner` + metadados
  enriquecidos (repo root via `git -C cwd rev-parse`, branch via
  `git -C cwd branch --show-current`, ambos com timeout 2s e cache por path).
- RF-002 `GET /api/agents/sessions?cli=&group=cli|project|day&sort=
  modified|created|title|duration&q=` — `q` aplica-se ao índice (title/cwd/
  branch); `&transcript=true` adiciona busca rg no transcript dir (limite 100
  matches, timeout 5s, devolve `matchSnippet` ≤160c).
- RF-003 Sessões com `state` viva (attention model) trazem glyph + `live=true`
  no payload — pin no topo do grupo independente de sort.
- RF-004 Actions menu por sessão expõe apenas ações aplicáveis:
  `Resume` sempre (resume cmd existe), `Resume in chat` só se CLI tem
  native-chat adapter, `Open log/cwd` só se `TranscriptPath`/`Cwd` existem,
  `Delete` sempre (com confirmação: "Remove session file from disk?").
- RF-005 Per-CLI visibility: `AgentSessionCliVisibility(userId, cli,
  hidden)` + `hideEmpty` bool em `UserSettings`; toggles no header da aba
  (chip por CLI com count).
- RF-006 Title derivation: primeira linha não-vazia da primeira user message
  do transcript (≤80c, strip markdown/ansi); fallback `session-<shortid>`.
- RF-007 `Resume` abre terminal page com `cwd` da sessão + resume command
  injetado (não executa — cola no prompt, Enter do usuário executa);
  `Resume in chat` cria `AgentRun` native-chat vinculado ao `SessionKey`
  para continuidade de history.
- RF-008 Delete: confirmação com path exibido; remove só o arquivo de
  transcript/session do CLI; sessões vivas (live=true) → delete desabilitado.
- RF-009 Empty states: por grupo ("No sessions for codex" + link pro
  CLI Agents tab) e global; `hideEmpty` colapsa grupos vazios com count
  no header.

### Não-funcionais

- Índice rebuild async, nunca bloqueia UI; primeira carga mostra skeleton +
  stream incremental por CLI conforme scanner retorna.
- rg transcript search: `rg -l --max-count 1` por arquivo (match/not) +
  `-m 3 -C 0` no arquivo para snippet; exclui paths fora de $HOME (mesma
  regra do CliDatabaseLocator).
- Nunca ler >64KB por transcript para title/metadata; head-only parsing.
- Delete é filesystem op auditada (log + feed item `session_deleted`).

## 3. Arquitetura

```mermaid
flowchart LR
  Scanner[AgentSessionScanner\n+enrichers] --> Idx[AgentSessionIndex\ncache 60s]
  Idx --> API[/api/agents/sessions]
  RG[rg transcript search] --> API
  API --> UI[SessionsTab.razor\nchips CLI + group/sort + search]
  UI --> Menu[SessionActionsMenu]
  Menu --> T[Terminal resume]
  Menu --> NC[Native chat resume]
  Menu --> Del[ConfirmDelete]
  Attn[Attention model] -->|live state| Idx
```

- `AgentSessionIndexService` (Application): owns cache + enrich + rg queries.
- `SessionActionsMenu` component reutiliza `Dropdown`/`Menu` Blazor existente.
- `Delete` via `IFileSystem` abstraction (testável).

## 4. Fases

- **P1** — índice enriquecido + grouping/sort + per-CLI chips + hideEmpty +
  title derivation.
- **P2** — search (índice + transcript rg) + actions menu completo +
  live state glyph.
- **P3** — native chat resume + delete + open-log/cwd platform handlers.

## 5. Testes

- Unit: title derivation (markdown strip, ansi strip, fallback); grouping
  por project/day; sort estável; live pin no topo.
- Unit: resume command por CLI (claude `--resume <id>`, codex `resume <id>`,
  opencode `--session <id>`) — já parcial no scanner, cobrir todos.
- Integration: `?q=` filtra; `transcript=true` retorna snippet ≤160c nunca
  conteúdo fora de $HOME; delete remove arquivo + emite feed item.
- Integration: toggles persistem por usuário; hideEmpty colapsa grupos.

## 6. Open questions

1. "Resume in native chat" — qual adapter? Proposta: `IAgentChatAdapter`
   existe para claude/codex/opencode (AcpClient); CLI sem adapter → ação
   oculta.
2. Transcript search indexado (sqlite FTS) ou rg on-demand? Proposta: rg
   on-demand v1 (transcripts crescem; índice FTS é P4).
3. Delete de sessão devin-cli (sqlite row)? Proposta: delete só em CLIs com
   session=1-file (claude jsonl, codex rollout); devin/opencode db →
   desabilitado (não tocar no sqlite do CLI).
