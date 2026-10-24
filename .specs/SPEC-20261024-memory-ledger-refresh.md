# SPEC-20261024-memory-ledger-refresh — Refresh .claude/MEMORY.md and followups.md

| Campo | Valor |
| --- | --- |
| Status | **Approved** |
| Owner | @afonsoft |
| Ticket | GAP-documentation-memory-ledger-stale |
| Área | `.claude/MEMORY.md`, `.specs/followups.md` |

## Contexto

`.claude/MEMORY.md` (ledger curado de decisões + débitos técnicos) não recebe commit
desde 2026-09-23 — mais de um mês de decisões arquiteturais (delegation v2, worktree
setup, quota, hooks, memory consolidation, P0 hardening) ausentes. A tabela
"Débitos Técnicos" ainda lista "Persistência real de anexos" e "Integração LLM real"
como abertos — ambos entregues (`ChatAttachmentStore.cs`, providers reais).
`.specs/followups.md` mantém `[ ]` em itens de trabalho já mergeado (ex.: "Commit +
push" da migração CLI) e um "Revisar UX diff" sem dono há semanas.

## Evidência (gap-analysis 2026-10-24)

- AS-IS: `git log -1 -- .claude/MEMORY.md` → 2026-09-23; Débitos técnicos listam itens resolvidos; `followups.md` tem 2 `[ ]` vinculados a trabalho mergeado.
- TO-BE: o próprio arquivo declara "Itens concluídos viram `[x]` e são limpos na próxima sessão"; MEMORY.md é a fonte declarada de decisões cross-session.
- Diferença: memória curada desatualizada induz decisões erradas em sessões futuras.

## Requisito

1. Adicionar à tabela de decisões as entradas relevantes pós-2026-09-23 (só decisões
   de arquitetura duradouras, não cada feature).
2. Remover/marcar como resolvidos os débitos já entregues.
3. Limpar em `followups.md` os `[ ]` cujo trabalho já mergeou e consolidar os
   realmente pendentes com dono/contexto.

## Critérios de Aceite

- [ ] MEMORY.md não lista débito já implementado; contém as decisões de Outubro relevantes.
- [ ] `followups.md` só contém pendências reais, cada uma com contexto/dono.
- [ ] Nenhuma linha removida por engano — pendências legítimas preservadas.

## Fora de escopo

Reescrever histórico de decisões antigas; migrar formato do ledger.
