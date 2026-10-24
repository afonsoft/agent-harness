# SPEC-20261024-docs-p0-p1-wave-sync — Sync docs with the P0 hardening + P1 reliability wave

| Campo | Valor |
| --- | --- |
| Status | **Approved** |
| Owner | @afonsoft |
| Ticket | GAP-documentation-october-late-docs-sync |
| Área | `docs/features.md`, `docs/api.md`, `docs/architecture/architecture.md` |

## Contexto

A onda P0/P1 de hardening e confiabilidade (denylist de credenciais, prompt-defense,
preflight doctor, deferred tools, parallel tool calls, hook engine, memory
consolidation) mergeou entre PRs #636–#643, mas `docs/features.md` não menciona
nenhum desses recursos — a seção mais recente é "October wave" do chat, sincronizada
em `afbccec` para o gap anterior (SPEC-20261008-docs-chat-wave-sync).

## Evidência (gap-analysis 2026-10-24)

- AS-IS: `grep -oE "delegate_[a-z]+|hook|preflight|denylist|schema_valid" docs/features.md` → 0 hits (apenas `mailbox`/`worktree` presentes); `docs/api.md` → 6 hits soltos, sem seção própria.
- TO-BE: convenção de sync pós-entrega estabelecida por `afbccec` ("docs: sync api.md/features.md with October chat wave") e pelo SPEC-20261008-docs-chat-wave-sync.
- Diferença: usuário final não descobre `taskctl doctor`, `delegate_task --dry_run`, hooks, memory tool, preflight endpoint.

## Requisito

Atualizar `docs/features.md` com seção(ões) para: security hardening (denylist,
prompt-defense), reliability (preflight, hooks, memory), delegation v2 (DAG/mailbox/
gates) e `docs/api.md` com os endpoints novos (preflight, agents/sessions, delegation).
Refresh pontual de `docs/architecture/architecture.md` se algum componente novo
(ChatHookRegistry, MemoryConsolidationService) ainda não constar.

## Critérios de Aceite

- [ ] `grep -ciE "preflight|denylist|hook|deferred" docs/features.md docs/api.md` > 0.
- [ ] Cada recurso novo referencia a SPEC que o introduziu.
- [ ] Nenhuma seção descreve comportamento divergente do código em `main`.

## Fora de escopo

Reescrita estrutural de docs; tradução pt-br completa (segue a convenção bilíngue
existente apenas onde já adotada).
