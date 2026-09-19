# Gap Analysis — 2026-09-19

Run: auditoria pós-sprint 0918/0919 (mcp-v2-http-transport, action-button-separation,
restore-board-page) + drift acumulado docs↔código. Branch `main` @ `94bff47`,
working tree limpo, em sync com `origin/main`, `gh` autenticado (afonsoft).
Read-only até o gate — só SPECs Draft + este report foram escritos.

## 1. Source Inventory

| Fonte | Estado |
|---|---|
| `.specs/SPEC-*.md` (74) | present |
| `docs/` (+ pt-br mirrors, `architecture/`) | present |
| `.claude/CONTEXT.md`, `.claude/memory/` (4 runs anteriores) | present |
| `CLAUDE.md`, `AGENTS.md`, `README.md`, `.claude/rules/`, `.claude/agents/` | present |
| `src/` (15 projetos) + `tests/` (Unit + Integration) | present |
| CI `dotnet.yml` / `code-quality.yml` / `codeql.yml` | present |
| Git history, Issues (só Epic #126 aberta), PRs (nenhum aberto) | present |
| `ORCHESTRATOR-ROADMAP.md` | absent |

Build: `dotnet build Taskboard.sln` verde, 0 warnings. Testes: **445 unit + 156
integration, 0 falhas**.

## 2. Candidates & Verdicts

| Key | Categoria | Veredito | Prioridade | Evidência |
|---|---|---|---|---|
| GAP-architecture-legacy-workflow-surface | architecture | **CONFIRMADO** | medium | TO-BE: SPEC-20260918-workflow-github-actions §Out-of-scope L36 ("entidade sai quando nada mais usar — avaliar no projects-removal"). AS-IS: `WorkflowWorkspace` entity + tabela (ModelSnapshot.cs:258-273) + endpoints `device-workspaces`/`workflow-capabilities` (Program.cs:662-700) + `WorkflowCapabilityService` + client methods — zero callers em Blazor/CLI/MCP. `src/Taskboard.Workflow/` é projeto vazio no sln (L34), sem `.cs`, sem referências. |
| GAP-documentation-docs-stack-drift | documentation | **CONFIRMADO** | medium | TO-BE: código real = Spectre.Console.Cli (`Taskboard.Cli.csproj:11-12`), Blazor WASM, GitHub-backed board, `/workflow` = GH Actions monitor. AS-IS docs: `CLAUDE.md:26`, `technologies.md:11`, `packages.md:13`, `architecture.md:13` citam `System.CommandLine`; `architecture.md` lista agregados `Project`/`Task` (dropados) + "SPA frontend" + motor de workflow legado; `features.md:18` "Workflow workspaces"; `features.md` MCP "4 tools" vs 5 `[McpServerTool]` reais. |
| GAP-documentation-stale-spec-status | documentation | **CONFIRMADO** | low | TO-BE: SPECs entregues = `Done`. AS-IS: `SPEC-20260911-refine-github-actions` (entregue via #52-56: actions v6/v7 + `permissions` ok), `SPEC-20260918-sidebar-icon-rail` (#125, validado em prod), `SPEC-20260918-action-button-separation` (#135, slice #134 closed), `SPEC-20260918-cli-agents-expansion` (#94, código+testes presentes) — todos `Approved`. `cli-migration.md` "em revisão" mas mergeada (dup `context:current` já removida — ocorrência única Program.cs:16). `followups.md` com itens feitos não checados. **2ª recorrência** (SPEC-20260914-stale-spec-status foi o 1º lote, sem prevenção). |
| GAP-tests-cli-coverage | tests | **CONFIRMADO** | medium | TO-BE: SPEC-003 §18 (testes CLI), followups.md (CommandAppTester, smoke `--help`/`context:current`, guard de `CommandArgument`), hard rule de testes. AS-IS: zero `.cs` de teste referencia `Taskboard.Cli`/`taskctl`/`CommandAppTester`. |
| GAP-requirements-coverage-threshold | requirements | **INCONCLUSIVO** | — | Contradição: CLAUDE.md:51 documenta gate `COVERAGE_THRESHOLD=45` ("meta 80%") enquanto Hard Rule 6 (`CLAUDE.md:101`) e `global-rules.md` exigem ≥80% ("meta 90%"). Recorrência do INCONCLUSIVO `coverage-80-new-code` de 0917. Decisão do usuário: subir o gate gradualmente ou corrigir a hard rule. |
| GAP-tests-root-page-regression | tests | REJEITADO | — | A regressão `/` (restaurada em #139) já tem cobertura: `ServerEndpointsTests.cs:47` + `WasmHostingTests.cs:24` fazem `GetAsync("/")`. |
| GAP-security-mcp-anonymous | security | REJEITADO | — | `api.MapMcp("mcp")` (Program.cs:425) herda `RequireAuthorization` do grupo `/api` (cookie ou X-Api-Key); `McpHttpEndpointTests.cs` cobre. |
| GAP-documentation-api-local-prefix | documentation | REJEITADO | — | `/api/local/ai/threads/{id}/events` existe de verdade (Program.cs:567,600,608) — `features.md` correto. |
| GAP-implementation-jira-missing | implementation | REJEITADO | — | `JiraService`/`IJiraService` + DTOs presentes; SPEC-010 `Implemented`. |
| GAP-implementation-context-current-dup | implementation | REJEITADO | — | followups.md pedia remover duplicata; `context:current` tem ocorrência única (Program.cs:16). Já resolvido — falta só o `[x]` (coberto pelo SPEC stale-status). |

## 3. Pendências administrativas (sem SPEC — ação direta no gate)

- **Epic #126** aberta com todos os slices fechados e o INCONCLUSIVO resolvido
  (action-button-separation #134/#135) → fechar.
- **~40 remote branches** já mergeadas (`origin/feature/*`, `origin/fix/*`) +
  local `feature/devin-20260918-action-button-separation` → cleanup.
- `orchestrator_stats.md` aponta sessão de 09-11 (`feat/install-systemd-skill`) —
  estado velho; regenerar na próxima sessão de orquestração.

## 4. SPECs Draft gerados

- `.specs/SPEC-20260919-legacy-workflow-surface.md` → GAP-architecture-legacy-workflow-surface
- `.specs/SPEC-20260919-docs-stack-drift.md` → GAP-documentation-docs-stack-drift
- `.specs/SPEC-20260919-stale-spec-status.md` → GAP-documentation-stale-spec-status
- `.specs/SPEC-20260919-cli-test-coverage.md` → GAP-tests-cli-coverage

## 5. Issues

Gate aprovado pelo usuário (2026-09-19: "Sim, aprovar tudo"; cobertura: "subir o
gate gradualmente"; cleanup: "Sim, executar ambos").

- **Epic #140** — gap-analysis-20260919
- **#141** — legacy-workflow-surface → SPEC-20260919-legacy-workflow-surface
- **#142** — docs-stack-drift → SPEC-20260919-docs-stack-drift
- **#143** — stale-spec-status → SPEC-20260919-stale-spec-status
- **#144** — cli-test-coverage → SPEC-20260919-cli-test-coverage
- **#145** — coverage-gate-ratchet → SPEC-20260919-coverage-gate-ratchet
  (SPEC criada após a medição: baseline real **66.26%**, ratchet 45→65→…→80)

## 6. Resultado da execução

| Slice | PR | Estado |
|---|---|---|
| S1 legacy-workflow-surface | #147 | **MERGED** — migration drop `WorkflowWorkspaces`, endpoints/service/projeto vazio removidos, docs `/workflow` = GH Actions monitor. 601 testes verdes. |
| S4 cli-test-coverage | #148 | **MERGED** — `Program.ConfigureCommands` extraído, `Spectre.Console.Cli.Testing` + `InternalsVisibleTo`, `CliSmokeTests` (15 testes: help raiz/por comando, `context:current` offline, guard de `CommandArgument` por reflection, `SplitRepo`). RED demonstrado. 460 unit verdes. |
| S2+S3 docs + spec-status | #149 | **MERGED** — `System.CommandLine`→`Spectre.Console.Cli` em CLAUDE/technologies/packages(+pt-br)/architecture(.md/.json/.html); agregados `Project`/`Task` removidos da doc; 5 SPECs → `Done`; `cli-migration.md` concluído; convenção anti-regressão em `global-rules.md`. |
| S5 coverage-gate-ratchet | #150 | CI em andamento — `COVERAGE_THRESHOLD` 45→65, política ratchet única em CLAUDE.md/global-rules.md. Workflow edit sinalizado para revisão humana. |

## 7. Pendências

- ~~INCONCLUSIVO `GAP-requirements-coverage-threshold`~~ — resolvido: usuário
  escolheu ratchet gradual (SPEC-20260919-coverage-gate-ratchet, #145).
- ~~Epic #126~~ — fechada. ~~46 branches mergeadas~~ — deletadas (só a branch
  ativa permanece).
- Próximos degraus do ratchet: 70 → 75 → 80 conforme novas specs adicionarem
  testes (registrar nova medição a cada degrau).
- `orchestrator_stats.md` continua datado de 09-11 — regenerar na próxima
  sessão de orquestração.
