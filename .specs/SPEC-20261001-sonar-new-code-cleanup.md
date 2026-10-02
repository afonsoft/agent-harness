# SPEC-20261001-sonar-new-code-cleanup: remediation of new-code findings on main

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | SonarCloud new-code regression cleanup |
| Product / System | agent-harness |
| Module / Bounded Context | Application + Integrations + Blazor + repo hygiene |
| Change type | Bugfix batch |
| Repository | afonsoft/agent-harness |
| Suggested branch | `fix/devin-20261001-sonar-new-code` |
| Technical owner | afonsoft |
| Status | Approved |
| Date | 2026-10-01 |
| Target agent | Devin |
| Ticket / Gap | GAP-implementation-sonar-new-code-debt (gap-analysis 2026-10-01) |
| Related SPECs | SPEC-20261001-pr-review-backlog-fixes, SPEC-20260930-sonar-s8949-pass-cancellation-token, SPEC-20260930-sonar-generated-docs-exclusion |

---

## 1. Executive Summary

### Problem

Gap-analysis 2026-10-01 verificou o SonarCloud na `main` (análise 15:41 UTC,
commit `e4f91db`): **Quality Gate = ERROR** (`new_reliability_rating` = C).

Fontes dos findings (todas verificadas via API `api/issues/search`):

1. **Bugs introduzidos pelo próprio PR #418** — o refactor de streaming (B-02)
   deixou `await Task.Delay(150)` sem `CancellationToken` em
   `ChatService.cs:493` (csharpsquid:S8949). `StreamingProcessRunner.cs:65`
   tem `WaitForExitAsync()` sem token (intencional — flush de output — mas
   precisa de `CancellationToken.None` explícito).
2. **~20 code smells em linhas novas do #418** — S3776, S107, S3358, S5034,
   S6667, S8970, S2325, S3267, S3878, S1075, S4035, S2486 — criados pelo diff
   de remediação (fixes legítimos que adicionaram complexidade/params).
3. **`.sonar_devin_auto_fix/` commitado** — 4 artefatos gerados
   (`SONAR_FIX_METRICS.html`, `SONAR_FIX_REVIEW_NOTES.md`,
   `SONAR_FIX_TODO_BOARD.md`, `sonarqube_issues.json`) tracked no git e
   indexados pelo Sonar → bug Web:S5254 + ruído. Commitado no PR #411
   (`91d7df4`).
4. `docs/**` já saiu do índice (exclusão OK) — issues de `docs/` fecham
   como Removed; nenhuma ação adicional além de aguardar o fechamento.

### Objective

Quality Gate da `main` de volta a OK (`new_reliability_rating` = A),
zero bugs em new code, smells em new code saneados, artefatos gerados
fora do controle de versão.

### Out of scope

- Os ~1.300 code smells do backlog global (issue #410) — somente os findings
  em new code abertos pelo diff do #418.
- Findings de `docs/` — já cobertos pela exclusão.

---

## 2. Agent Role

> Senior .NET engineer — concurrency, Sonar rule remediation, ABP layering.

## 3. Agent Autonomy Level

3

### Restrictions

- `.github/workflows/**` protegido — a exclusão já está correta; NÃO editar.
- Cada fix com teste de regressão ou justificativa de impossibilidade.
- `.sonar_devin_auto_fix/` removido via `git rm` + `.gitignore` — sem perda
  de dados (conteúdo é regenerável; se valor histórico, mover para
  `.claude/memory/` antes de remover — decidir na implementação).

---

## 4. Findings (todos CONFIRMADOS via SonarCloud API, 2026-10-01)

| ID | Regra | Local | Finding | Tipo |
|---|---|---|---|---|
| N-01 | csharpsquid:S8949 | `src/Taskboard.Application/Chat/ChatService.cs:493` | `Task.Delay(150)` sem `ct` no loop de drain do streaming (B-02) | BUG |
| N-02 | csharpsquid:S8949 | `src/Taskboard.Integrations/Agents/StreamingProcessRunner.cs:65` | `WaitForExitAsync()` sem token — intencional (flush pós-exit) | BUG |
| N-03 | Web:S5254 | `.sonar_devin_auto_fix/SONAR_FIX_METRICS.html` | Artefato gerado commitado — `<html>` sem lang | BUG |
| N-04 | csharpsquid:S3776 | `ChatService.cs`, `OpenAiCompatibleClient.cs`, `AiChat.razor`, `AcpV2Dialect.cs` | Complexidade cognitiva em código novo do #418 | smell |
| N-05 | csharpsquid:S107 | `ChatService.cs` | Método com >7 parâmetros (novo) | smell |
| N-06 | csharpsquid:S3358 | `RuntimeConfigurationService.cs`, `ProviderChat.razor` | Ternário aninhado novo | smell |
| N-07 | csharpsquid:S5034+S6667 | `ManagedJobService.cs` | ValueTask múltiplo await / exceção capturada sem log efetivo | smell |
| N-08 | csharpsquid:S4035 | `src/Taskboard.Domain/Entity.cs` | `==`/`!=` restaurados (B-10) exigem refinamento IEquatable | smell |
| N-09 | csharpsquid:S8970, S2325, S3267, S3878, S1075×5 | `AiChatService.cs`, `AiChat.razor`, `MainLayout.razor`, `AgentCliMap.cs`, `AcpSessionClient.cs` | Smells em linhas alteradas pelo #418 | smell |
| N-10 | — | `.sonar_devin_auto_fix/` (4 arquivos) | Diretório de saída do auto-fix tracked no git | hygiene |

---

## 5. Functional Requirements

### RF-001 — Bugs S8949 (N-01, N-02)

- `ChatService.cs:493`: passar o token do enumerador ao `Task.Delay(150, ct)`.
  Decidir o comportamento em cancelamento: o drain de activity pode
  deliberadamente ignorar `requestAborted` (usuário parou — continuar
  drenando) mas deve honrar `cts.Token` (stop cooperativo). Escolha e
  comentário.
- `StreamingProcessRunner.cs:65`: `WaitForExitAsync(CancellationToken.None)`
  explícito — documenta flush intencional e satisfaz a regra.

### RF-002 — Artefatos fora do repo (N-03, N-10)

- `git rm -r .sonar_devin_auto_fix/` + entrada em `.gitignore`.
- Se o conteúdo tiver valor histórico, arquivar em `.claude/memory/` antes.
- Fecha o bug Web:S5254 e ~30 smells desse diretório.

### RF-003 — Smells em new code (N-04 a N-09)

Remediar cada ocorrência seguindo o padrão já usado nas SPECs
`sonar-*` (extração de método, `StringBuilder`, early-return, etc.).
Sem mudança de comportamento; smells em código coberto exigem teste de
regressão quando o refactor tocar lógica.

### RF-004 — Gate

Após merge, a próxima análise da `main` deve reportar
`new_reliability_rating` = A e quality gate OK. Validar via
`api/qualitygates/project_status`.

---

## 6. Acceptance Criteria

- AC-01: `ChatService.cs` e `StreamingProcessRunner.cs` sem S8949.
- AC-02: `.sonar_devin_auto_fix/` fora do índice git e do Sonar.
- AC-03: Zero issues de tipo BUG em new code na main.
- AC-04: Smells N-04..N-09 remediados ou marcados won't-fix com
  justificativa registrada na SPEC.
- AC-05: Build 0 warnings; unit + integration verdes; format limpo.
- AC-06: Quality gate da main OK na análise seguinte ao merge.

## 7. Risks & Mitigations

- Refactors em `ChatService`/`PipelineEngine` recentemente alterados —
  mitigado pela suite 1378/307 verde como baseline.
- `git rm` de artefatos — revisitar se algum doc referencia os arquivos.

## 8. Task Plan

- [ ] T1 — N-01 + N-02 (S8949 bugs)
- [ ] T2 — N-03 + N-10 (remover `.sonar_devin_auto_fix` + gitignore)
- [ ] T3 — N-04..N-09 (smells em new code)
- [ ] T4 — validação final + confirmação do gate pós-merge

## 8.1 Won't-fix registrados (execução 2026-10-02)

- **S1075 `AgentCliMap.cs:63-67` + `SearchBackends.cs:44` + `Taskboard.Mcp/Program.cs:13` +
  `Server/Program.cs:3257`** — endpoints de instalação/loopback fixos são
  constantes intrínsecas do catálogo; torná-los configuráveis adiciona
  superfície sem benefício (já são `internal const` nomeados).
- **S2589 `DynamicCommandClassifier.cs:436` ×2** — falso positivo: `inSingle`/
  `inDouble` são mutados dentro da local function `ConsumeUnquoted`
  (analisador não rastreia captura de variáveis em local functions); a
  condição é viva em runtime.
- **S8970 em `AgentControlService.cs`, `Server/Program.cs:107`,
  `AiChatService.cs:171`, `McpProvisioningService.cs:486`** — falso positivo:
  nullable está habilitado nesses projetos e o `!` é necessário
  (comprovado por CS8602/CS8604 com TreatWarningsAsErrors ao removê-lo).
- Smells de `*.js`/`*.css` fora da lista N-04..N-09 (boot.js, taskboard.js,
  site.css) — fora do escopo do #418; ficam para o backlog geral (#410).

## 9. Definition of Done

- [ ] Gate ERROR resolvido; ACs verificados.
- [ ] Issue #410 comentada com o estado residual real.
