# SPEC-20261020-agents-feed-notifications: global agents feed — unread, badge, mark-unread, preferences, sound

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Agents feed + notifications — modelo unread global |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Application + Server + Blazor (Agents page, menu badge) + Domain |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/agents-feed-notifications` |
| Status | `Draft` |
| Depends on | SPEC-20261020-agent-attention-model (eventos de estado), SPEC-20261020-agent-board-v2 (unread store compartilhado) |
| Reference | Orca `docs/activity` + `docs/notifications`; COMPARISON-20261020-orca-delegation-board §3 |

## 1. Executive Summary

### Problem

O activity feed existe só dentro da aba Dashboard e é reativo (merge de
eventos ao abrir). Não há: (a) modelo de unread — usuário não sabe o que perdeu
desde a última visita; (b) badge na navegação; (c) preferências de notificação
(que categorias geram som/badge/persistência); (d) marcação manual de unread
para "voltar aqui depois".

### Solution

1. **Agents feed global**: feed persistente `AgentFeedItem` unificando
   completions, worker_done, escalations, questions/decisions, dispatch
   lifecycle, worktree events, PR/MR state changes — cada item com deep-link.
2. **Unread model**: `readAtUtc` por usuário+item; badge no menu "Agents" =
   count unread de itens que precisam de ação (`needsAttention`); badge no tab
   Dashboard = count total unread.
3. **Mark unread / Mark all read**: ação por item e "Mark all read" no header;
   unread manual é flag explícita (não só timestamp).
4. **Notification preferences**: por categoria (`completion|attention|
   question|failure|worktree|pr`) toggles `badge`, `sound`, `persist`;
   defaults: attention/question = badge+sound; completion/failure = badge;
   worktree/pr = persist only. Som único, respeita mute.
5. **Deep-link**: item do feed abre a superfície certa (run, task, worktree,
   PR) — mesmo resolver do board (`AgentBoardCard.deepLink`).

### Scope

In scope: feed server-side persistente, unread store, badges, preferences,
sound, deep-link. Out of scope: push notifications OS/browser (P4), e-mail,
feed multi-usuário com ACL por escopo (v1 = por usuário logado vê o que pode).

## 2. Requisitos

### Funcionais

- RF-001 `AgentFeedItem` entity: `Id`, `Kind`, `Title`, `Body?` (≤280c),
  `DeepLink`, `SessionKey?`, `Cli?`, `IssueNumber?`, `AtUtc`,
  `NeedsAttention` (bool), `UserId` (escopo do item).
- RF-002 Producers: transições do attention model (waiting_you, blocked,
  done<15m), `worker_done`, `escalation`, `decision`/`question` criada e
  resolvida, `dispatch` settled/abandoned, worktree status/comment checkpoint,
  PR state change do repo selecionado.
- RF-003 `GET /api/agents/feed?before=<cursor>&limit=50` paginado
  descending; `GET /api/agents/feed/unread-count` retorna `{total, attention}`.
- RF-004 `POST /api/agents/feed/{id}/read`, `.../unread`, `.../read-all`;
  todos idempotentes; unread manual seta flag `ManuallyUnread` que read-all
  respeita (read-all limpa também `ManuallyUnread`).
- RF-005 Badge menu "Agents" = `unread attention count`; badge tab Dashboard =
  `unread total`; atualiza via SignalR `agent.feed` (patch count + item novo).
- RF-006 `NotificationPreferences` por usuário: JSON por categoria
  `{badge, sound, persist}`; `persist=false` → item não grava (ephemeral:
  só badge/toast); UI em `/settings` seção "Agents notifications".
- RF-007 Sound: `attention`/`question` tocam som curto uma vez (debounce 5s
  por sessão de tab); respeita preferência `sound=false` e mute global.
- RF-008 Feed dedup: `(Kind, SessionKey, DeepLink)` janela 5min → merge
  (incrementa `RepeatCount`, atualiza AtUtc) — evita flood de heartbeat-adjacent
  events.
- RF-009 Retenção: itens persistidos ≥30 dias; job de prune diário
  (`AgentFeedPruneJob` no background job existente).
- RF-010 SignalR `agent.feed`: grupo por usuário; payload `{kind, item?|patch,
  unreadCounts}`; reconnect re-fetch desde cursor salvo.

### Não-funcionais

- Feed write ≤2ms amortizado (insert único + unread increment); producers
  publicam via canal, não bloqueiam a transição de estado.
- Query do feed indexada em `(UserId, AtUtc desc)` — SQLite composite index.
- Preferências cached por usuário (IMemoryCache, invalidate on write).
- Nenhum feed item contém transcript de CLI nem conteúdo de mensagem >280c
  (preview truncado no producer, nunca no client).

## 3. Arquitetura

```mermaid
flowchart LR
  Attn[Attention model] --> P[FeedProducer\nchannel]
  Mail[Mailbox decide/worker_done] --> P
  WT[Worktree/PR events] --> P
  P --> DB[(AgentFeedItem\n+ UserFeedRead)]
  DB --> API[/api/agents/feed]
  DB -->|agent.feed patch| Hub[SignalR]
  Hub --> UI[FeedPanel.razor\n+ menu badge]
  UI --> DL[deepLink resolver\ncompartilhado com board]
```

- `AgentFeedService` (Application): produce/consume/read/unread.
- `UserFeedRead` (userId, itemId, readAtUtc, manuallyUnread) — unread =
  ausência de row OU `manuallyUnread=true`.
- `FeedPanel` pode viver dentro da aba Dashboard substituindo o merge atual +
  header de menu atualizado no `MainLayout`.

## 4. Fases

- **P1** — entity+service+endpoints+FeedPanel substituindo merge do dashboard +
  badge menu (counts via poll 30s).
- **P2** — SignalR push + mark unread/manual + read-all + dedup.
- **P3** — preferences UI + sound + prune job.

## 5. Testes

- Unit: dedup merge em janela; unread = ausência de row; read-all limpa
  manuallyUnread; needsAttention só para waiting_you/question/escalation.
- Unit: preferences `persist=false` não grava item (mas badge ephemeral conta).
- Integration: producer→feed→unread-count→read round-trip; pagination cursor
  estável; SignalR patch após worker_done.
- Integration: prune remove >30d preservando unread.

## 6. Open questions

1. Feed compartilha o `UnreadStore` do board (card unread) ou stores
   separados? Proposta: mesma tabela `UserFeedRead` — card unread deriva do
   último feed item daquela SessionKey (consistência).
2. Ephemeral items (persist=false) aparecem onde? Proposta: toast + badge
   count only, nunca no feed.
3. Som default: bundle WAV curto ou `Notification` API com `silent=false`?
   Proposta: WAV bundled (sem permissão OS em v1).
