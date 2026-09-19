# ESTADO_ORCHESTRATOR

> Arquivo de estado da skill `orchestrator` para o repositório `taskboard-ai`. Ler ao iniciar a sessão; escrever ao final de cada fase.

---

## Sessão

- **iniciado_em**: `2026-09-19 UTC` (sessão 2)
- **fase_atual**: `Phase 4 — E9 Verification Loop implementado (aguarda PR/merge)`
- **repositorio**: `afonsoft/taskboard-ai`
- **branch_trabalho**: `feature/devin-20260919-harness-verification-loop`
- **framework**: `afonsoft/skills` instalado via `npx skills add afonsoft/skills` (ver `skills-lock.json`)
- **framework_update_check**: `up-to-date` (commit `226758d` em `/home/ubuntu/repos/skills`)

### Epics em curso (fila sequencial — todos SPECs aprovados 2026-09-19)

| Epic | SPEC | Issue | Status |
|---|---|---|---|
| E6 - Workspace Isolation | `SPEC-20260919-harness-workspace-isolation` | #161 | merged via PR #177 (`4d70c26`) |
| E7 - Context & Memory | `SPEC-20260919-harness-context-memory` | #162 | merged via PR #183 (`c546ac0`) |
| E8 - Security Gateway | `SPEC-20260919-harness-security-permission-gateway` | #163 | merged via PR #189 (`86e8881`) + deploy |
| E9 - Verification Loop | `SPEC-20260919-harness-verification-loop` | #164 | implemented — S1..S5 done (#190-#192), SPEC Done, PR pending |
| E10 - CLI DB Reader | `SPEC-20260919-cli-db-reader` | #165 | queued |
| E11 - CLI Metrics | `SPEC-20260919-cli-metrics` | #166 | queued (blocked by #165) |
| E12 - Multi-Agent Orchestration | `SPEC-20260919-ade-multi-agent-orchestration` | #167 | queued (blocked by #161,#162,#164) |
| E13 - Living Specs | `SPEC-20260919-ade-living-specs` | #168 | queued (blocked by #167) |
| E14 - Observability & FinOps | `SPEC-20260919-ade-observability-finops` | #169 | queued (blocked by #167) |
| E15 - Cockpit HITL | `SPEC-20260919-ade-cockpit-hitl` | #170 | queued (blocked by #167,#168,#169) |
| E16 - Harness Platform (mestre) | `SPEC-20260919-ade-harness-platform` | #171 | queued (blocked by #170,#166,#163) |

```yaml
fila_e6:
  - { id: "E6/S1", task: "T1 Contracts & VOs (IWorkspaceIsolationService, WorktreeStatus, WorktreeSessionDto)", issue: 172, depends_on: [] }
  - { id: "E6/S2", task: "T2 Git Command Runner (timeouts, exit codes, stdio assíncrono)", issue: 173, depends_on: ["E6/S1"] }
  - { id: "E6/S3", task: "T3 GitWorktreeManager (create/diff/commit/cleanup)", issue: 174, depends_on: ["E6/S2"] }
  - { id: "E6/S4", task: "T4 Integração com orquestrador de agentes (cwd=worktree)", issue: 175, depends_on: ["E6/S3"] }
  - { id: "E6/S5", task: "T5 Verify: build/test/coverage gate, docs, SPEC→Done + PR", issue: 176, depends_on: ["E6/S4"] }
```

---

## Fase 0 — Preconditions

| Item | Status |
|---|---|
| Git inicializado | ✅ (`/home/ubuntu/repos/taskboard-ai`) |
| Remote `origin` GitHub | ✅ `afonsoft/taskboard-ai` |
| Acesso ao repositório | ✅ (`gh repo view` retornou metadados) |

---

## Fase 2 — Audit do Harness

| Item | Status |
|---|---|
| Git inicializado | ✅ (`/home/ubuntu/repos/taskboard-ai`) |
| Remote `origin` GitHub | ✅ `afonsoft/taskboard-ai` |
| Acesso ao repositório | ✅ (`gh repo view` retornou metadados) |
| `CLAUDE.md` (fonte única) | ✅ |
| `AGENTS.md` (thin reference) | ➖ N/A (`CLAUDE.md` proíbe criar `AGENTS.md`) |
| `.claude/settings.json` | ✅ |
| `.claude/rules/global-rules.md` | ✅ |
| `.claude/agents/` (review, plan, test) | ✅ |
| `.claude/CONTEXT.md`, `RULES.md`, `TOOLS.md`, `WORKFLOWS.md`, `README.md`, `MEMORY.md` | ✅ |
| `.claude/memory/` | ✅ |
| `.specs/` | ✅ SPEC-000..015 |
| `docs/architecture/` | ✅ |
| `docs/agents/` | ➖ N/A (não requerido pelo `CLAUDE.md` do projeto) |
| Skills instaladas (`.claude/skills`) | ✅ |
| Skills instaladas (`.devin/skills`) | ✅ (symlink para `.claude/skills`) |
| Skills instaladas (`.agent/skills`) | ✅ (symlink para `.claude/skills`) |
| `.devin/config.json` | ✅ |

---

## GAPs Identificados

| # | ID | Dimensão | Severidade | Descritivo | Tier Risco | Status |
|---|----|----------|------------|------------|------------|--------|
| 1 | `GAP-001` | CI/CD | P2 | Workflow `dotnet.yml` com cache NuGet, `concurrency`, `permissions`, SonarCloud, CodeQL e Dependabot | T2 Batchável | 🟢 done |
| 2 | `GAP-002` | Documentação | P4 | Docs não cobriam Kanban GitHub nem orquestração de agentes | T1 Auto | 🟢 done |
| 3 | `GAP-003` | Arquitetura | P3 | `AgentLogMessage` persistido em SQLite com EF Core | T2 Batchável | 🟢 done |
| 4 | `GAP-004` | Arquitetura | P3 | Adapter ACP JSON-RPC sobre stdin/stdout | T2 Batchável | 🟢 done |
| 7 | `GAP-007` | Arquitetura | P2 | `.devin/` e `.agent/` presentes com symlinks para `.claude/skills` e `config.json` | T2 Batchável | 🟢 done |

---

## Tarefas (Fase 4 — Fila DAG)

### Tarefas Pendentes

```yaml
- id: TASK-003
  desc: "Melhorar GitHub Actions (cache, concurrency, permissions, SonarCloud, CodeQL, Dependabot)"
  skill: /dotnet-github-actions
  gap_ref: GAP-001
  issue_ref: "https://github.com/afonsoft/taskboard-ai/issues/34"
  spec_ref: ".specs/SPEC-20260911-refine-github-actions.md"
  depends_on: []
  status: done
  concluido_em: "2026-09-11"

- id: TASK-004
  desc: "Persistir AgentLogMessage em SQLite (EF Core)"
  skill: /tdd-spec
  gap_ref: GAP-003
  issue_ref: "https://github.com/afonsoft/taskboard-ai/issues/35"
  spec_ref: ".specs/SPEC-20260911-persist-agent-logs.md"
  depends_on: []
  status: done
  concluido_em: "2026-09-11"

- id: TASK-005
  desc: "Adapter ACP JSON-RPC (stdin/stdout) para agentes que suportam o protocolo"
  skill: /tdd-spec
  gap_ref: GAP-004
  issue_ref: "https://github.com/afonsoft/taskboard-ai/issues/36"
  spec_ref: ".specs/SPEC-20260911-acp-json-rpc.md"
  depends_on: []
  status: done
  concluido_em: "2026-09-11"
```

### Tarefas Concluídas

```yaml
- id: TASK-001
  desc: "Kanban GitHub (Octokit, MudBlazor, labels backlog/in-progress/review/done)"
  skill: /tdd-spec
  gap_ref: ""
  issue_ref: "PR #13"
  spec_ref: ".specs/SPEC-010-integrations.md"
  depends_on: []
  status: done
  concluido_em: "2026-09-10"

- id: TASK-002
  desc: "Orquestração de agentes CLI via ACP + SignalR (SPEC-015)"
  skill: /tdd-spec
  gap_ref: ""
  issue_ref: ""
  spec_ref: ".specs/SPEC-015-agent-orchestration.md"
  depends_on: [TASK-001]
  status: done
  concluido_em: "2026-09-10"
```

---

## Checkpoints de Sanidade

| # | Após Tarefa | Data | Reavaliação Necessária? | Ação Tomada |
|---|-------------|------|------------------------|------------|
| 1 | TASK-002 | 2026-09-10 | Não | build + testes verdes; seguir para docs e PR |

---

## Batch de Execução (Tier T2)

| # | GAP | Tarefa | Skill | Status |
|---|-----|--------|-------|--------|
| 1 | GAP-001 | Atualizar `.github/workflows/dotnet.yml` e adicionar `codeql.yml`, `dependabot.yml` | /dotnet-github-actions | 🟡 pending_approval |

---

## Fase 4 — Novas Tarefas Aprovadas (Sessão 2026-09-10)

As specs aprovadas nesta sessão foram registradas para execução:

### Novas GAPs

| # | ID | Dimensão | Severidade | Descritivo | Tier Risco | Status |
|---|---|---|---|---|---|---|
| 5 | `GAP-005` | Frontend/UX | P2 | Telas de login, configurações e skills (tema dark/light, agentes, skills) | T2 | 🟡 queued |
| 6 | `GAP-006` | DevEx | P4 | Instalador leve `install-cli.sh` em `/usr/local/bin` com alias | T2 | 🟡 queued |

### Novas Tarefas

```yaml
- id: TASK-006
  desc: "Implementar telas de login, configurações e skills (SPEC-20260910-ui-login-settings-skills)"
  skill: /tdd-spec
  gap_ref: GAP-005
  issue_ref: "https://github.com/afonsoft/taskboard-ai/issues/24"
  spec_ref: ".specs/SPEC-20260910-ui-login-settings-skills.md"
  depends_on: []
  status: done
  concluido_em: "2026-09-11"

- id: TASK-007
  desc: "Implementar instalador CLI `install-cli.sh` em /usr/local/bin (SPEC-20260910-install-cli-sh)"
  skill: /tdd-spec
  gap_ref: GAP-006
  issue_ref: "https://github.com/afonsoft/taskboard-ai/issues/25"
  spec_ref: ".specs/SPEC-20260910-install-cli-sh.md"
  depends_on: []
  status: done
  concluido_em: "2026-09-11"
```

### Phase 3 — Issues Criadas

| Epic | Slice | GitHub Issue |
|---|---|---|
| E1 - Refinar GitHub Actions | E1/S1 | #30 / #34 |
| E2 - Persistir logs de agentes | E2/S1 | #31 / #35 |
| E3 - Adapter ACP JSON-RPC | E3/S1 | #32 / #36 |
| E4 - Completar harness Devin CLI e Antigravity | E4/S1 | #33 / #37 |

### Próximos passos

1. Escolher um slice e executar via `execute-spec` (Fase 4).
2. Revalidar `dotnet build` e `dotnet test` após cada slice.
3. Ao concluir um Epic, executar QA e revisão (Fase 5).

---

## Fase 7 — Verificação Final

| Item | Resultado |
|---|---|
| `dotnet build` | ✅ pass |
| `dotnet test` | ✅ pass (89 unit, 9 integration) |
| SPECs aprovados/completados | ✅ 17 revisados |
| Issues abertas | 4 (#38, #39, #40, #41) |
| TODO/FIXME/ponytail críticos | ✅ nenhum no `src/` |
| Branches órfãs | ✅ nenhum |

### Ações da Fase 7

- Corrigido DI entre `AgentOrchestrationService` (Singleton) e `IAgentLogRepository` (Scoped) via `IServiceScopeFactory`.
- Adicionada EF Core migration `AddAgentLogs` para entidade `AgentLog`.
- Atualizado `.claude/skills/orchestrator/SKILL.md` com afonsoft/skills.
- Commits aplicados na branch `update/skills-lock`.

### Ações da Fase 7 (reavaliação)

- Reconciliadas issues #38, #39, #40 e #41 abertas no GitHub; verificado que o conteúdo já estava implementado no commit `0759c81`.
- Fechadas issues #38 a #41 com comentário em português e referência ao commit de implementação.
- `dotnet build` e `dotnet test` mantidos verdes (89 unit + 9 integration).
- Estado do orquestrador atualizado para refletir GAPs concluídos e `.devin`/`.agent` presentes.

**Status**: fluxo concluído sem gaps pendentes.

---

## Execução da sessão 2026-09-11

### SPECs concluídos

| SPEC | Status | Commit |
|---|---|---|
| `SPEC-20260911-admin-change-password` | Completed | `2e82153` |
| `SPEC-20260911-kanban-smartsheet-layout` | Completed | `9971c41`, `83378ab` |
| `SPEC-20260911-settings-ux-redesign` | Completed | `9375cce` |
| `SPEC-20260911-skills-ux-redesign` | Completed | `21b7fda` |

### Verificação final

- `dotnet build` na Release: ✅ pass
- `dotnet test` Taskboard.sln: ✅ 93 unit + 11 integration


---

## Execução da sessão 2026-09-15

### SPECs em execução

| SPEC | Status | Issue | Branch |
|---|---|---|---|
| `SPEC-20260915-github-board-unified` | In implementation | #75 | `feature/devin-20260915-github-board-unified` |

### Bugfix prévio (mesma branch)

- `KanbanBoard` passava `GetAllLabels()` a `GetIssuesAsync`; a API do GitHub trata `labels` como filtro AND → issues recém-criadas nunca apareciam. Corrigido para buscar sem filtro e agrupar no cliente.
- `GitHubService.CreateIssueAsync`/`UpdateIssueColumnAsync` passaram a garantir a label da coluna via `EnsureLabelExistsAsync` (evita HTTP 422).

### Verificação

- `dotnet build`: ✅ 0 warnings, 0 errors
- `dotnet test`: ✅ 180 unit + 31 integration

### Encerramento (2026-09-15)

- SPEC-20260915-github-board-unified: merged via PR #76 (squash `7e9dc0d`), Issue #75 fechada.
- Deploy: imagem `taskboard-ai:latest` rebuildada de `main`; container `taskboard` recriado com `--env-file .env` (TOKEN_OK, API respondendo).

---

## Execução da sessão 2026-09-17 (orchestrator run)

### Phase -1 — Framework

- `afonsoft/skills` clone `/home/ubuntu/repos/skills` @ `bd78a76c` — up-to-date com `origin` (fetch OK, sem commits novos).

### Phase 0 — Preconditions

- Git clean ✅ | `gh auth` ✅ (afonsoft) | remote `afonsoft/taskboard-ai` ✅ | dotnet 10.0.112 + node v24.16.0 ✅

### Phase 1 — Reconciliação de Issues

- **#81** (epic gap-analysis-20260915): filhos #82/#83 CLOSED, PRs #84–#86 merged → **fechada** com comentário pt-BR.
- **#74** ("Test"/"teste", label todo): issue de teste sem vínculo com trabalho — **reportada ao usuário** (não fechada automaticamente).

### Phase 6 — SPECs com status defasado

6 SPECs marcados `Approved` mas já implementados (issues fechadas + código em main) → status corrigido para `Done`:

| SPEC | Evidência |
|---|---|
| SPEC-20260910-install-cli-sh | `install-cli.sh` no repo; issue #25 CLOSED |
| SPEC-20260910-system-configuration | `RuntimeConfigurationService` + endpoints `/api/configuration` |
| SPEC-20260911-acp-json-rpc | `JsonRpcAcpClient` + testes; issue #36 CLOSED |
| SPEC-20260911-admin-change-password | `PUT /api/admin/password` (Program.cs); commit `2e82153` |
| SPEC-20260911-api-docs-and-prompts | `UseSwaggerUI` (Program.cs) + página `/prompts` |
| SPEC-20260911-persist-agent-logs | `AgentLog` EF Core + migration `AddAgentLogs`; issue #35 CLOSED |

### Entregas da sessão anterior (registradas)

| SPEC | Status | PR |
|---|---|---|
| SPEC-20260917-skills-installer | Done | #87 (squash `9ec7cf2`) |
| SPEC-20260917-rag-mcp-provisioning | Done | #87 |
| SPEC-20260917-cli-agents-terminal | Done | #88 (squash `55a3f8b`) |

### Phase 7 — Verificação final

- `dotnet build -c Release`: ✅ 0 warnings/errors · `dotnet test`: ✅ 353 (265 unit + 88 integration) · `docker build`: ✅ (imagem descartada — deploy é no host)
- Deploy: host systemd `taskboard-server` enabled+running; `/agents` reporta 5/5 CLIs autenticados; container Docker removido
- TODO/FIXME em `src/`: nenhum
- Issues abertas restantes: #74 (teste — aguardando decisão do usuário)
- Branches locais órfãs (candidatas a cleanup, não removidas sem confirmação): `chore/update-afonsoft-skills`, `devin/spec-*`, `feat/docs-architecture-and-harness`, `feature/devin-20260910-*`, `fix/codeql-pr26`

**Status**: fluxo concluído; pendências = decisão do usuário sobre #74 e cleanup de branches.

### Cleanup final (2026-09-17)

- Issue #74 ("Test") fechada — zero issues abertas.
- Branches locais removidas (11): todas merged ou superseded (squash).
- Branches remotas removidas (3, PRs merged): `chore/update-afonsoft-skills`, `feature/devin-20260914-update-skills`, `feature/devin-20260915-wasm-post-migration-hardening`.
- Estado final: apenas `main` local + `origin/main`. Nenhuma pendência.

---

## Execução da sessão 2026-09-19 (sessão 2 — ADE/Harness)

### Phase 6 — Aprovação em lote

- 11 SPECs ADE/Harness aprovados (`Draft`→`Approved`); Epic issues E6–E16 (#161–#171) criadas no GitHub; commit `35b069f`.

### Phase 4 — E6 Workspace Isolation (branch `feature/devin-20260919-harness-workspace-isolation`)

|| Slice | Issue | Commit | Entrega |
||---|---|---|---|
|| S1 | #172 | `8a836f5` | `IWorkspaceIsolationService`, `WorktreeStatus`, `WorktreeSession`, DTOs, EF config + migration `AddWorktreeSessions` |
|| S2 | #173 | `046f21a` | `GitCommandRunner` (`ArgumentList`, timeout, stdio async, `WithoutTaskboardEnv`) |
|| S3 | #174 | `33163f3` | `GitWorktreeManager` + `IWorktreeSessionRepository`/`EfCoreWorktreeSessionRepository`, `WorktreePaths` (confines, branch-part sanitize) |
|| S4 | #175 | `84ecc53` | `AgentOrchestrationService` isola runs com `RepoPath` git (cwd=worktree, retain-on-failure, fallback seguro) |
|| S5 | #176 | — | Endpoints `POST/GET/DELETE /api/harness/worktrees[/diff]`, docs api.md (+pt-br), SPEC→Done |

### Phase 7 — Verificação E6

- `dotnet build` Debug: ✅ 0 warnings/0 errors · unit: ✅ 509 · integration: ✅ 159
- Decisões: `git diff <base>` (two-dot) cobre mudanças commitadas+pendentes (RF-002); sanitize de branch exclui `.` (git rejeita `..`); isolamento é best-effort com fallback logado — nunca quebra orquestração.
- QA self-review: edge `sessão-órfã` corrigido via `Reactivate` upsert (`a90a194`).
- **PR #177** merged → `4d70c26` (slices #172–#176 fechadas; CI: fix hermético de `TerminalDisabledTests` via `FakeAgentDiscoveryService`).

### Phase 4 — E7 Context & Memory (branch `feature/devin-20260919-harness-context-memory`)

| Slice | Issue | Commit | Entrega |
|---|---|---|---|
| S1 | #178 | `*` | `MemoryType`, `ProjectMemoryItem`, `IContextCompiler`/`IContextCompactor`/`IMemoryService`, EF config + migration `AddProjectMemoryItems`, `EfCoreMemoryService` |
| S2 | #179 | `*` | `ProjectContextCompiler` — descoberta recursiva de instruction files, `<env>` + git context, `<project_memory>` via remote `origin` |
| S3 | #180 | `0ddd55b` | `ContextCompactor` — trigger 80% budget, sumariza meio preservando system + últimas 5 turmas/última instrução user |
| S4 | #181 | `2e25f76` | Endpoints `POST /api/harness/context/compile`, `POST/GET/DELETE /api/harness/memory`, docs, SPEC→Done |

### Phase 7 — Verificação E7

- `dotnet build -c Release`: ✅ 0 warnings/0 errors · unit: ✅ 527 · integration: ✅ 163
- Fix em S4: `GET memory?take` obrigatório → `BadHttpRequestException` mapeada para 500 pelo `GlobalExceptionHandler`; `take` agora opcional (`int?`).
- **PR #183** merged → `c546ac0` (slices #178–#182 fechadas).

### Phase 4 — E8 Security Gateway (branch `feature/devin-20260919-harness-security-gateway`)

| Slice | Issue | Commit | Entrega |
|---|---|---|---|
| S1 | #184 | `d77e72d` | `SecurityRiskLevel`, `SecurityPolicyMode`, `SecurityAccessDeniedException` (`Taskboard:00025`), `ICommandRiskClassifier`, `IPermissionGateway`, SecurityDtos |
| S2 | #185 | `76e523a` | `DynamicCommandClassifier` — lexer por segmentos (quotes, chains, redirects, env), tabelas git/dotnet/npm/rm, fail-closed; paths resolvidos contra o jail |
| S3 | #186 | `*` | `PathJailValidator` (canônico + symlink escape) + `SecretScrubber` (regex compiladas ghp_/pat_/sk-/AWS/Bearer/PEM) |
| S4+S5 | #187/#188 | `*` | `PermissionGateway` (matriz de políticas; escape=jail deny duro), endpoint `POST /api/harness/security/evaluate`, docs, SPEC→Done |

### Phase 7 — Verificação E8

- `dotnet build -c Release`: ✅ 0 warnings/0 errors · unit: ✅ 611 · integration: ✅ 167
- Decisões: `Classify(command, worktreePath)` path-aware resolve conflito RF-001 vs §5 (`rm -rf` dentro → WorkspaceWrite, fora → Dangerous); `EscapesSandbox` no assessment distingue deny duro de approvable.
- **PR #189** merged → `86e8881` (slices #184–#188 fechadas). Deploy: `taskboard-server` reiniciado com publish novo; `/api/meta` 200, endpoints `/api/harness/security/*` protegidos (401 anônimo).

### Phase 4 — E9 Verification Loop (branch `feature/devin-20260919-harness-verification-loop`)

| Slice | Issue | Commit | Entrega |
|---|---|---|---|
| S1 | #190 | `ca53a4a` | `VerificationStatus`, `VerificationReport` aggregate + EF config/migration `AddVerificationReports`, `IVerificationEngine` + DTOs, `IProcessRunner`/`ProcessCommandRunner` genérico |
| S2 | #191 | `9874458` | `CompilerErrorParser` (regex stdout build), `TestFailureParser` (TRX XML), `CoverageCalculator` (Cobertura XML) |
| S3–S5 | #192/#193/#194 | — | `DotNetVerificationEngine` (format→build→test+coverage), `IVerificationReportRepository`/`EfCore…`, `VerificationLoop` (retry callback + escalate), integração opt-in no `AgentOrchestrationService` (`VerifySolutionFile`/`VerifyMinCoverage`/`VerifyMaxAttempts`), endpoint `POST /api/harness/verification/run`, docs, SPEC→Done |

### Phase 7 — Verificação E9

- `dotnet build -c Release`: ✅ 0 warnings/0 errors · unit: ✅ 627 · integration: ✅ 169
- Decisões: `Succeeded` só após verificação passar (era marcado antes — corrigido); falha inicial do agente agora marca `Failed` (bug semântico pré-existente); verificação só roda quando `VerifySolutionFile` setado.
- Race em teste de orquestração: assert de `MarkCompletedAsync` movido para polling no mock (verificação roda entre log "exit code 0" e o mark).
