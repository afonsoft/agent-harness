# SPEC-20260919 — Stale SPEC Status (2ª recorrência): sincronizar statuses + convenção anti-regressão

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `stale-spec-status-2` |
| Type | `Docs` / `Process` |
| Stack | `Markdown` |
| Repository | `afonsoft/agent-harness` |
| Branch | `docs/devin-20260919-stale-spec-status` |
| Ticket | `GAP-documentation-stale-spec-status` (gap-analysis-20260919) — Issue #143, Epic #140 |
| Status | `Done` — entregue neste PR |

Origin: gap-analysis-20260919 — **segunda recorrência** da classe
`GAP-documentation-stale-specs` já tratada por `SPEC-20260914-stale-spec-status`
(que corrigiu o lote anterior mas não instalou prevenção). Novos SPECs
entregues seguem com `Status: Approved`, e `cli-migration.md` /
`followups.md` também divergem do estado real.

## 1. User Story

**As a** maintainer do agent-harness,
**I want** que statuses de SPEC e arquivos de pendências reflitam o estado
real de entrega — com uma convenção que impeça nova regressão,
**so that** gap-analysis e orchestrator auditem contra contratos vivos, não
zumbis.

## 2. Scope

### In scope

- `Status: Approved` → `Done` (com nota de PR) nos SPECs entregues:
  - `SPEC-20260911-refine-github-actions` — outcome verificado: `dotnet.yml`
    usa `checkout@v7`, `setup-dotnet@v6`, `cache@v6`, `upload-artifact@v7`,
    `codecov@v7`; `permissions: contents: read` global + no job
    `vulnerabilities` (linhas 32-33, 129-130); bumps via PRs #52–#56.
  - `SPEC-20260918-sidebar-icon-rail` — PR #125 merged, validado em produção
    (gap-analysis-20260918 §3).
  - `SPEC-20260918-action-button-separation` — PR #135 merged, slice #134
    CLOSED.
  - `SPEC-20260918-cli-agents-expansion` — PR #94 merged; 8 `AgentCliKind`
    novos em `AgentCliMap.cs:102+`, `POST/GET agent-clis/{kind}/install*`
    (`Program.cs:1351,1368`), `AgentCliInstallService.cs` +
    `AgentCliInstallServiceTests.cs` existem.
- `.specs/cli-migration.md`: `Status: em revisão` → concluído — a duplicata
  `context:current` já não existe (ocorrência única em `Program.cs:16`) e a
  migração Spectre está mergeada há semanas.
- `.specs/followups.md`: marcar `[x]` nos itens concluídos (duplicata
  `context:current`, smoke tests via suíte atual se aplicável); itens ainda
  abertos que viraram SPEC (testes de CLI) ganham cross-reference para
  `SPEC-20260919-cli-test-coverage`.
- Convenção anti-regressão: adicionar em `CLAUDE.md` (ou
  `.claude/rules/global-rules.md`) a regra "PR que entrega SPEC marca
  `Status: Done` no mesmo PR" — ou um check leve (`scripts/` ou step de CI)
  que lista SPECs `Approved` cujo branch `feature/*` já foi mergeado.
  Decisão final na implementação; mínimo = documentar a convenção.

### Out of scope

- Reescrever conteúdo técnico dos SPECs.
- Automatizar fechamento de Issues/Epics no GitHub.
- Mudanças em `.github/workflows/**` (se o check escolhido for CI, abrir SPEC
  própria — workflows são hard-rule protegidos).

## 3. Technical Context

- Formato de status varia por SPEC: `| Status | \`Done\` |` (tabela) e
  `- **Status**: Done` (lista) — qualquer check precisa cobrir ambos.
- Epic #126 (`gap-analysis-20260918`) permanece aberta embora o item
  INCONCLUSIVO tenha sido resolvido via action-button-separation — fechamento
  é ação administrativa do gate, não deste SPEC.

## 4. Functional Requirements

| ID | Requirement |
|---|---|
| RF-001 | Os 4 SPECs listados passam a `Status: Done` com referência ao PR de entrega. |
| RF-002 | `cli-migration.md` marcado concluído; pendências bloqueantes resolvidas riscadas/atualizadas. |
| RF-003 | `followups.md` reflete o estado real (`[x]` nos concluídos; cross-ref para SPECs novas nos que viraram trabalho formal). |
| RF-004 | Convenção "SPEC entregue → `Status: Done` no PR" registrada em `CLAUDE.md` ou `global-rules.md`; se check automático for escolhido e couber fora de `.github/workflows/`, incluí-lo. |

## 5. Acceptance Criteria

- **AC-1** `grep -l "Status.*Approved" .specs/SPEC-*.md` não retorna SPECs cujos PRs já foram mergeados.
- **AC-2** `cli-migration.md` e `followups.md` sem pendências fantasma.
- **AC-3** A convenção está escrita em arquivo de regras lido por agentes (CLAUDE.md ou global-rules.md).
- **AC-4** Diff só toca Markdown/scripts (sem código de produto).

## 6. DoD

- [x] RF-001..004 implementados.
- [x] SPEC → `Status: Done`; PR merged.
