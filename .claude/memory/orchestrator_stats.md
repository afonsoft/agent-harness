# ESTADO_ORCHESTRATOR

> Arquivo de estado da skill `orchestrator` para o repositório `agent-harness`. Ler ao iniciar a sessão; escrever ao final de cada fase.

---

## Sessão

- **iniciado_em**: `2026-09-21 UTC` (sessão 4)
- **fase_atual**: `Phase 5 — 3 SPECs aprovadas entregues como PRs abertos (#292, #293, #294)`
- **repositorio**: `afonsoft/agent-harness` (renomeado de `taskboard-ai` em 2026-09-20)
- **branch_trabalho**: `feature/devin-20260921-acp-v2-readiness` (stacked em `feature/20260921-ai-code-chat-ux`)
- **framework**: `afonsoft/skills` instalado via `npx skills add afonsoft/skills` (ver `skills-lock.json`)
- **framework_update_check**: `up-to-date` (commit `dc353de` em `/home/ubuntu/repos/skills`)

### Entregas da sessão 2026-09-21 (3 SPECs aprovadas)

| SPEC | Issue | Status | Entrega |
|---|---|---|---|
| `SPEC-20260921-ai-code-thread-config` | #288 | PR aberto | PR #292 — repo combobox, workspace auto-resolve, model tier + ACP-first catalog, auditoria ModelSource, tooltips nativos do rail |
| `SPEC-20260921-ai-code-chat-ux` | #289 | PR aberto | PR #293 — tool-call renderers, fila FIFO de prompts, medidor de contexto, fork/retry, quick-switch modelo/modo |
| `SPEC-20260921-acp-v2-readiness` | #282 | PR aberto | PR #294 — `IAcpDialect`/`AcpV1Dialect`/`AcpV2Dialect`, negociação por conexão, `ITurnTracker` v1/v2, parser v2, batch NDJSON |

### Verificação 2026-09-21

- `dotnet build`: ✅ 0 warnings/0 errors
- `dotnet test` unit: ✅ **997/997**
- `dotnet test` integration: ✅ 257/259 — 1 falha pré-existente (`AgentRunEndpointsTests.Dado_TemplateMuitoLongo`, reproduz na base limpa) + 1 flake MCP (passa isolado)
- `dotnet format --verify-no-changes`: ✅ limpo
- v1 invisível: default `MaxProtocolVersion=1`, wire v1 inalterado; v2 só com flag explícita

### Epics em curso (fila sequencial — todos SPECs aprovados 2026-09-19)

| Epic | SPEC | Issue | Status |
|---|---|---|---|
| E6 - Workspace Isolation | `SPEC-20260919-harness-workspace-isolation` | #161 | merged via PR #177 (`4d70c26`) |
| E7 - Context & Memory | `SPEC-20260919-harness-context-memory` | #162 | merged via PR #183 (`c546ac0`) |
| E8 - Security Gateway | `SPEC-20260919-harness-security-permission-gateway` | #163 | merged via PR #189 (`86e8881`) + deploy |
| E9 - Verification Loop | `SPEC-20260919-harness-verification-loop` | #164 | merged via PR #195 (`a9b5d23`) + deploy |
| E10 - CLI DB Reader | `SPEC-20260919-cli-db-reader` | #165 | merged via PR #201 (`846a01f`) + deploy |
| E11 - CLI Metrics | `SPEC-20260919-cli-metrics` | #166 | merged via PR #209 + fixes #210/#211/#212 (`0cbbf66`) + deploy validado |
| E12 - Multi-Agent Orchestration | `SPEC-20260919-ade-multi-agent-orchestration` | #167 | merged via PR #218 (`6258c4f`) + deploy |
| E13 - Living Specs | `SPEC-20260919-ade-living-specs` | #168 | Done — merged + slices #220–#224 fechadas |
| E14 - Observability & FinOps | `SPEC-20260919-ade-observability-finops` | #169 | Done — merged (recurring jobs PR #234, maintenance jobs PR #235) |
| E15 - Cockpit HITL | `SPEC-20260919-ade-cockpit-hitl` | #170 | Done — merged via PR #245 (`03d90c1`) + deploy; issue fechada 2026-09-20 |
| E16 - Harness Platform (mestre) | `SPEC-20260919-ade-harness-platform` | #171 | Done — todos sub-Epics entregues; issue fechada 2026-09-20 |

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
| Git inicializado | ✅ (`/home/ubuntu/repos/agent-harness`) |
| Remote `origin` GitHub | ✅ `afonsoft/agent-harness` |
| Acesso ao repositório | ✅ (`gh repo view` retornou metadados) |

---

## Fase 2 — Audit do Harness

| Item | Status |
|---|---|
| Git inicializado | ✅ (`/home/ubuntu/repos/agent-harness`) |
| Remote `origin` GitHub | ✅ `afonsoft/agent-harness` |
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
  issue_ref: "https://github.com/afonsoft/agent-harness/issues/34"
  spec_ref: ".specs/SPEC-20260911-refine-github-actions.md"
  depends_on: []
  status: done
  concluido_em: "2026-09-11"

- id: TASK-004
  desc: "Persistir AgentLogMessage em SQLite (EF Core)"
  skill: /tdd-spec
  gap_ref: GAP-003
  issue_ref: "https://github.com/afonsoft/agent-harness/issues/35"
  spec_ref: ".specs/SPEC-20260911-persist-agent-logs.md"
  depends_on: []
  status: done
  concluido_em: "2026-09-11"

- id: TASK-005
  desc: "Adapter ACP JSON-RPC (stdin/stdout) para agentes que suportam o protocolo"
  skill: /tdd-spec
  gap_ref: GAP-004
  issue_ref: "https://github.com/afonsoft/agent-harness/issues/36"
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
  issue_ref: "https://github.com/afonsoft/agent-harness/issues/24"
  spec_ref: ".specs/SPEC-20260910-ui-login-settings-skills.md"
  depends_on: []
  status: done
  concluido_em: "2026-09-11"

- id: TASK-007
  desc: "Implementar instalador CLI `install-cli.sh` em /usr/local/bin (SPEC-20260910-install-cli-sh)"
  skill: /tdd-spec
  gap_ref: GAP-006
  issue_ref: "https://github.com/afonsoft/agent-harness/issues/25"
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
- Deploy: imagem `agent-harness:latest` rebuildada de `main`; container `taskboard` recriado com `--env-file .env` (TOKEN_OK, API respondendo).

---

## Execução da sessão 2026-09-17 (orchestrator run)

### Phase -1 — Framework

- `afonsoft/skills` clone `/home/ubuntu/repos/skills` @ `bd78a76c` — up-to-date com `origin` (fetch OK, sem commits novos).

### Phase 0 — Preconditions

- Git clean ✅ | `gh auth` ✅ (afonsoft) | remote `afonsoft/agent-harness` ✅ | dotnet 10.0.112 + node v24.16.0 ✅

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
- **PR #195** merged → `a9b5d23` (slices #190–#194 fechadas). Deploy: `taskboard-server` reiniciado; `VerificationReports` migrada no SQLite do serviço; endpoint protegido (401 anônimo).

### Phase 4 — E10 CLI DB Reader (branch `feature/devin-20260919-cli-metrics`)

| Slice | Issue | Commit | Entrega |
|---|---|---|---|
| S1 | #196 | `*` | `CliDbSource`/`CliDbSourceStatus`/`CliDatabaseMap`, `ICliDatabaseLocator`/`ICliDatabaseReader`/`ICliDbExtractor`, records `CliSessionRecord`/`CliUsageRecord`/`CliExtractionResult`/`CliDbReadOptions`, exceções `CliDbAccessDenied`/`CliDbReadFailed` (`Taskboard:00030/31`) |
| S2 | #197 | `*` | `CliDatabaseLocator` (globs sob $HOME, nunca cria dirs), `SqliteCliDatabaseReader` (ro + WAL-copy p/ temp + cleanup + retry), `ICliDbConnection` com SELECT estruturado (identificadores validados, denied tables, colunas-secretas filtradas, budgets) |
| S3 | #198 | `*` | `CliDbSchemaFingerprinter` (user_version/application_id/signature) + `CliDbExtractorBase` (locate→open→fingerprint→extract, cursor `{file}|{rowid}`) |
| S4 | #199 | `*` | Extractors: Codex `threads`, OpenCode `session`(+usage/cost), Devin `sessions`, Antigravity `conversation_summaries`, Cline `hub_events` agrupado, Claude ctx-mode `session_meta` (experimental); fingerprints capturados de schemas vivos; DI |

### Phase 7 — Verificação E10

- `dotnet build -c Release`: ✅ 0 warnings/0 errors · unit: ✅ 667 · integration: ✅ 169
- Schemas reais divergiam do inventário presumido — whitelists corrigidas após inspeção somente-schema dos DBs vivos; Codex shards → pattern `state_*.sqlite`; Cline → `hub-events-*.db` (connectors.db fora do glob + denylist).
- Fixes: regex de coluna-secreta com boundary `_` (`tokens_*` passam, `access_token` não); pooling do fixture vazava handle em arquivo deletado (`Pooling=false`); `Dispose` tolerante a home inexistente.

### Phase 4 — E11 CLI Metrics (branch `feature/devin-20260919-cli-metrics`)

| Slice | Issue | Commit | Entrega |
|---|---|---|---|
| S1 | #202 | `232a284` | `CliMetricSource`/`CliSessionMetric`/`CliDailyUsageAggregate` + EF configs (índices únicos `(Kind,SourceName)` e `(SourceId,ExternalId)`) + migration `AddCliMetrics` |
| S2 | #203 | `0e037c6` | `ICliMetricsRepository` (DTO-facing) + `EfCoreCliMetricsRepository` + `CliMetricsService` — skip por assinatura de arquivo (paths+mtime+size), watermark resume, upsert dedupe, chunks de 500, recompute idempotente de agregados/dia, isolamento de falha por extractor, retenção configurável (raw 90d; agregados indefinidos); `ICliDbExtractor.Sources` público + `ICliDatabaseLocator.Stat` |
| S3 | #204 | `ab5d1f2` | `CliMetricsSyncService` (BackgroundService+PeriodicTimer, bind `Taskboard:CliMetrics:{Enabled,SyncIntervalMinutes}`, passo inicial no startup) + `CliMetricsSyncCoordinator` single-flight |
| S4 | #205 | `682ca5a` | `POST /api/local/cli-metrics/sync` (404 disabled, in-flight flag), `GET sources/summary?period=/sessions?kind&from&to&take`; factory de testes com `Taskboard:HomeDir` vazio |
| S5 | #206 | `8b028fc` | `/agents`: Sessions 7d, Tokens, Last activity, badge Metrics DB, botão Sync now; `CliMetricsTotalsDto.LastActivityUtc`; TaskboardClient methods |
| S6 | #207 | `240cbd2` | `ICliUsageMetricsProvider` + `EfCoreCliUsageMetricsProvider` (agregados kind/modelo p/ E14) |
| S7 | #208 | — | docs en/pt-br, SPEC→Done |

### Phase 7 — Verificação E11

- `dotnet build -c Release`: ✅ 0 warnings/0 errors · unit: ✅ 686 · integration: ✅ 174 (1 flake inicial do PTY — HomeDir do factory precisava existir; fix: `Directory.CreateDirectory`)
- Decisões: cursor do extractor guardado em todas as linhas de source do extractor (resume lê a primeira); `CliSessionMetric` clampa `StartedAtUtc` futuro (clock skew); `CliDailyUsageAggregate.Reset` para recompute idempotente.

### Pós-merge E11 — fixes de produção (PRs #210/#211/#212)

- #210 `b1b2520`: docs ADE + Cline `created_at` epoch **ms** (era lido como s) + cap 512MB→4GB (DBs reais ~1.6GB).
- #211 `53cb3a3`: `SELECT "rowid"` em tabela com `INTEGER PRIMARY KEY` reporta o nome da coluna PK (`sequence`) → `GetOrdinal("rowid")` explodia. Fix: `AS "<col>"` em toda coluna selecionada.
- #212 `0cbbf66`: fontes em `Error` com arquivo inalterado nunca retentavam (OpenCode preso no erro do build antigo) → `Status=Error` força retry; cursor Cline só avançava em linhas com `session_id` → preso em `|0` → agora avança por rowid escaneada.
- Deploy validado 2026-09-19: 6/6 fontes `CopiedToTemp` sem erro — Antigravity 5, Claude 25, Codex 31, Devin 381, OpenCode 155 sessões; Cline 0 (db real só tem 6 linhas sem session_id — correto); 78 agregados/dia.
- Warning pré-existente (E7): `ProjectMemoryItem.Tags` sem value comparer — candidato a cleanup futuro.
- Warning (E12): `PipelineStageExecution.DependsOn` sem value comparer — `DependsOn` é imutável pós-criação, não-bloqueante; agrupar no mesmo cleanup do E7.
- Flake conhecido: `McpEndpointsTests.PostMcpRemove` falha em suite cheia, passa isolado (mexe em `~/.claude.json` real).

### Phase 4 — E12 Multi-Agent Orchestration (branch `feature/devin-20260919-ade-multi-agent-orchestration`)

| Slice | Issue | Commit | Entrega |
|---|---|---|---|
| S1 | #213 | `d257ae3` | `AgentRole`/`StageStatus`/`PipelineStatus`/`PipelineStageKind` + IDs; `PipelineDefinition`/`PipelineStage`/`PipelineExecution`/`PipelineStageExecution` (DAG c/ detecção de ciclo, `INVALID_PIPELINE_DAG`); EF configs + migration; `PipelineTemplates` (standard-feature/quick-patch/test-driven); contracts `IPipelineOrchestrator` + DTOs |
| S2+S3 | #214/#215 | `ea7710f` | `PipelineEngine` (dispatch por escopo, transições persistidas antes do spawn, tasks paralelas rastreadas, `DrainAsync`, cancelamento por execução) + `PipelineContextSynthesizer` (handoff `handoffSummary` → upstream context) |
| S4+S5 | #216/#217 | — | `PipelineExecutionAppService` (scoped) + `PipelineEngine` singleton via `IServiceScopeFactory`; `PipelineEngineService` (hosted, timer); endpoints `templates/start/{id}/approve/retry/cancel` (201/200/202/400/404); docs en/pt-br; SPEC→Done |

### Verificação E12

- `dotnet build -c Release`: ✅ 0 warnings/0 errors · unit: ✅ 706 (18 PipelineEngine + 12 domain) · integration: ✅ 181 (7 PipelineEndpoints; flake conhecido `PostMcpRemove` isolado ✓)
- Decisões: `IWorkspaceIsolationService` é scoped → resolvido por escopo dentro do engine singleton (não por ctor); `DbContext` nunca compartilhado entre estágios paralelos (scope por stage task); estágios marcados `Running` + save antes do dispatch (elimina race Pending→Complete); `PipelineEngine` mora em Application (deps todos em Contracts).

### Phase 4 — E13 Living Specs (branch `feature/devin-20260920-ade-living-specs`)

| Slice | Issue | Commit | Entrega |
|---|---|---|---|
| S1 | #220 | — | `SpecStatus`/`SpecLintWarning`/`LivingSpecification`/`SpecRequirement`/`SpecTask` em Domain.Shared; `ISpecDocumentParser` em Contracts; `MarkdigSpecParser` (AST: seções `## N.`, tabela de metadados, RF/FR heading+bullet, BDD criteria, task checkboxes, "Files to create or modify") + normalização de status messy; corpus test cobre as 94 specs reais |
| S2+S3 | #221/#222 | — | `SpecAppService` (scan `Taskboard:SpecsDir` ou `.specs/` mais próximo; rewrite cirúrgico da célula Status, UTF-8) + `SpecDriftDetector` (stale→Done; Done+arquivos removidos→Deprecated) + endpoints `/api/specs` (list/status/{id}/drift-report); `Taskboard:00033` |
| S4 | #223 | — | Página `/specs` (filtros de status, busca, banner de drift, badges), `SpecDetailDialog` (markdown sanitizado via `MarkdownRenderer`), `RunSpecDialog` → `POST /api/harness/pipelines/start` (E12); link no NavMenu |

### Verificação E13

- `dotnet build -c Release`: ✅ 0 warnings/0 errors · unit: ✅ 731 · integration: ✅ 187/188 (flakes conhecidos: `PostMcpRemove` — `.claude.json` real; `CliMetricsEndpoints.SyncManual` — race do sync de startup com o coordinator single-flight; ambos passam isolados)
- Decisões: `LivingSpecification` em Domain.Shared (Integrations não referencia Domain — layering real do repo); specs file-backed — sem tabela EF, disco = fonte da verdade; `repositoryPath` no detail DTO = parent de `.specs/` (alvo do "Run with Agent").

### Phase 4 — E14 Observability & FinOps (branch `feature/devin-20260920-ade-observability-finops`)

| Slice | Issue | Entrega |
|---|---|---|
| S1 | #226 | `HarnessTelemetrySource` (Domain.Shared — Application não vê Integrations): spans `harness.run`/`harness.stage`/`harness.tool_call`/`harness.verification` com tags `harness.run_id`/`agent.type`/`model.name`/`tokens.*`/`cost.usd`; spans em run (orchestrator), stage (PipelineEngine) e verification (DotNetVerificationEngine, RunId/AgentType/ModelName no request DTO); OTLP só quando `Taskboard:Telemetry:OtlpEndpoint` configurado (guardrail §8); pacotes OpenTelemetry 1.19.0 + OTLP exporter + Extensions.Hosting |
| S2 | #227 | `TokenUsageType`/`TokenUsage`/`ModelPriceRateInfo` (Domain.Shared); `RunCostMetric` + `ModelPriceRate` (Domain, `decimal` em tudo); `AgentRunState.BudgetExceeded`; `PipelineExecution.BudgetCapUsd`; EF configs + seed `HasData` (12 rates: anthropic/openai/deepseek/google + `*` fallback); migration `AddFinOpsMetrics` |
| S3 | #228 | `TokenCostCalculator` (exact→prefixo mais longo→`*`, matemática decimal exata); `TokenUsageParser` (linhas JSON stdout: Claude `usage`, Codex `token_count`/`total_token_usage`, OpenAI `prompt/completion_tokens`); `IFinOpsService`/`FinOpsService` (record/cumulative/summary/telemetry, runId normalizado Guid↔"N"); `AgentExecutionRequest.MaxBudgetUsd` + `AgentExecutionResult.Usage`; orchestrator cancela mid-flight quando usage streamada cruza o cap → `BudgetExceeded`; PipelineEngine: gate pré-dispatch + métrica por estágio + cancel pós-estágio |
| S4 | #229 | Endpoints `GET /api/harness/finops/summary?period=` e `GET /api/harness/runs/{id}/telemetry`; página `/finops` (cards, barras por agente/modelo, burn diário, lookup de run); `TaskboardClient` + NavMenu |
| S5 | #230 | 39 testes novos (calculator AC1 exato, parser 3 formatos, service, budget mid-flight+pós-run, span tags AC3, 4 integração endpoints); docs en/pt-br; SPEC→Done |

### Verificação E14

- `dotnet build -c Release`: ✅ 0 warnings/0 errors · unit: ✅ 755 · integration: ✅ 192 (flakes conhecidos passaram neste run)
- Decisões: primitivos de telemetria/custo/parser em Domain.Shared (mesmo desvio de E13 — `Application`/`Integrations` não se referenciam); `DateTime` (não `DateTimeOffset`) em `RunCostMetric.RecordedAtUtc` — SQLite não traduz comparações/ORDER BY de DateTimeOffset; telemetry nunca quebra orquestração (helpers `Try*` com AppendLog de aviso); `IFinOpsService` resolvido por escopo no engine via `GetService` (null-safe nos testes).

---

## Execução da sessão 2026-09-20 (sessão 3 — rename + reconciliação)

### Entregas anteriores registradas (contexto)

- Repo renomeado `afonsoft/taskboard-ai` → `afonsoft/agent-harness` (GitHub redirect ativo).
- PRs mergeados: #232 skills sync, #233 terminal resilience, #234 FinOps recurring job + PTY resize, #235 maintenance jobs, #244 global-repo-selector (E15 UI), #245 cockpit HITL (E15).
- Deploy host `taskboard-server` republicado de `main` 2x (17:33 e 23:09/23:17); `_framework` limpo entre deploys; health checks verdes.

### Phase 0/1 — Reconciliação

- Issues #170 (E15 cockpit) e #171 (E16 master) fechadas com evidência (PR #245 + sub-Epics). **Zero issues abertas.**

### Phase 6 — SPECs não aprovados

- Scan completo: 60+ SPECs todos em status terminal (Done/Implemented/Completed/Deprecated/corrigido). Nenhum Draft pendente.

### Phase 7 — Verificação

- `dotnet build -c Release`: ✅ 0 warnings/0 errors
- `dotnet test`: ✅ 821 unit + 214 integration — **após fix**
- **Gap encontrado**: rename PR #246 quebrou 5 testes de `SelectedRepositoryServiceTests` (fixture renomeado mudou ordem alfabética do fallback; asserções esperavam `afonsoft/skills`). Corrigido via **PR #248** (merged).
- TODO/FIXME/ponytail em src/tests: nenhum
- Branches: cleanup feito — só `main` local + `origin/main` (3 remotas merged deletadas, 3 locais merged deletadas)

### Deploy

- Serviço `taskboard-server` (systemd user) ativo em `127.0.0.1:47823`, unit description atualizada, env/`~/.taskboard/agent-harness` alinhados ao rename. Endpoints novos (`/api/specs`, `/api/harness/runs`, `/api/vscode/workdir`) respondendo 401 anônimo.

**Status**: fluxo encerrado — zero issues abertas, zero SPECs pendentes, main verde, deploy atual.
