# Gap Analysis — 2026-10-02

Re-run idempotente após run 2026-10-01 (gate nunca aprovado). Main `a5520b9`,
working tree limpo, CI verde (dotnet.yml + SonarCloud workflow + CodeQL).

## Source inventory

| Source | Estado |
|---|---|
| `.specs/SPEC-*.md` (226 files) | present — 10 Draft, ~16 Implemented, ~25 Done, resto legado |
| `.claude/memory/gap-analysis-*.md` | present — último run 2026-10-01 (gate pendente) |
| GitHub issues abertas | #410, #412, #413, #414 (sem epic/slice de gap-analysis) |
| SonarCloud `afonsoft_agent-harness` | gate main **ERROR** — `new_reliability_rating=3` (5 BUGs OPEN) |
| CI main | dotnet.yml success (2026-10-01 20:50); SonarCloud/CodeQL success |
| `gh` auth | OK (afonsoft) |

## Verificação das pendências do run anterior

| Item | Evidência | Veredito |
|---|---|---|
| SPEC-20261001-sonar-new-code-cleanup (Draft) | Gate ERROR; S8949 OPEN em `src/Taskboard.Application/Chat/ChatService.cs:540` e `src/Taskboard.Integrations/Agents/StreamingProcessRunner.cs:65` | CONFIRMADO — persiste |
| `.sonar_devin_auto_fix/` tracked | `git ls-files` lista 4 artefatos; `Web:S5254` OPEN em `SONAR_FIX_METRICS.html:1` | CONFIRMADO — persiste |
| SPEC-20261001-spec-issue-reconciliation (Draft) | 10 SPECs `| Status | Draft`; chat×6 implementadas (verificado: `chat.reasoning` em `OpenAiCompatibleClient.cs`/`ChatService.cs`, canonical `.agents/skills` em `ChatCapabilityRegistry.cs`/`SkillTool.cs`, ordenação exata→builtin→skill em `SlashCommandPalette.razor:38-43`); SPEC-20261001-ai-chat-openwebui `Implemented (pending human review)` merged #420 | CONFIRMADO — persiste |
| Issues #412/#413/#414 | seguem OPEN com fixes merged (#415) | CONFIRMADO — housekeeping |
| Issue #410 | OPEN; contagem ~707 smells obsoleta | CONFIRMADO — precisa update |
| SPEC-20260930-mobile-responsive-ui (Draft) | `inputmode`, `modal-fullscreen`, overlay `?` ausentes de `src/Taskboard.Client` (grep só acha bootstrap lib) | CONFIRMADO — PARCIAL persiste |
| Commits novos 2026-10-01→02 | PR #419 (settings @key crash) e #420 (openwebui parity) merged verdes; commit a5520b9 atualizou 3 SPECs chat documentando código já mergeado | sem gap novo |

## Candidate gaps → verdicts

| Chave | Candidato | Veredito |
|---|---|---|
| GAP-impl-sonar-new-code | Gate ERROR (reliability 3); 2×S8949 OPEN em new code | CONFIRMADO — já mapeado a SPEC-20261001-sonar-new-code-cleanup (Draft) — DUPLICADO p/ recriação |
| GAP-hygiene-sonar-artifacts | `.sonar_devin_auto_fix/` 4 artefatos tracked; Web:S5254 OPEN | CONFIRMADO — fold na SPEC acima — DUPLICADO |
| GAP-docs-spec-drift | ~10 SPECs stale: chat×6, settings-tabs, openwebui (implemented c/ status Draft/Implemented) | CONFIRMADO — mapeado a SPEC-20261001-spec-issue-reconciliation (Draft) — DUPLICADO |
| GAP-ops-stale-issues | #410/#412/#413/#414 abertas c/ fix merged | CONFIRMADO — fold na SPEC de reconciliação — DUPLICADO |
| GAP-impl-mobile-responsive | ACs pendentes (inputmode, modal-fullscreen, overlay `?`) | CONFIRMADO — SPEC-20260930-mobile-responsive-ui (Draft) — DUPLICADO |
| GAP-hygiene-architecture-html | `docs/architecture/architecture.html` Web:PageWithoutTitleCheck :3 + Web:S5254 :170 OPEN (2026-08-24, legado, fora do new-code gate) | CONFIRMADO (severidade baixa) — sugerir fold na SPEC sonar-cleanup; não bloqueia gate |
| GAP-impl-chat-specs-today | Specs chat atualizadas hoje documentam código já mergeado (reasoning, canonical source, palette order) — verificado em código | REJEITADO — não é gap, é evidência do drift de status (coberto acima) |

## Prioridades

1. **P0** GAP-impl-sonar-new-code (+artifacts) — gate da main em ERROR.
2. **P1** GAP-impl-mobile-responsive — SPEC existente, execução pendente.
3. **P2** GAP-docs-spec-drift + GAP-ops-stale-issues — housekeeping; reconciliation SPEC também deve
   decidir se chat×6/settings-tabs/openwebui vão a `Done`.
4. **P3** GAP-hygiene-architecture-html — opcional, absorver na sonar-cleanup.

## SPECs pendentes de gate (nenhum novo criado — idempotência)

- `.specs/SPEC-20261001-sonar-new-code-cleanup.md` — Draft
- `.specs/SPEC-20261001-spec-issue-reconciliation.md` — Draft
- `.specs/SPEC-20260930-mobile-responsive-ui.md` — Draft

## Gate → Issues (2026-10-02)

Gate APROVADO pelo usuário. Status dos 3 SPECs flipado para `Approved`.
Labels canônicas garantidas. Issues criadas:

- Epic: **#421** `gap-analysis-20261002` (labels: epic, todo)
- Slice: **#422** sonar-new-code-cleanup (P0)
- Slice: **#423** spec-issue-reconciliation (P2)
- Slice: **#424** mobile-responsive-ui (P1)

## Pendências abertas

- Handoff orchestrator: executar os 3 SPECs Approved (aguardando confirmação).
- Fechar #412/#413/#414; atualizar contagem em #410 (escopo da #423).
- Flip de status: chat×6, settings-tabs, openwebui → Done (via #423).
