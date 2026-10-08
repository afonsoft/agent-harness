# SPEC-20261008-docs-chat-wave-sync — Sync `docs/` with the October chat wave

| Campo | Valor |
| --- | --- |
| Status | **Approved** |
| Owner | @afonsoft |
| Ticket | GAP-documentation-chat-wave-docs-sync |
| Área | docs/api.md, docs/features.md (+ mirrors `*.pt-br.md`) |

## Contexto

O repositório mantém `docs/api.md` como referência de endpoints e `docs/features.md` como catálogo de features (ambos com espelho `*.pt-br.md`). Ambos descrevem o subsistema de chat no estado de ~29/set — o ai-chat legado (ACP) e o provider chat básico. A onda de outubro (SPECs 20261005–20261017) entregou chat runs, approvals com classificação de risco, attachments, workspace panel com tabs, preview proxy, git bar, delegation DAG/mailbox e push subscriptions — nada disso consta nos docs.

## Evidência (gap-analysis 2026-10-08)

- AS-IS: `grep -in "chat-run-hub\|/preview/\|/api/local/push\|delegation" docs/api.md` → 0 hits; `docs/features.md` só cobre SPECs até 20261003 (`mcp-tool-surface`).
- TO-BE: `.specs/SPEC-20261005-chat-tool-approval.md`, `SPEC-20261011-chat-workspace-panel.md`, `SPEC-20261013-chat-risk-approvals.md`, `SPEC-20261014-chat-git-bar-overview.md`, `SPEC-20261015-chat-preview-panel.md`, `SPEC-20261016-chat-browser-tool.md`, `SPEC-20261017-chat-polish.md`, `SPEC-20261005-delegation-dag-mailbox.md`; `docs/architecture/architecture.md` (atualizado hoje) já lista esses endpoints/componentes.
- Diferença observada: documentação de usuário/API ~2 semanas atrás da implementação.

## Requisito

Atualizar `docs/api.md` (e `api.pt-br.md`) com a superfície nova: `POST /api/local/chat/conversations/{id}/attachments`, chat runs + cancel, approvals (`approve`/`always-allow`/`deny`), `/chat-run-hub` (SignalR), `/preview/{port}/{**path}`, `/api/local/push/*`, `/api/local/delegation/*` — com a mesma densidade de formato das entradas existentes (statuses, payloads, limites como o cap de 8 MB).

Atualizar `docs/features.md` (e `features.pt-br.md`) com as features do chat workspace: tabs (Plan/Tasks/Editor/Browser/Preview/Terminal/Changes), risk-based approvals, ChatGitBar, browser tool/screenshots, delegation inbox/DAG, jobs/schedules já cobertos.

## Critérios de Aceite

- [ ] Todo endpoint novo de chat presente em `api.md` retorna `grep` com hit para `chat-run-hub`, `/preview/`, `push`, `delegation`, `attachments`.
- [ ] `features.md` menciona workspace panel, approvals por risco e delegation.
- [ ] Espelhos pt-br atualizados em paridade.
- [ ] Sem mudança de código.

## Fora de escopo

README raiz, ADRs (GAP-architecture-adr-records), novos endpoints.
