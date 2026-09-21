# Orchestrator Sessions

## Session — 2026-09-20 23:25

**Scope**: Reconciliação pós-rename (`taskboard-ai` → `agent-harness`), fechamento de Epics E13–E16, verificação final de main.
**Decisions**: Issues #170/#171 fechadas com evidência de merge; SPECs todos em status terminal (nenhum Draft); SONAR_PROJECT_KEY deixado como `afonsoft_taskboard-ai` (workflow protegido + projeto Sonar existente).
**Delivered**: PR #246 (rename docs), #247 (Issues link no menu), #248 (fix testes do repo-selector), 2 re-deploys do `taskboard-server`, branches limpas (só main).
**Remaining**: Nenhum gap pendente. Decisão do usuário pendente: recriar projeto SonarCloud como `afonsoft_agent-harness` ou manter key antiga.
**Lessons**: Rename de repo exige re-verificar testes que dependem de ordem alfabética de fixtures (SelectedRepositoryServiceTests quebrou); `strings -e l` para achar UTF-16 em assemblies .NET; deploy exige `rm -rf publish/wwwroot/_framework` antes do publish.
