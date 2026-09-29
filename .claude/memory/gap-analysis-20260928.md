# Gap Analysis — 2026-09-28

- Repository: `/home/ubuntu/repos/agent-harness` | Branch: `main` | Commit: `73a6c90`
- Phase reached: `done` (gate aprovado → issues → implementado → merged → deploy)
- Mode: `full` (sweep completo pós-fechamento do fluxo orchestrator)

---

## 1. Source Inventory

| Source | Status | Notes |
|---|---|---|
| `.specs/` | `present` | 132 SPECs; `check-spec-status.sh` → "OK: no spec status drift"; nenhum non-terminal |
| `docs/` | `present` | features.md/api.md cobrem features recentes (keybar, generic CLI) |
| `docs/architecture/` | `present` | architecture.{md,json,html} |
| `.claude/memory/` | `present` | 10 reports gap-analysis anteriores; último 2026-09-23 (delivered) |
| `CLAUDE.md`/`AGENTS.md`/`README.md` | `present` | |
| `.claude/rules/`, `.claude/agents/` | `present` | |
| Tests/CI | `present` | unit 1207/1207 · integration 296/296 · COVERAGE_THRESHOLD=77 (baseline 77.85%) |
| gh auth + remotes | `ok` | afonsoft/agent-harness; 0 issues abertas; 0 PRs abertos |
| submodules | none | |

## 2. AS-IS × TO-BE highlights

| Topic | AS-IS | TO-BE | Sources |
|---|---|---|---|
| Spec lifecycle | sem drift (script OK) | enforcement mecânico | `scripts/check-spec-status.sh`; SPEC-20260922-spec-status-enforcement |
| EF collection props | 3 props c/ converter, 0 comparers | comparer p/ change tracking | `PipelineConfigurations.cs:78-84`, `ProjectMemoryItemConfiguration.cs:40-42` |
| Terminal E23 | focus/keybar shipped s/ testes | cobertura mínima por convenção | PRs #341/#342; `TerminalRazorSourceGuardTests` |
| Env legacy | fallback `TASKBOARD_*` ativo | remoção após 1 ciclo | `HarnessEnv.cs:11`; SPEC-20260922 §48/§67 |
| Git worktrees | registro stale `pipe_1a2e329b` | refs limpas | `git worktree list` → "prunable" |
| Harness home | cruft de migração | home limpa | `~/.agent-harness/` stubs/baks/prev dirs |
| Pipeline runs | terminal states corretos | — | `harness.sqlite` `pipe_0bfefc2f` → Failed (auto-retry OK) |

## 3. Candidates e Verdicts

| Key | Categoria | Veredito | Prioridade | SPEC/Issue |
|---|---|---|---|---|
| `GAP-implementation-ef-value-comparers` | implementation | **CONFIRMADO** | impact low-med · effort S | `.specs/SPEC-20260928-ef-value-comparers.md` (Draft) |
| `GAP-tests-terminal-focus-keybar-coverage` | tests | **CONFIRMADO** | impact low-med · effort S | `.specs/SPEC-20260928-terminal-focus-keybar-coverage.md` (Draft) |
| `GAP-requirements-taskboard-env-fallback-removal` | requirements | **CONFIRMADO** | low · effort M · breaking | `.specs/SPEC-20260928-taskboard-env-fallback-removal.md` (Draft) |
| `GAP-operation-stale-worktree-registration` | operation | **CONFIRMADO** | trivial | housekeeping (sem spec): `git worktree prune` + delete branch merged `feature/agent-pipe_*` |
| `GAP-operation-harness-home-cruft` | operation | **CONFIRMADO** | trivial | housekeeping: `~/.agent-harness` stubs (`taskboard.db`/`taskboard.sqlite` 0B), `*.bak-*`, `publish.prev`, `publish-mcp.prev`/`publish-mcp.new`, `skills-cache.inaccessible-*`, nested `agent-harness/` |
| Spec status drift | automation | REJEITADO | — | `check-spec-status.sh` OK; CI wiring deferido pelo próprio spec (workflows protegidos) — **decisão CI continua aberta** |
| SonarCloud project key | operation | REJEITADO | — | resolvido: `code-quality.yml:27` = `afonsoft_agent-harness` |
| `pipe_0bfefc2f` AwaitingRetry preso | operation | REJEITADO | — | DB: `Failed` terminal pós 5 attempts — fix 09-23 funcionou |
| `~/.taskboard/worktrees` legado | operation | REJEITADO | — | dir não existe mais (migrado/limpo) |
| Secrets no repo | security | REJEITADO | — | apenas fixtures em `SecretScrubberTests.cs` |
| followups.md itens abertos | documentation | REJEITADO | — | tracked por design; nota: checkbox "Commit+push" (sessão 08-31) provavelmente stale |
| Coverage ratchet bump | automation | REJEITADO | — | gate 77 vs baseline 77.85% — sem drift medido; próximo bump é tarefa de sprint, não gap |

## 4. Evidências (detalhe)

- EF comparers: `grep -rn "ValueComparer" src/` → 0; converter `IReadOnlyList<string>` em `ValueConverters/ReadOnlyListStringJsonValueConverter.cs:7`; warnings registrados em `orchestrator_stats.md` (E7/E12).
- Focus/keybar: `Terminal.razor:87` (`.terminal-keybar`), `:377/:502` (`taskboard.setTerminalFocus`), `:437` (`taskboardTerminal.pasteClipboard`); JS em `src/Taskboard.Client/wwwroot/js/terminal.js`; 0 hits em `tests/`.
- Fallback: `HarnessEnv.cs:5-15` (`LegacyPrefix="TASKBOARD_"`); SPEC-20260922-harness-home-rename §48 ("fallback removido num ciclo futuro")/§67 ("com spec própria").
- Worktree: `git worktree list` → `pipe_1a2e329b0b814bd5b01e3c3cf71002cc prunable` (dir inexistente); `git log main..feature/agent-pipe_*` → 0 commits à frente (todo o conteúdo já está em main).
- Cruft: `ls ~/.agent-harness` → `taskboard.db`/`taskboard.sqlite` (0B), `*.bak-*`, `publish.prev`, `publish-mcp.prev`, `publish-mcp.new`, `skills-cache.inaccessible-*`, `skills-cache.root-stale`, `agent-harness/` nested.

## 5. Draft SPECs gerados (aguardando gate)

- `.specs/SPEC-20260928-ef-value-comparers.md`
- `.specs/SPEC-20260928-terminal-focus-keybar-coverage.md`
- `.specs/SPEC-20260928-taskboard-env-fallback-removal.md`

## 5. Issues

- Epic: gap-analysis-20260928 → #356 (closed)
- Slices: ef-value-comparers → #357 (closed, PR #362 `341b000`) · terminal-focus-keybar-coverage → #358 (closed, PR #363 `d505311`) · taskboard-env-fallback-removal → #359 (closed, PR #364 `5ea8d46`)

## 6. Approval gate

- Decision: `approved` (todos os 3 SPECs + housekeeping) | By: user | Date: 2026-09-28

## 6.1 Orchestrator handoff

- Executado na mesma sessão (Phase 4 sequential): 3 branches → 3 PRs → CI verde → squash merge → issues fechadas por `Closes`.
- Verificação final: `dotnet build` 0w/0e · unit 1212 · integration 296 · shellcheck OK · format clean · redeploy `harness-server` @5ea8d46 (health 200, API 401 anônimo).
- Housekeeping: worktree stale pruned; branch `feature/agent-pipe_*` deletada (merged); cruft `~/.agent-harness` limpo; `skills-cache.root-stale` pendente (root-owned, precisa sudo).

## 7. Pendencies

- Housekeeping sem spec aguardando OK: prune do worktree stale + delete branch merged; cleanup de `~/.agent-harness` cruft.
- Decisão aberta (não-gap): wiring de `check-spec-status.sh` em CI — exige aprovação humana para editar `.github/workflows/**`.
- Validação manual touch do focus/keybar continua pendente (sem harness de browser no repo).
- followups.md: ADR Spectre.Console.Cli e UX-diff review ainda abertos (não bloqueantes).
