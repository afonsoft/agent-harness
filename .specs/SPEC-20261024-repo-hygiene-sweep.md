# SPEC-20261024-repo-hygiene-sweep — Stale trackers and merged branches

| Campo | Valor |
| --- | --- |
| Status | **Approved** |
| Owner | @afonsoft |
| Ticket | GAP-operation-repo-hygiene |
| Área | GitHub Issues, branches remotas |

## Contexto

Dois tipos de lixo operacional acumulados:

1. Issue #634 (épico P0) permanece **OPEN** com checklist 0/4 — porém T1–T4 estão
   mergeados (`SensitivePathDenylist.cs`, PRs #636–#639).
2. Branches remotas mergeadas não são deletadas sistematicamente (~94 remotas);
   exemplo provado: `origin/feat/sdd-worker-brief-p3` (tip `fc79448` é ancestor de
   `main` via PR #626) e `feat/worktree-setup-scripts-p3` (PR #615 merged).

## Evidência (gap-analysis 2026-10-24)

- AS-IS: `gh issue view 634` → OPEN, `- [ ]` ×4; `git merge-base --is-ancestor origin/feat/sdd-worker-brief-p3 origin/main` → true; `git branch -r | wc -l` → 94.
- TO-BE: tracker reflete o estado real; branches mergeadas morrem (convenção já usada com `--delete-branch` nos merges mais recentes).
- Diferença: ruído no tracker e na lista de branches; dificulta enxergar trabalho real.

## Requisito

1. Marcar T1–T4 como concluídos na issue #634 e fechá-la (cada item com link do PR).
2. Listar branches remotas cujo tip é ancestor de `origin/main` (ou PR merged) e
   deletar as confirmadas; reportar as ambíguas em vez de deletar.
3. Documentar no AGENTS.md/CLAUDE.md a convenção `--delete-branch` por padrão.

## Critérios de Aceite

- [ ] Issue #634 fechada com 4/4 itens marcados.
- [ ] `git branch -r` reduzido; nenhuma branch com conteúdo não-mergeado deletada.
- [ ] Convenção de delete-on-merge registrada em doc.

## Fora de escopo

`feat/worktree-setup-scripts-p3` tip `115376b` ("dedupe P3 boilerplate") — commit
órfão pós-merge de conteúdo incerto; decidido à parte com o dono (INCONCLUSIVO no
relatório gap-analysis-20261024).
