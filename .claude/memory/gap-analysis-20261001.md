# Gap Analysis — 2026-10-01

Audit pós-merge do backlog de review (PR #418, main `e4f91db`).
Objetivo: verificar se as correções foram implementadas e levantar pendências.

## Source inventory

| Source | Estado |
|---|---|
| `.specs/SPEC-*.md` (190+) | present — 6 chat + settings-tabs + mobile-responsive em Draft; ~40 sonar-* Approved; alguns Implemented |
| GitHub issues | 4 abertas: #410, #412, #413, #414 |
| SonarCloud API (afonsoft_agent-harness) | present — gate main ERROR |
| CodeQL / GitGuardian | present — PR #418 verde |
| `.claude/memory/gap-analysis-*.md` | present — último run 2026-09-29 |
| `gh` auth | OK |

## Verificação: correções implementadas?

| Verificação | Evidência | Veredito |
|---|---|---|
| B-01..B-23 + C-01..C-07 (PR #418) | Spot-checks: `ChatRunCoordinator` registrado (Program.cs:521), `UpdateChatConversationModelAsync` (TaskboardClient:950), `GetEffectiveBool` (RuntimeConfigurationService:247), `Entity ==` (Entity.cs:43) + suite 1378 unit / 307 integration verde no merge | COBERTO |
| SPEC-20261001-terminal-memory-mobile | merged #416, status Done | COBERTO |
| S7637 SHA pinning + docs exclusion | merged #415; docs/ fora do índice Sonar | COBERTO |
| 6 SPECs chat (delegation, registry, MCP, slash, ux, default-mode) | `RunAgentTool`, `IChatCapabilityRegistry`, `ChatMcpClientManager`, `SlashCommandPalette`, mini-terminal, fallback Chat→Agent — todos presentes (PR #411) | IMPLEMENTADO, status stale |
| settings-tabs | nav-tabs 5 abas + `?tab=` em Settings.razor | IMPLEMENTADO, status stale |
| mobile-responsive-ui | viewport meta ✓; inputmode=0, modal-fullscreen ausente, overlay `?` ausente | PARCIAL — gap real |
| Issues #412/#413/#414 | fixes merged #415; issues seguem OPEN | housekeeping pendente |
| Issue #410 | S7637 resolvido; backlog residual ~293 smells (docs excluído) | precisa update |

## Candidate gaps → verdicts

| Chave | Candidato | Veredito |
|---|---|---|
| GAP-impl-sonar-new-code | Gate main ERROR: 2 bugs S8949 (ChatService:493 Task.Delay sem ct — introduzido pelo refactor B-02; StreamingProcessRunner:65 flush sem token) + ~20 smells em linhas novas do #418 (S3776×3, S107, S3358×2, S5034, S6667, S8970, S2325×2, S3267, S3878, S1075×5, S4035) | CONFIRMADO → SPEC-20261001-sonar-new-code-cleanup |
| GAP-hygiene-sonar-artifacts | `.sonar_devin_auto_fix/` (4 artefatos gerados) tracked + indexado → bug Web:S5254 + ruído | CONFIRMADO → fold na SPEC acima (N-03/N-10) |
| GAP-docs-spec-drift | ~50 SPECs com status stale (chat×6, settings-tabs, keyed-render, sonar×40, Implemented legados) | CONFIRMADO → SPEC-20261001-spec-issue-reconciliation |
| GAP-ops-stale-issues | #412/#413/#414 abertas com fix merged; #410 com contagem obsoleta | CONFIRMADO → fold na SPEC de reconciliação |
| GAP-impl-mobile-responsive | ACs não cumpridos (inputmode, modal fullscreen <576px, overlay `?`, auditoria overflow-x) | DUPLICADO — SPEC-20260930-mobile-responsive-ui já existe (Draft); ação = aprovar/executar, sem SPEC novo |
| GAP-docs-issues-closure | Issues em docs/ e duplicação C-08 | REJEITADO — docs/ fora do índice; duplicação 0.1% no gate atual |
| B-01..B-23 reabertos | Fixes revertidos? | REJEITADO — verificados presentes na main |
| C-08/C-09 pendente re-scan | — | RESOLVIDO — re-scan feito; encontrou os findings acima (absorvidos por GAP-impl-sonar-new-code) |

## Prioridades

1. **GAP-impl-sonar-new-code** (P0) — gate da main em ERROR; inclui bug introduzido pelo refactor B-02.
2. **GAP-impl-mobile-responsive** (P1) — SPEC existente Draft; execução pendente.
3. **GAP-docs-spec-drift + GAP-ops-stale-issues** (P2) — housekeeping, baixo risco.

## SPECs gerados (Draft — gate pendente)

- `.specs/SPEC-20261001-sonar-new-code-cleanup.md`
- `.specs/SPEC-20261001-spec-issue-reconciliation.md`

## Pendências abertas

- Aprovação do gate: criar issues + executar SPECs.
- Issues #412/#413/#414: fechar após aprovação.
- `mobile-responsive-ui`: aprovação/execução da SPEC existente.
