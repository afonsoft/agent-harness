# Orchestrator Sessions

## Session — 2026-09-28 (gap-analysis sweep completo)

**Scope**: Sweep pós-fechamento — 11 candidatos → 5 CONFIRMADOS (3 spec-worthy) / 6 REJEITADOS. Relatório em `.claude/memory/gap-analysis-20260928.md`.
**Decisions**: EF value comparers (3 props IReadOnlyList<string> sem comparer → warnings EF + change tracking quebrado); terminal focus/keybar sem cobertura automatizada (padrão source-guard existe); `TASKBOARD_*` fallback deferido 1 ciclo → remoção agora é breaking spec.
**Delivered**: SPECs Draft→Approved (`ef-value-comparers`, `terminal-focus-keybar-coverage`, `taskboard-env-fallback-removal`) via PR #360; Epic #356 + slices #357/#358/#359; worktree stale pruned + branch merged deletada; cruft `~/.agent-harness` limpo (exceto `skills-cache.root-stale` — root-owned, precisa sudo).
**Remaining**: Epic #356 entregue na mesma sessão — PRs #362/#363/#364 merged (`341b000`/`d505311`/`5ea8d46`), issues #356–#359 fechadas, redeploy OK. Pendente usuário: `sudo rm -rf ~/.agent-harness/data/skills-cache.root-stale`; decisão CI wiring de `check-spec-status.sh` (workflows protegidos); validação manual touch do keybar.
**Lessons**: `git worktree list` marca `prunable` quando o dir some — cruzar com `git log main..branch` antes de deletar; quarentena `*.inaccessible-*`/`*.root-stale` do skills-cache indica arquivos root-owned de provisioning antigo.

## Session — 2026-09-28 (orchestrator run — reconciliação + verificação)

**Scope**: Phase -1/0 OK (skills @`9958c42` up-to-date, git clean, gh OK, dotnet 10.0.112/node 24.16.0). Reconciliação de issues abertas, sync de status de SPECs, Phase 7 completa.
**Decisions**: #338 (E23 terminal mobile) — slices #339/#340 merged via PRs #341/#342 em 2026-09-24 → épico fechado; #318 (gap-analysis spec-drift epic) — único slice #319 closed, SPEC Done → épico fechado; SPECs terminal-focus-mode/terminal-virtual-keybar `Implemented (aguardando merge)` → `Done` via PR #354.
**Delivered**: Issues #338 e #318 fechadas (comentários pt-BR com evidência); PR #354 merged (spec status sync, `3e7626f`); PR #344 merged (dependabot test-tooling, `2e0e3d9`); `check-spec-status.sh` → "OK: no spec status drift"; build Release 0w/0e; testes 1207 unit + 296 integration verde; 9 branches locais + 8 remotas merged deletadas; re-deploy `harness-server` de main @`3e7626f` (publish c/ `_framework` limpo, health 200, `/api/meta` OK).
**Remaining**: Nenhum gap pendente. Branch `feature/agent-pipe_1a2e329b…` mantida (worktree ativo).
**Lessons**: `check-spec-status.sh` é a fonte mecânica anti-drift — rodar antes de fechar épicos de status-sync; branches squash-merged não aparecem em `git branch --merged` — cruzar com `gh pr list --head`.

## Session — 2026-09-23 (SPEC-20260923 cockpit run hardening)

**Scope**: Análise de `pipe_1a2e329b` (Completed) e `pipe_0bfefc2f` (AwaitingRetry preso) → spec aprovada → implementação end-to-end: provisioning de clone, auto-retry/rotação de CLI, realtime, aprovações com resumo, eventos duráveis, terminal PTY no run.
**Decisions**: Provisioning server-side (`IRepositoryProvisioningService`) clona para `~/repos/<repo>` e valida `origin` — bug raiz era `~/repos` ser clone de `LangGraph-UI` e o fallback criar worktree do repo errado; auto-retry persistido (5×1min/CLI, `AutoRetryCount`/`NextAutoRetryAtUtc`) com rotação e `PipelineStatus.Failed`+`FailureReason` terminal; realtime via grupo SignalR `runs` + `run_status`; `/runs/{id}/events` retorna `CockpitEventsPage` (sink durável + buffer live); `TerminalHub.OpenForRun(runId)` resolve worktree server-side; `RepositoryProvisioningFailed`→422; `steer` passou a ir ao sink durável para aparecer no replay.
**Delivered**: Spec merged na main (`f91d0ef`), implementação PR #334 merged (`a79b383`), migration `AddPipelineAutoRetryAndFailure`, docs en+pt-br, spec → Done. CI do PR todo verde (Build/Test, CodeQL, SonarCloud, GitGuardian). Unit 1121 · Integration 283.
**Remaining**: Smoke manual pendente (run com repo ainda não clonado, gate de aprovação, aba Terminal); re-deploy do `harness-server` de main @`a79b383` pendente.
**Lessons**: `~/repos` ser clone torna qualquer fallback ao root perigoso — validar `origin` sempre; factory de integração precisa fake do provisioner (senão `git clone` real); mudar endpoint de `List<>` para envelope quebra desserialização — atualizar client+testes juntos; `gh pr merge` direto funciona mesmo com checks pendentes quando proteção não bloqueia — confirmar `state=MERGED` depois.

## Session — 2026-09-22 (orchestrator run, sessão 4)

**Scope**: Reconciliação de issues abertas, diagnóstico do CI vermelho no PR #292, sync de SPECs, cleanup de branches.
**Decisions**: #282/#289 já entregues (PRs #294/#293) → fechadas; CI do #292 falhava por teste `9000` chars defasado vs `MaxLength` 16384 — fix já estava em main (PR #295), resolvido com merge `origin/main` → branch (sem rebase, evita force-push); em conflito `site.css` prevaleceu a decisão mais recente (PR #300).
**Delivered**: PR #292 merged (`0267c2d`, issue #288 fechada), PR #303 merged (`659de3e`, 3 SPECs → Done), 10 branches limpas (4 locais + 6 remotas), zero issues abertas.
**Remaining**: Redeploy do `taskboard-server` de `main` @`659de3e` pendente de confirmação; worktree antigo `~/.taskboard/worktrees/pipe_c6bc…` aguarda remoção manual.
**Lessons**: Teste com limite hard-coded quebra quando a constante sobe — derivar do `MaxLength` (já feito no #295); branch de PR aberto ficando velha acumula falhas que já foram corrigidas em main — mergear main cedo; `gh pr checks` mostra runs antigos da branch — confirmar por `mergeStateStatus`.

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


---

## Sessão 2026-09-22 (tarde) — Sidebar fix + gap-analysis + rename Harness

- **Sidebar nav** (pedido direto): `.nav` Bootstrap `flex-wrap: wrap` quebrava itens p/ coluna à direita → `nowrap` + espaçamento compacto. PR #305 merged (`11404db`), redeploy feito.
- **gap-analysis** run: 11 candidatos → 2 CONFIRMADOS + spec a pedido (harness-home-rename). Epic #306 → slices #307/#308/#309.
- **Entregas**: PR #311 (deflake PostMcpRemove — wait `/api/mcp/status` em vez de file-poll; SyncManual retry on InFlight), #312 (ratchet 73→77, baseline 77.85%), #313 (rename: `HARNESS_*` envs c/ fallback `TASKBOARD_*`, `~/.agent-harness`, `harness.sqlite` c/ migração no boot, `harness-server.service`, `install.sh --migrate`, `WithoutHarnessEnv`, `.harness-skills.json` fallback).
- **Host migrado**: `~/.taskboard` → `~/.agent-harness`; `harness-server.service` active; `/health` 200; `harness.sqlite` migrado.
- **Lição**: `sed -i` em symlink (`AGENTS.md→CLAUDE.md`) substitui o link por arquivo — usar `sed` só no alvo real.
- **Lição**: `WithoutTaskboardEnv` renomeado p/ `WithoutHarnessEnv` — scrub cobre os dois prefixos.
- Estado final: main @`2bd50ad`, 0 issues, 0 PRs, branches só `main`. Unit 1023 · Integration 263.

## Sessão 2026-09-29 — Epic #366 (bot-review dos últimos 20 PRs)

- **Origem**: análise de 75 comentários de `devin-ai-integration[bot]`, `github-advanced-security[bot]`, `sonarqubecloud[bot]` nos últimos 20 PRs → 6 SPECs Draft aprovados.
- **Entregas** (1 PR por SPEC, squash → main):
  - #367/PR #374 `ca84f7d` — PTY security: rebind por `UserKey`, allowlist de contêiner via `DockerCliDiscovery`, `@key` no pane, sessionId antes do OpenForThread, lock no scrollback, workdir coerente.
  - #368/PR #375 `cf280ed` — `docker exec` com nome do binário (não path do host), `IContainerCliDiscovery`, Chat/ACP honra `ContainerContext`, `AgentCliMap.SupportsAcp` promovido.
  - #369/PR #376 `592ce7e` — `SupportsChat`=ACP real, custom acp recusado na criação, fork preserva Transport/AgentCliId/ContainerContext.
  - #370/PR #377 `bb81bf5` — New cria thread + repo global + fecha drawer; rail refresh em drawer/expand; Retry/Run-agent condicionados; rail colapsado 52px.
  - #371/PR #378 `7af9a09` — TTL no catálogo de modelos, `SetModels` persiste (lock `_saveGate`), diálogo re-polla 12s c/ cancel, POST refresh com catch, Sonar S6444/S8970/S6667/S4487.
  - #372/PR #379 `131e14b` — cast CodeQL removido, guards estruturais (export object, media block, focus handler), `NavMenuOrderTests`, metadados de SPECs, nota de migração custom `HARNESS_DATA_DIR`.
- **Epic #366 fechado**; 0 issues abertas; `check-spec-status.sh` OK; unit 1244 · integration 296.
- **Deploy**: `harness-server` republicado de main @`7af9a09` (publish em dir novo + swap — `publish.new`→`publish`); active, `/health` 200, `/api/meta` OK, protegido 401.
- **Lições**: `dotnet format --verify` roda no CI — rodar `dotnet format` local antes do push quando mexer em blocos grandes; FakeRunner síncrono pode completar refresh inline durante asserts — usar `TaskCompletionSource` gate para deixar refresh em voo; `SelectedRepo.Selected` é o seletor global compartilhado por todas as telas.
- **Pendente do usuário**: `sudo rm -rf ~/.agent-harness/data/skills-cache.root-stale`; decisão de wire `check-spec-status.sh` no CI (workflows protegidos); validação manual touch do keybar/focus.

## Sessão 2026-09-29 (cont.) — Epic #381 (rail overlay + view-first + jobs)

- **#382/PR #385** `ai-chat-rail-overlay`: rail persistente virou overlay oculto por padrão — `History`/`New` no topo-direito, `.ai-chat-rail-wrap` fixed overlay em toda largura, `ThreadRail` sem toggle de collapse. Guard antigo (`OnToggleCollapsed`) substituído.
- **#386/PR #387** `ai-chat-view-first`: seletor View antes do Agent CLI; em `chat` CLIs sem `SupportsChat` (ACP) ficam disabled e troca automática p/ primeiro CLI compatível; `Taskboard:WebCliAgent:Enabled` exposto no catálogo runtime (editable, default false) — o erro "Web CLI Agent feature is disabled" se resolve via Settings.
- **#383/PR #388** `jobs-dashboard`: `ManagedJobService` base + `JobRegistry` singleton (defs via DI, overrides `JobSchedule` EF — migration `AddJobSchedules`, log ring 50, canal de wake), `/api/jobs` (GET/PUT/POST run, 400/404/202), tela `/jobs` (poll 10s) após Skills no NavMenu; `cli-probe-refresh` virou job de 1h; `skills-sync` RunOnce (`SyncOnStartup`→`EnabledByDefault`).
- **Lições**: `signals.ReadAsync()` já consome o item — ler `signalTask.Result` antes do drain `TryRead` (bug real pego por teste de trigger manual). Ports de persistência em Contracts retornam DTOs, nunca entidades (Contracts só vê Domain.Shared). Merge de features concorrentes em `AiChat.razor` + docs: resolver mantendo ambas as descrições.
- **Lição flake**: assert de paralelismo por `maxInFlight` em vez de wall-clock (CI mediu 1.163s vs limite 1.1s).
- **Estado**: unit 1267 · integration 302 · spec-drift OK. #385/#387 merged.

## Sessão 2026-09-30 — Sonar backlog #410 (batch 1) + crash fix #414

- **Origem**: continuação da sessão `pickle-thing` (Sonar autofix + chat UX, PR #411 merged). Branch `fix/devin-20260930-sonar-code-smells` tinha ~76 arquivos em voo (S101 naming etc.) sem commit.
- **Crash fix (#414)**: callback `Progress<AgentLogMessage>` em `AgentOrchestrationService.RunAsync` é async void e tocava `cts.Token`/`CancelAsync` após o `finally` descartar o CTS → `ObjectDisposedException` derrubava o processo (test host abort). Fix: token capturado uma vez + try/catch no callback. Teste de regressão `Dado_LogReportadoAposFimDoRun_...` (crashava antes, passa depois).
- **Entregas** (5 commits → PR #415):
  - `ff278f3` batch 1 C# — S101/S1075/S2365/S2743/S3875 + ternários/blocos (75 arquivos).
  - `9b409e6` crash fix + teste de regressão.
  - `c33c917` batch 2 — Dockerfile (S7031/S7020), shell (S7679), CSS (S4666), JS (S6582/S6653/S7747/S7758/S7765/S4138), S2325 (PathJailValidator/SecretScrubber static + DI limpo), S3398 (ToolCallRender Accumulator), S4136, S2486.
  - `1841f56` S3267 — loops→LINQ (12 arquivos).
  - `0622adc` S1192 — 55 literais repetidos → constantes (23 arquivos).
- **Validação**: build 0 warnings; unit 1341/1341; integration 307/307.
- **Restante #410**: S3776 (59 sites CRITICAL, complexidade 16–66 — refactor por método, sessão dedicada); S7637 bloqueado (workflow protegido); exclusão docs geradas (1.007 findings) precisa token admin SonarCloud OU aprovação humana p/ workflow; S8970 falso-positivo documentado.
- **Lições**: sed de literais quebra as próprias declarações de const (circular CS0110) — fazer sed primeiro, inserir consts depois; `Progress<T>` callback é async void — qualquer exceção derruba o processo, sempre guardar; `Cast<T?>().FirstOrDefault() ?? fallback` para enums em LINQ; testes de corrida com CTS: esperar live-id sumir + delay antes do Report tardio.
- **PR**: #415 aberto (não mergeado — aguarda CI/revisão).

## Sessão 2026-09-30 (cont.) — Issues #412/#413 (incidente de memória)

- **#412 PTY órfã**: `NotifyClosedAsync` agora loga fechamento (mesmo formato dos outros caminhos); `SweepLoopAsync` sobrevive a ticks ruins (catch por tick + log); `SweepIdleAsync` isola cada entrada. Testes: ambas as rotas de fechamento emitem registro (RecordingLogger no harness).
- **#413 log flood**: `Microsoft.EntityFrameworkCore.Database.Command: Warning` em appsettings.json + Production; Development mantém Information.
- Commit `636ed80` no PR #415; unit 1343/1343.
- **Lição**: caminhos de remoção "best-effort" precisam do MESMO registro de log que os caminhos explícitos — o incidente só foi diagnosticável pelo journal.
