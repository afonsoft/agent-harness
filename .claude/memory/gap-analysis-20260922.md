# Gap Analysis — 2026-09-22

- Repository: `/home/ubuntu/repos/agent-harness` | Branch: `main` | Commit: `11404db` (post-merge PR #305 sidebar fix)
- Phase reached: `gate` (aguardando aprovação)
- Mode: `full`

---

## 1. Source Inventory

| Source | Status | Notes |
|---|---|---|
| `.specs/` | `present` | 100+ SPECs — todos em status terminal (Done/Completed/Deprecated) + 2 Drafts desta run |
| `docs/` | `present` | sincronizados (verificados: cockpit, finops no README — 34 refs) |
| `docs/architecture/` | `present` | architecture.md atualizado (22 refs cockpit/pipeline/worktree) |
| `.claude/knowledge/` | `present` | agora existe (README.md) — gap anterior resolvido |
| `.claude/memory/` | `present` | 7 gap-reports anteriores + orchestrator_stats/sessions |
| Tests/CI | `present` | unit 1017/1017 ✅ · integration 261/262 (1 flake) · cobertura **77.85%** |
| `gh auth` + remotes | `ok` | 0 issues abertas, 0 PRs abertos |

## 2. Dedup vs auditoria anterior (gap-analysis-20260920)

Todos os 4 CONFIRMADOS de 2026-09-20 entregues (issues #250–#253): pause/resume, docs sync, ratchet 65→73, sonar key → `DUPLICADO` (resolvidos). Ratchet re-emerge como novo ciclo (mesma política, nova medição).

## 3. Candidatos e Veredictos

| Key | Categoria | Veredito | Prioridade | SPEC | Evidência |
|---|---|---|---|---|---|
| `GAP-tests-flaky-integration` | tests | **CONFIRMADO** | medium-high | `SPEC-20260922-flaky-integration-tests` (Draft) | `PostMcpRemove` reproduzido hoje (10s deadline expirado, entry ainda presente); `SyncManual` race documentada; `POST /api/mcp/remove` é fire-and-forget (`Program.cs:2262`) e teste faz file-polling; ambos passam isolados |
| `GAP-automation-coverage-ratchet-bump` | automation | **CONFIRMADO** | medium | `SPEC-20260922-coverage-ratchet-bump-77` (Draft) | Medido 77.85% (reportgenerator, mesma metodologia do CI) vs `COVERAGE_THRESHOLD: 73` (`dotnet.yml:44`); política ratchet exige bump; ⚠️ toca workflow → aprovação humana |
| architecture.md drift | documentation | **REJEITADO** | — | — | 22 refs a cockpit/pipeline/worktree — doc sincronizado |
| README/feature drift | documentation | **REJEITADO** | — | — | 34 refs a cockpit/finops/kanban/board |
| sonar project key | automation | **REJEITADO** | — | — | `SONAR_PROJECT_KEY: afonsoft_agent-harness` correto |
| `.claude/knowledge/` ausente | documentation | **REJEITADO** | — | — | diretório existe com README.md |
| Frontend/UI test coverage | tests | **REJEITADO** | — | — | SPEC-014 adia Playwright explicitamente ("futuro") |
| Auth em endpoints | security | **REJEITADO** | — | — | `/api` group `RequireAuthorization`; hubs authorized; só `/framework-assets` anônimo (intencional) |
| `Taskboard.Maui` vazio | implementation | **REJEITADO** | — | — | stub intencional fase 2 (SPEC-000); compila na solution |
| `ORCHESTRATOR-ROADMAP.md` ausente | documentation | **REJEITADO** | — | — | referenciado só em memory antiga; não prescrito por regra vigente |

## 4. Housekeeping (não-spec)

- Branch `feature/agent-pipe_c6bc3ec5bc7d4cac9e31c0807f91c967-single-agent` (PR #291 merged via squash) persiste local+remoto; worktree `~/.taskboard/worktrees/pipe_c6bc…` já removido. Deletável.

## 5. Métricas

- Cobertura combinada (reportgenerator, `-assemblyfilters:"-*.Tests.*"`): **77.85%** (line-rate 0.7785)
- Build: limpo · Unit: 1017/1017 · Integration: 261/262 (flake `PostMcpRemove`)
- Issues: 0 abertas | PRs: 0 abertos | SPECs não-terminais: 2 Drafts desta run

## 6. Draft SPECs gerados (aguardando gate)

- `.specs/SPEC-20260922-flaky-integration-tests.md`
- `.specs/SPEC-20260922-coverage-ratchet-bump-77.md`

## 7. Ordem sugerida de execução

1. `flaky-integration-tests` primeiro (estabiliza CI para o PR do ratchet).
2. `coverage-ratchet-bump-77` depois (toca workflow — aprovação humana na implementação).
