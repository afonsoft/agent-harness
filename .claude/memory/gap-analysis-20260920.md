# Gap Analysis — 2026-09-20 (pós-rename + onda ADE E6–E16)

- Repository: `/home/ubuntu/repos/agent-harness` | Branch: `main` | Commit: `fdf3fbd` (+#249 memory merge)
- Phase reached: `gate` (aguardando aprovação)
- Mode: `full`

---

## 1. Source Inventory

| Source | Status | Notes |
|---|---|---|
| `.specs/` | `present` | ~90 SPECs — todos em status terminal (Done/Implemented/Completed/Deprecated) |
| `docs/` | `present` | features.md + api.md (en/pt-br) sincronizados com a onda ADE |
| `docs/architecture/` | `present` | architecture.md/.html/.json **desatualizados** (pré-ADE) |
| `.claude/CONTEXT.md` | `present` | — |
| `.claude/MEMORY.md`, `.claude/memory/` | `present` | 5 relatórios gap-analysis anteriores + orchestrator_stats/sessions |
| `CLAUDE.md`/`AGENTS.md`/`README.md` | `present` | AGENTS.md é symlink → CLAUDE.md; README **desatualizado** (pré-ADE) |
| Tests/CI | `present` | 821 unit + 214 integration verdes; cobertura combinada **73.70%** |
| `gh auth` + remotes | `ok` | afonsoft/agent-harness; **0 issues abertas, 0 PRs abertos** |
| `.claude/knowledge/` | `absent` | prescrito no CLAUDE.md, diretório inexistente |

## 2. Dedup vs auditoria anterior (gap-analysis-20260919-ade-harness)

Todos os 10 gaps CONFIRMADO de 2026-09-19 foram entregues (E6–E16 + web-cli-agent): worktrees, context/memory, security gateway, verification loop, multi-agent DAG, cockpit, living specs, FinOps, CLI DB metrics → `DUPLICADO` (resolvidos). Nenhum recriado.

## 3. Candidatos e Veredictos

| Key | Categoria | Veredito | Prioridade | SPEC | Evidência |
|---|---|---|---|---|---|
| `GAP-implementation-cockpit-pause-resume` | implementation | **CONFIRMADO** | medium-high | `SPEC-20260920-cockpit-pause-resume` (Draft) | SPEC-20260919-ade-cockpit-hitl AC+T4 exigem Pause/Resume; `grep pause\|resume` em Cockpit/Harness → 0 matches; RunControlBar só steer/stop/create-pr |
| `GAP-documentation-architecture-drift` | documentation | **CONFIRMADO** | high | `SPEC-20260920-docs-sync-ade-platform` (Draft) | architecture.md: 0 matches p/ cockpit/pipeline/worktree — doc pré-ADE |
| `GAP-documentation-readme-feature-drift` | documentation | **CONFIRMADO** | medium | `SPEC-20260920-docs-sync-ade-platform` (Draft) | README overview sem cockpit/specs/repo-selector/FinOps/gateway |
| `GAP-documentation-knowledge-dir` | documentation | **CONFIRMADO** | low | `SPEC-20260920-docs-sync-ade-platform` (Draft) | CLAUDE.md prescreve `.claude/knowledge/`; `ls` → absent |
| `GAP-automation-coverage-ratchet-stale` | automation | **CONFIRMADO** | medium | `SPEC-20260920-coverage-ratchet-bump` (Draft) | threshold 65 vs medido 73.70% local; política ratchet sobe a cada sprint |
| `GAP-automation-sonar-project-key` | automation | **CONFIRMADO** | low | `SPEC-20260920-sonar-project-key` (Draft) | `SONAR_PROJECT_KEY: afonsoft_taskboard-ai` vs repo renomeado; decisão A/B/C pendente |
| AGENTS.md duplicado | documentation | **REJEITADO** | — | — | symlink → CLAUDE.md; fonte única preservada |
| `?repo=` endpoints sem doc | documentation | **REJEITADO** | — | — | api.md:329 documenta os 4 endpoints |
| install.sh REPO_DIR legado | operation | **REJEITADO** | — | — | `~/.taskboard/taskboard-ai` migrado no servidor; installs novos consistentes |
| Docker image name | operation | **REJEITADO** | — | — | Dockerfile sem refs; docs já `agent-harness:latest` |
| features.md/api.md stale | documentation | **REJEITADO** | — | — | verificado: cockpit, ?repo=, vscode restart, issues link cobertos |

## 4. Métricas

- Cobertura combinada (unit+integration cobertura.xml merged): **73.70%** (26966/36587)
- Build: 0 warnings/0 errors | Testes: 821+214 verdes
- Issues abertas: 0 | PRs abertos: 0 | SPECs não-terminais: 0

## 5. Draft SPECs gerados (aguardando gate)

- `.specs/SPEC-20260920-cockpit-pause-resume.md`
- `.specs/SPEC-20260920-docs-sync-ade-platform.md`
- `.specs/SPEC-20260920-coverage-ratchet-bump.md`
- `.specs/SPEC-20260920-sonar-project-key.md`

## 6. Resultado do Gate

- **Aprovado** pelo usuário em 2026-09-20 → Phase 6 executada.
- Issues criadas: [#250](https://github.com/afonsoft/agent-harness/issues/250) (cockpit pause/resume), [#251](https://github.com/afonsoft/agent-harness/issues/251) (docs sync), [#252](https://github.com/afonsoft/agent-harness/issues/252) (coverage ratchet), [#253](https://github.com/afonsoft/agent-harness/issues/253) (sonar key).
- SPECs promovidos `Draft` → `Approved` com link da issue no metadata.
- ⚠️ Execução pendente: 2 specs tocam `.github/workflows/` (#252, #253) → aprovação humana adicional no momento da implementação. #253 depende da decisão A/B/C do usuário.
