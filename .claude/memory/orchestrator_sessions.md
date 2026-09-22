# Orchestrator Sessions

## Session — 2026-09-20 23:25

**Scope**: Reconciliação pós-rename (`taskboard-ai` → `agent-harness`), fechamento de Epics E13–E16, verificação final de main.
**Decisions**: Issues #170/#171 fechadas com evidência de merge; SPECs todos em status terminal (nenhum Draft); SONAR_PROJECT_KEY deixado como `afonsoft_taskboard-ai` (workflow protegido + projeto Sonar existente).
**Delivered**: PR #246 (rename docs), #247 (Issues link no menu), #248 (fix testes do repo-selector), 2 re-deploys do `taskboard-server`, branches limpas (só main).
**Remaining**: Nenhum gap pendente. Decisão do usuário pendente: recriar projeto SonarCloud como `afonsoft_agent-harness` ou manter key antiga.
**Lessons**: Rename de repo exige re-verificar testes que dependem de ordem alfabética de fixtures (SelectedRepositoryServiceTests quebrou); `strings -e l` para achar UTF-16 em assemblies .NET; deploy exige `rm -rf publish/wwwroot/_framework` antes do publish.

## Session — 2026-09-22

**Scope**: Fix board "Execução do Agente" vazio + "Criar PR" bookkeeping (PR #295); SPEC + implementação Cockpit live-logs/terminal/explorer/diff-collapse (PR #296); re-deploy; memória.
**Decisions**: Espelhar eventos normalizados `run:`→`issue:` no producer (`PipelineEngine.EmitNormalized`) em vez de re-mapear queries; roteamento `issue:`→pipeline no `AgentControlService` com fallback legado; board updates do create-pr best-effort (nunca falham request cujo PR já existe); explorer com path-jail + 500/dir + 512 KB + binário; diff parseado em `UnifiedDiffParser` (Contracts) para reuso/testes sem bUnit.
**Delivered**: PR #295 (merged `ee3dfcc`), PR #296 (merged `1b92be3`), PR #297 (deflake, open), SPEC-20260921-cockpit-live-logs-explorer-diff → Implemented, re-deploy `taskboard-server` de main @`1b92be3` (health 200, `_framework` limpo).
**Remaining**: PR #297 aguardando CI/merge. Verificação manual de UX do Cockpit (follow-scroll, pill, explorer, diff collapse) pendente — requer browser.
**Lessons**: Poll-based waits em janelas <50 ms flakeiam sob carga no CI — usar TCS para segurar a janela; merge pode acontecer entre push e rerun — checar `state` do PR antes de assumir head; confirmar `_framework` limpo em todo deploy (blazor.boot.json stale quebra o WASM).

## Session — 2026-09-22 (worktree root)

**Scope**: Mover o root dos worktrees de `~/.taskboard/worktrees` para `~/repos` (pedido do usuário — worktrees visíveis no workspace junto aos clones).
**Decisions**: Default do produto mudou (não só env do deploy) — `Taskboard:WorktreeRoot` configurável com `~`-expansion seguindo o padrão `Taskboard:WorkspaceRoot`; stale-cleanup só apaga `.git`-file (worktree) ou dir vazio — clone/outros conteúdos → `InvalidValue`.
**Delivered**: PR #298 (merged `6960cfe`), re-deploy `taskboard-server` (health 200), docs/SPECs atualizados.
**Remaining**: Worktree antigo `~/.taskboard/worktrees/pipe_c6bc…` segue válido via path persistido — remover manualmente quando inspeção não for mais necessária.
**Lessons**: Em root compartilhado, nunca apagar dir existente sem provar que é worktree (gitfile) — risco de destruir clone real; `gh pr checks --watch` pode ficar stale — checar `state`/`mergeStateStatus` do PR diretamente.
