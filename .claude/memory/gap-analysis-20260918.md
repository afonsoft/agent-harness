# Gap Analysis — 2026-09-18

Run: revalidação pós-sprint dos 7 SPECs entregues (rag-mcp-sync, skills-cache-permissions,
gantt-github-timeline, projects-removal, workflow-github-actions, ai-chat-threads,
sidebar-icon-rail). Branch `main` @ `b5b62a2`, working tree limpo, `gh` autenticado
(afonsoft). Read-only até o gate — sem Issues/branches/commits.

## 1. Source Inventory

| Fonte | Estado |
|---|---|
| `.specs/SPEC-*.md` (7 alvos) | present |
| `docs/` (api, features, installation + pt-br) | present |
| `docs/architecture/` | present |
| `.claude/CONTEXT.md`, `.claude/memory/` | present |
| `CLAUDE.md`, `AGENTS.md`, `README.md` | present |
| `.claude/rules/`, `.claude/agents/` | present |
| Source `src/` + testes `tests/` | present |
| CI `.github/workflows/` | present |
| Git history + PRs #115–#125 | present |
| `ORCHESTRATOR-ROADMAP.md` | absent |

## 2. Candidates & Verdicts

| Key | Categoria | Veredito | Prioridade | Evidência |
|---|---|---|---|---|
| GAP-implementation-touch-targets-touch-viewports | implementation | **CONFIRMADO** | low | TO-BE: sidebar SPEC §scope "interactive targets ≥44px on touch viewports" + AC-5. AS-IS: `btn-sm` (~31px) em `Settings.razor:137,239,371-381` e rows de agents; nenhuma media rule eleva action buttons a 44px (site.css só cobre `task-card-menu`/`filter-chip`, linhas 1033/1101). |
| GAP-implementation-aichat-failed-thread-state | implementation | **CONFIRMADO** | low | TO-BE: ai-chat AC5 "erro mostra estado de erro na thread". AS-IS: `OnSseEvent` em `AiChat.razor:286` só atualiza `_running`; `_activeThread.Status` fica stale → badge "Failed" (header + sidebar) nunca aparece na sessão, embora `AiChatService.cs:199` marque `SetStatus(Failed)` e publique `ai_chat.run`. |
| GAP-spec-contradiction-action-button-gap | requirements | **INCONCLUSIVO** | — | Contradição interna do sidebar SPEC: AC-5 exige "≥8px separation" mas o RF autoriza `d-inline-flex gap-1` (4px) para "dense/inline cells" — o implementado. Precisa decisão: alinhar AC ao RF (4px ok em células densas) ou impor 8px. |
| GAP-terminology-skills-step-recovered | requirements | REJEITADO | — | RF-004 diz `Recovered`/`Failed`; enum `SkillsInstallStepState` (Contracts/Skills/SkillsInstallStep.cs:4-11) reporta `Succeeded` + mensagem "recovered from inaccessible cache". Semântica preservada; validado em produção. |
| GAP-terminology-aichat-system-event | requirements | REJEITADO | — | RF-003 diz "evento `system`"; `AiChatEventRole` só admite `user/assistant/activity/error` — post-backs usam `activity`/`error`. Equivalente funcional. |
| GAP-route-prefix-api-local | documentation | REJEITADO | — | ACs citam `/api/local/projects`; rotas reais são `/api/projects` — 404 verificado em produção (autenticado) / 401 (anônimo). |
| GAP-viewport-threshold-1280-vs-992 | requirements | REJEITADO | — | AC-1 "≥1280px" vs RF-001 "≥992px" (mesmo SPEC); impl ativa no `lg` 992px — superconjunto que satisfaz ≥1280px. |

## 3. Verificação positiva (amostras de cobertura)

- **rag-mcp-sync**: `mcp/sync` 400 `rag-not-configured` sem URL (Program.cs:1407+), `mcp/remove` explícito (1431), `Sanitize(apiKey)` em todos os paths de log (McpProvisioningService.cs:177-354), UI disable+tooltip+unsaved-hint (Settings.razor:185-223).
- **skills-cache-permissions**: `EnsureAccessible` probe + quarantine rename + `chmod +x` + step `cache-prepare` — validado com recuperação real em produção.
- **gantt**: `MaxIssues=200`, `MaxConcurrentTimelineFetches=8`+`SemaphoreSlim`, `GetMilestonesAsync`, 14 testes verdes.
- **projects-removal**: 9 tabelas dropadas, `ProjectsRedirect.razor`, taskctl sem comandos locais, residue só em migrations históricas.
- **workflow**: ordem last-run desc (`GitHubService.cs:331` MinValue→último), timer 60s só com runs live (`Workflow.razor:286-304`), badge mapper 21 testes.
- **ai-chat**: SSE dual-mode, delete 204/404, prompt builder cap 8k/20 msgs, composer desabilitado em `_running`, 12 testes verdes.
- **sidebar**: rail 4.5rem, pre-paint script, tooltips hover+focus-visible, reduced-motion, topbar title — CSS/JS confirmados.

## 4. SPECs Draft gerados

- `.specs/SPEC-20260918-touch-targets.md` → GAP-implementation-touch-targets-touch-viewports
- `.specs/SPEC-20260918-aichat-thread-failed-state.md` → GAP-implementation-aichat-failed-thread-state

## 5. Issues criadas (aprovado pelo usuário)

- Epic: https://github.com/afonsoft/taskboard-ai/issues/126 (`epic`, `in_progress`)
- S1 touch-targets: https://github.com/afonsoft/taskboard-ai/issues/127 (`slice`) → `.specs/SPEC-20260918-touch-targets.md`
- S2 aichat-failed-state: https://github.com/afonsoft/taskboard-ai/issues/128 (`slice`) → `.specs/SPEC-20260918-aichat-thread-failed-state.md`

## 6. Resultado da execução

- PR #129 merged (`789ae9d`) — touch-targets; slice #127 CLOSED.
- PR #130 merged (`8ee81bc`) — aichat-thread-failed-state; slice #128 CLOSED.
- PR #131 merged (`9f25eb5`) — SPECs → `Status: Done`.
- Deploy: `taskboard-server` restarted 23:37 -03; regra CSS servida em `http://127.0.0.1:47823/css/site.css` (verificado via curl).
- Epic #126 permanece `in_progress` aguardando a decisão do item INCONCLUSIVO.

## 7. Pendências

- INCONCLUSIVO `GAP-spec-contradiction-action-button-gap` aguarda decisão do usuário (alinhar AC-5 ao RF ou impor ≥8px) — não virou SPEC/Issue. Ao decidir, fechar ou atualizar o Epic #126.
