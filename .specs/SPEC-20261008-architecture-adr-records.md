# SPEC-20261008-architecture-adr-records — Bootstrap ADR records under `docs/architecture/`

| Campo | Valor |
| --- | --- |
| Status | **Approved** |
| Owner | @afonsoft |
| Ticket | GAP-architecture-adr-records |
| Área | docs/architecture/adr/ |

## Contexto

`docs/architecture/` tem o documento principal + diagrama archify, mas **nenhum ADR** (Architecture Decision Record). A convenção do projeto (`architecture` skill + `doctor`) prevê `AD-NNNN` para decisões significativas, e `.specs/followups.md:16` já cobra uma pendência concreta: registrar *por que* Spectre.Console.Cli foi escolhido sobre `System.CommandLine` na migração CLI. Outras decisões estruturais igualmente sem registro: local-first SQLite, deploy host-mode via systemd user service, worktree-per-run isolation, chat tool risk classifier.

## Evidência (gap-analysis 2026-10-08)

- AS-IS: `ls docs/architecture/` → `architecture.{md,json,html}` apenas; nenhum `AD-*`/`adr/` no repo.
- TO-BE: `.specs/followups.md:16` (`- [ ] Decisão/ADR registrando por que Spectre foi escolhido`); convenção architecture `AD-0001` como próximo artefato.
- Diferença: decisões irreversíveis/caras existem só na memória oral e em git history — sem ADR, novas iterações revisitam decisões já fechadas (ex.: sandbox-runtime já teve decisão dono-documentada só em `gap-analysis-20261007.md`).

## Requisito

Criar `docs/architecture/adr/` com `AD-0001-spectre-console-cli.md` (satisfaz `followups.md:16` → marcar `[x]`) e ADRs mínimos para as decisões já tomadas que ainda orientam o código: `AD-0002-local-first-sqlite`, `AD-0003-worktree-per-run`, `AD-0004-host-mode-deployment` (decisão do dono de 07/out: execução no host, sandbox posterior). Formato: Status/Context/Decision/Consequences, ~40-60 linhas cada — registro de decisão *já tomada*, não proposta nova.

## Critérios de Aceite

- [ ] `docs/architecture/adr/AD-000{1..4}-*.md` presentes.
- [ ] `followups.md:16` marcado `[x]` (Spectre ADR entregue).
- [ ] `architecture.md` referencia a seção `adr/` (1 linha).
- [ ] Sem mudança de código.

## Fora de escopo

Reescrever decisões (todos são registros retroativos), processo de aprovação de novos ADRs.
