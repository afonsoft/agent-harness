# SPEC-20260920-global-repo-selector

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `global-repo-selector` |
| Type | `Frontend / Feature` |
| Stack | `.NET 10 / Blazor WebAssembly / Blazor.Bootstrap / SignalR / code-server` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260920-global-repo-selector` |
| Ticket | [#236 (E15)](https://github.com/afonsoft/agent-harness/issues/236) — slices #237–#243 |
| Status | `Done` — entregue via [PR #244](https://github.com/afonsoft/agent-harness/pull/244) |

## 1. User Story

**As a** Harness user,
**I want** a single repository selector at the top of the sidebar — above the Board item — shared by every repo-aware screen (Board, Gantt, Workflow, Specs, VS Code, Terminal),
**So that** I pick the repository once and every screen works on it: the board shows its issues, the Gantt its timeline, Workflow its Actions runs, Specs its `.specs/` catalog, the terminal opens in `~/repos/<repo>` and VS Code opens its workspace — instead of re-selecting the repo on each page.

**Problem context:**

- `RepositoryCombobox` is duplicated per page: `BoardView.razor`, `Gantt.razor` and `Workflow.razor` each fetch `IGitHubService.GetRepositoriesAsync()` and keep a private `_selectedRepo` — selection never survives navigation.
- `/specs` reads `.specs/` from a single server-side dir (`Taskboard:SpecsDir` or the app's own `.specs`) — no notion of "specs of repo X".
- The terminal PTY always spawns in `homeDir` (`PtySessionFactory(homeDir)`); `TerminalHub.Open()` accepts no cwd.
- `/editor` only opens the selected repo when reached via `?repo=`; the bare menu entry falls back to `HomeDirectory`.
- `ICodeServerManager` exposes only `GetStatusAsync`/`EnsureStartedAsync` — when the code-server child dies or hangs there is no user-facing way to restart it (today requires a server restart).

## 2. Scope

**In scope:**

- New client-side `SelectedRepositoryService` (Blazor WASM, scoped DI): loads repos via `IGitHubService`, holds the current `owner/repo`, persists to `localStorage["harness.selectedRepo"]`, raises a `Changed` event.
- Sidebar: `RepositoryCombobox` rendered above the first nav item (above Board); invalid input reverts with the existing warning strings; disabled + existing token warning when `GITHUB_TOKEN` is missing.
- Sidebar rail mode (collapsed): the selector collapses to a repo icon with a tooltip showing the current repo; clicking it expands the sidebar.
- Menu reorganization: repo-scoped items on top — Board, Gantt, Workflow, Specs, VS Code, Terminal — then a visual `<hr>` divider, then the rest — AI Chat, CLI Agents, FinOps, Settings, Skills, Prompts.
- Board/Gantt/Workflow: per-page combobox and per-page repo fetch removed; pages consume the shared service and reload on `Changed`.
- Specs: `GET /api/specs*` accepts `?repo=owner/name` resolving `~/repos/<name>/.specs` via `WorkspaceService`; missing clone/dir → empty list; absent param → current default behavior.
- Terminal: `Open(string? repo)` on the hub resolves the repo workdir server-side (`~/repos/<name>` when cloned, else `~/repos`, else `~` when no repo); only new tabs get the cwd.
- VS Code: `/editor` without `?repo`/`?path` uses the globally selected repo's workdir; changing the selection while on the page reloads the iframe to the new workdir.
- VS Code restart: `POST /api/vscode/restart` → `ICodeServerManager.RestartAsync` (kill → spawn → wait-listening); "Restart" button on `/editor` with spinner, status refresh and iframe reload.
- Tests + bilingual docs.

**Out of scope:**

- Cross-device persistence of the selection (server-side per-user config) — `localStorage` only.
- Cloning repos from the UI (missing clone is an empty-state/hint, not a "Clone" button).
- Auto-restart/health-watchdog for code-server — restart is user-triggered only.
- Repo pickers inside dialogs (`RunSpecDialog`, `AgentSelectionModal`, New Task) — untouched.
- `/github-board` page and `RepositorySelector` component — untouched.
- Changes to `IGitHubService`, YARP config, `.github/workflows/**`.

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Blazor` — new `Services/SelectedRepositoryService.cs`; `Layout/NavMenu.razor` (combo + reorder + divider + rail icon); `BoardView.razor`, `Gantt.razor`, `Workflow.razor` (consume service, drop per-page combo); `Specs.razor` (pass `repo`); `Terminal.razor` (send repo on `Open`); `VscodeEditor.razor` (default folder + Restart button); `Services/TaskboardClient.cs` (new params/endpoints).
- `Taskboard.Client` — `Program.cs` DI registration; `wwwroot/js/taskboard.js` (localStorage helpers, iframe reload helper).
- `Taskboard.Server` — `Program.cs` specs endpoints `?repo=`; `POST /api/vscode/restart`; `Hubs/TerminalHub.cs` `Open(repo)`.
- `Taskboard.Application` — `SpecAppService` resolves the specs dir **per request** instead of caching `_specsDir` at construction.
- `Taskboard.Integrations` — `Terminal/TerminalSessionManager.OpenAsync` + `PtySessionFactory.Create(cwd)`/`PtySession` working directory; `Vscode/CodeServerProcessManager.RestartAsync` + `ICodeServerManager` contract.

**Files to read before implementing:**

- `AGENTS.md` · `.claude/rules/global-rules.md`
- `src/Taskboard.Blazor/Layout/NavMenu.razor` · `Layout/MainLayout.razor`
- `src/Taskboard.Blazor/Components/Shared/RepositoryCombobox.razor`
- `src/Taskboard.Blazor/Components/BoardView.razor` · `Pages/Gantt.razor` · `Pages/Workflow.razor` · `Pages/Specs.razor` · `Pages/Terminal.razor` · `Pages/VscodeEditor.razor`
- `src/Taskboard.Blazor/Services/TaskboardClient.cs` · `RepositoryFilter.cs`
- `src/Taskboard.Client/Program.cs` · `wwwroot/js/taskboard.js` · `wwwroot/css/site.css`
- `src/Taskboard.Server/Program.cs` (specs + vscode endpoints, DI) · `Hubs/TerminalHub.cs`
- `src/Taskboard.Application/Specs/SpecAppService.cs` (per-request dir)
- `src/Taskboard.Integrations/Workspace/WorkspaceService.cs` (`ResolveCardWorkdir`)
- `src/Taskboard.Integrations/Terminal/{TerminalSessionManager,PtySessionFactory,PtySession,IPtySession}.cs`
- `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs`
- `src/Taskboard.Application.Contracts/Vscode/VscodeContracts.cs` (`ICodeServerManager`)
- `.specs/SPEC-20260917-vscode-web-workspace.md` · `SPEC-20260917-terminal-tabs.md` · `SPEC-20260918-sidebar-icon-rail.md` · `SPEC-20260915-repo-search-combobox.md`

**Files to create or modify:**

```text
src/Taskboard.Blazor/Services/SelectedRepositoryService.cs        # NEW
src/Taskboard.Blazor/Layout/NavMenu.razor                         # MOD — combo + reorder + divider + rail icon
src/Taskboard.Blazor/Components/BoardView.razor                   # MOD — consume service
src/Taskboard.Blazor/Components/Pages/Gantt.razor                 # MOD — consume service
src/Taskboard.Blazor/Components/Pages/Workflow.razor              # MOD — consume service
src/Taskboard.Blazor/Components/Pages/Specs.razor                 # MOD — repo param
src/Taskboard.Blazor/Components/Pages/Terminal.razor              # MOD — Open(repo)
src/Taskboard.Blazor/Components/Pages/VscodeEditor.razor          # MOD — default folder + Restart
src/Taskboard.Blazor/Services/TaskboardClient.cs                  # MOD — specs repo param + vscode restart
src/Taskboard.Client/Program.cs                                   # MOD — DI
src/Taskboard.Client/wwwroot/js/taskboard.js                      # MOD — localStorage + iframe helpers
src/Taskboard.Client/wwwroot/css/site.css                         # MOD — sidebar selector + divider + rail icon
src/Taskboard.Server/Program.cs                                   # MOD — specs ?repo= + vscode/restart
src/Taskboard.Server/Hubs/TerminalHub.cs                          # MOD — Open(repo)
src/Taskboard.Application/Specs/SpecAppService.cs                 # MOD — per-request dir
src/Taskboard.Application.Contracts/Specs/ISpecAppService.cs      # MOD — repo param
src/Taskboard.Application.Contracts/Vscode/VscodeContracts.cs     # MOD — RestartAsync
src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs     # MOD — RestartAsync
src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs     # MOD — workdir param
src/Taskboard.Integrations/Terminal/PtySessionFactory.cs          # MOD — cwd param
src/Taskboard.Integrations/Terminal/PtySession.cs                 # MOD — cwd param
tests/...                                                         # unit + integration tests
docs/features.md · features.pt-br.md · api.md · api.pt-br.md      # MOD
.specs/SPEC-20260920-global-repo-selector.md                      # NEW (this file)
```

## 4. Requirements

### RF-001: `SelectedRepositoryService` (client state)

- **Description:** Scoped service in `Taskboard.Blazor.Services` registered in `Taskboard.Client/Program.cs`. Responsibilities: `Task EnsureLoadedAsync()` (fetch repos via `IGitHubService`, sort by `FullName`, pick initial selection), `IReadOnlyList<string> Repositories`, `string? Selected`, `bool TokenMissing`, `string? Warning`, `event Func<Task>? Changed`, `Task SelectAsync(string value)` (validates `owner/repo`, persists, raises `Changed` only on effective change).
- **Rules:** initial selection = persisted `localStorage["harness.selectedRepo"]` when it is a valid `owner/repo` present in the loaded list (a persisted value absent from the list is still honored — free-text parity with the combobox); otherwise first repo alphabetically; `localStorage` unavailable (private mode/JS interop failure) → in-memory only, no exception escapes; repo-list fetch failure → `Warning` set, free-text selection still allowed; `TokenMissing` → selector disabled.
- **Input → Output:** persisted value + GitHub list → `Selected`; `SelectAsync("owner/repo")` → persisted + `Changed` fired.

### RF-002: Sidebar selector

- **Description:** `NavMenu.razor` renders a `RepositoryCombobox` (existing shared component) inside the sidebar, visually above the first `NavLink` (Board), bound to `SelectedRepositoryService`. Commit → `SelectAsync`; invalid `owner/repo` → the service rejects, the combobox restores the previous value and the service's `Warning` renders under the input (same strings as today's per-page warnings).
- **Rules:** the repo list is fetched once per app lifetime by the service — no per-page refetch; selector `Disabled` when `TokenMissing`.
- **Input → Output:** user commit → global `Selected` updated; every subscribed page reloads.

### RF-003: Menu regrouping + divider + rail mode

- **Description:** Nav order becomes: *(selector)* Board · Gantt · Workflow · Specs · VS Code · Terminal, then an `<hr class="app-sidebar-divider">` separator, then AI Chat · CLI Agents · FinOps · Settings · Skills · Prompts. In the collapsed icon rail (`html[data-sidebar-collapsed]`, ≥992px) the selector input is replaced by a repo icon button (`IconName.Folder2`) with a CSS tooltip/`title` showing the current `owner/repo`; clicking it expands the sidebar (clears the collapsed flag via the existing `taskboard.setSidebarCollapsed(false)` path).
- **Rules:** divider hidden in rail mode is acceptable but preferred visible as a thin rule; divider must not be a `NavLink`; mobile offcanvas keeps identical behavior (selector usable, items close the drawer on navigate).
- **Input → Output:** DOM order + conditional rail rendering.

### RF-004: Board / Gantt / Workflow consume the service

- **Description:** Each page drops its own `_repositories`/`_repoNames`/`_repoInput`/`OnRepoCommitted`/`GetRepositoriesAsync` block and the `RepositoryCombobox` markup. On init: `await service.EnsureLoadedAsync()` then load data for `service.Selected`; subscribe to `service.Changed` → reload for the new repo; unsubscribe in `Dispose`/`DisposeAsync`.
- **Rules:** `TokenMissing` renders the same warning as today; `Warning` from the service surfaces identically; Board keeps its priority legend; Workflow keeps its auto-refresh timer; an invalid/removed repo surfaces the page's normal load error — no silent fallback.
- **Input → Output:** `service.Selected` → `KanbanBoard RepositoryFullName` / timeline+metrics / workflows.

### RF-005: Specs catalog per repository

- **Description:** `ISpecAppService` methods gain an optional `repo` parameter (`ListAsync`, `GetAsync`, `UpdateStatusAsync`); `SpecDriftDetector.BuildReportAsync` likewise. Resolution order for the specs dir: `repo` present → `WorkspaceService.ResolveCardWorkdir(repo)` + `/.specs` (only when the clone **and** the `.specs` dir exist — otherwise the catalog is empty); `repo` absent → current `Taskboard:SpecsDir`/app-base walk-up. `SpecAppService` stops caching `_specsDir`; resolves per call (still tolerant to a missing dir → empty list).
- **Rules:** `repo` is validated as `owner/repo` server-side (400 on malformed); path confinement identical to `vscode/workdir` — never outside `WorkspaceRoot`; the hourly `SpecDriftReportCache` keeps covering the **default** dir only; a `repo` param produces an on-demand drift scan (no cache); `Specs.razor` passes `service.Selected`, shows the repo's specs, and renders the existing `EmptyState` ("repository not cloned or no `.specs/`") when empty; `SpecDetailDialog`/`RunSpecDialog` receive the repo context so status updates and runs hit the same dir.
- **Input → Output:** `GET /api/specs?repo=afonsoft/x` → specs parsed from `~/repos/x/.specs`.

### RF-006: Terminal opens in the selected repo workdir

- **Description:** `TerminalHub.Open(string? repo = null)` → `TerminalSessionManager.OpenAsync(userKey, connectionId, workdir, onOutput, onClosed)`; `TerminalSessionManager` receives the resolved workdir from the hub (hub injects `WorkspaceService` and calls `ResolveCardWorkdir(repo)` — `~/repos/<name>` when the clone exists, else `~/repos`, and `~`/current `homeDir` when `repo` is null); `PtySessionFactory.Create(string? workingDirectory)` passes it to `PtySession`, which uses it as `ProcessStartInfo.WorkingDirectory` (and keeps `HOME` env unchanged).
- **Rules:** `repo` malformed or escaping the root → `WorkspaceService` fallback (same as `ResolveCardWorkdir` today) — never an arbitrary client path; only **new** tabs get the repo cwd — live sessions and `Reattach` keep their original cwd; `?cmd=` flow unchanged; `Terminal.razor` sends `service.Selected` on `Open`; no `repo` → identical behavior to today.
- **Input → Output:** `Open("afonsoft/x")` → bash PTY with cwd `~/repos/x`.

### RF-007: `/editor` defaults to the selected repo workdir

- **Description:** Folder resolution order in `VscodeEditor.razor`: `?path=` → `?repo=` → `service.Selected` (resolved via existing `GET /api/vscode/workdir?repo=`) → `HomeDirectory`. The page subscribes to `service.Changed`; on change (only when no explicit `?repo`/`?path` was supplied) it re-resolves the folder and reloads the iframe (`taskboard.js` helper or a cache-busting query arg).
- **Rules:** explicit query params always win over the global selection; folder shown in the toolbar reflects the effective path; workdir resolution stays server-side (no path leaks to the client).
- **Input → Output:** `/editor` with `afonsoft/x` selected → iframe `/vscode/?folder=~/repos/x`.

### RF-008: VS Code service restart

- **Description:** `ICodeServerManager.RestartAsync(CancellationToken)` → `CodeServerProcessManager`: if running, `Kill(entireProcessTree)` + await exit (bounded ~10s); then `EnsureStartedAsync` path (spawn + `WaitForListeningAsync`); returns post-attempt `VscodeStatus`. Endpoint `POST /api/vscode/restart` (auth'd like the other `/api/vscode/*`) → `200` with status; `404` when not installed; `503` when the process fails to start/listen (generic error, no stack). Concurrency: a lock serializes restart vs. `EnsureStarted` — a concurrent caller gets the same in-flight result, never a double spawn.
- **UI:** `/editor` shows a **Restart** button (`IconName.ArrowClockwise`, outline secondary, small) in the header row whenever `Installed` — next to the running/stopped badge. While restarting: disabled + spinner + "Restarting…"; on completion: status re-fetched and the iframe reloads. Failure → danger alert "Restart failed — check server logs" and the button re-enables.
- **Input → Output:** POST → `{ installed, running, port, ... }`; UI click → restarted code-server + fresh iframe.

### RF-009: Documentation

- **Description:** `docs/features(.pt-br).md` document the global selector, menu groups, per-repo specs, terminal cwd and the VS Code restart; `docs/api(.pt-br).md` document `?repo=` on `/api/specs*`, `Open(repo)` on the hub and `POST /api/vscode/restart`.

**Business rules / invariants:**

- The client only ever transmits `owner/repo` — never a filesystem path; every path resolution happens server-side and stays confined to `WorkspaceRoot`/`$HOME` (same invariants as `vscode/workdir`).
- One repo list fetch per app session (the service); no page refetches `GetRepositoriesAsync`.
- Changing the selection never kills running processes — existing PTYs and the code-server child keep running; only *new* sessions/iframes use the new repo.
- No `localStorage` data beyond the `owner/repo` string — no tokens, no user data.

## 5. API Contract

```http
GET  /api/specs?status=&q=&repo=owner/name      → 200 LivingSpecDto[] (repo-scoped) | 400 malformed repo
GET  /api/specs/{id}?repo=owner/name            → 200 LivingSpecDetailDto | 404
POST /api/specs/{id}/status?repo=owner/name     → 200 LivingSpecDetailDto | 400 | 404
GET  /api/specs/drift-report?repo=owner/name    → 200 SpecDriftReportDto (on-demand for non-default repo)
POST /api/vscode/restart                        → 200 VscodeStatus | 404 not installed | 503 failed to start
```

**Hub (`/terminal-hub`):**

| Method | Args | Return |
| --- | --- | --- |
| `Open` | `repo?: string` (new optional arg; callers without it get today's `~` cwd) | `string sessionId` |
| `Input` / `Resize` / `Close` / `Reattach` | unchanged | unchanged |

**Client-side storage:**

```text
localStorage["harness.selectedRepo"] = "owner/repo"
```

**Expected errors:** `400` malformed `repo` on `/api/specs*`; `404` not installed on `vscode/restart`; `503` code-server failed to start/listen; `HubException` unchanged for terminal cap/disabled.

## 6. Acceptance Criteria

- [ ] **Dado** a app carregada com `GITHUB_TOKEN` configurado **quando** a sidebar renderiza **então** o combobox aparece acima do item Board, os itens repo-scoped (Board, Gantt, Workflow, Specs, VS Code, Terminal) vêm antes do divisor `---` e os demais depois.
- [ ] **Dado** `afonsoft/x` selecionado no combo **quando** navego para Gantt e depois Workflow **então** ambos carregam dados de `afonsoft/x` sem nenhum combo na página.
- [ ] **Dado** `afonsoft/x` selecionado **quando** recarrego a página (F5) **então** a seleção persiste via `localStorage` e Board abre já em `afonsoft/x`.
- [ ] **Dado** texto inválido `just-a-name` **quando** commitado no combo da sidebar **então** o warning existente aparece e o valor anterior é restaurado — a seleção global não muda.
- [ ] **Dado** `~/repos/x/.specs` existente **quando** abro `/specs` com `afonsoft/x` selecionado **então** a tabela lista as specs daquele clone; **e quando** `afonsoft/y` não está clonado **então** o empty state aparece.
- [ ] **Dado** `afonsoft/x` selecionado **quando** abro uma nova aba na `/terminal` **então** `pwd` no PTY imprime `~/repos/x`; **e quando** `afonsoft/y` não está clonado **então** a aba abre em `~/repos`.
- [ ] **Dado** uma aba de terminal viva **quando** troco o repo no combo **então** a aba existente mantém seu cwd — só abas novas usam o novo repo.
- [ ] **Dado** `afonsoft/x` selecionado **quando** abro `/editor` pelo menu **então** o iframe carrega `~/repos/x`; **e quando** navego com `?repo=afonsoft/z` explícito **então** `afonsoft/z` prevalece sobre a seleção global.
- [ ] **Dado** code-server parado/morto (`running=false`) **quando** clico **Restart** na `/editor** **então** `POST /api/vscode/restart` dispara, o botão mostra spinner, e ao concluir `running=true` e o iframe recarrega.
- [ ] **Dado** a sidebar colapsada em rail (≥992px) **quando** clico no ícone de repo **então** a sidebar expande mostrando o combobox com a seleção atual.
- [ ] **Dado** `GITHUB_TOKEN` ausente **quando** a app abre **então** o combo fica desabilitado com o warning existente e todas as telas repo-scoped mostram seus estados de token ausente.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `localStorage` indisponível | private mode | seleção funciona na sessão, não persiste; sem exceção |
| Valor persistido removido do GitHub | `localStorage` com repo apagado | valor ainda commitado (free-text parity); páginas mostram erro de load normal |
| `repo` com `..` ou formato inválido | `?repo=../x` em specs/terminal | 400 (specs) ou fallback root (terminal) — nunca path fora do root |
| Restart durante `EnsureStarted` em voo | cliques duplos | uma única reinicialização; callers recebem o mesmo resultado |
| code-server não instalado | POST restart | `404`; UI mostra o empty state de install, não o botão |
| Repo trocado com `/editor` aberto via `?repo=` | selection change | iframe **não** recarrega (param explícito vence) |
| Specs `RunSpecDialog` com repo | run | o run usa o workdir do repo selecionado |

## 7. Task Plan (agent execution)

- [ ] **T1 — Service:** `SelectedRepositoryService` + `taskboard.js` localStorage helpers + DI; unit tests da lógica (seleção inicial, persistência, validação, `Changed`).
- [ ] **T2 — Sidebar:** `NavMenu`/`MainLayout` — combo acima de Board, nova ordem, `<hr>` divider, rail icon → expand; CSS em `site.css`.
- [ ] **T3 — Páginas repo-scoped:** Board/Gantt/Workflow consomem o service (subscribe/dispose); remoção dos combos e fetches duplicados.
- [ ] **T4 — Specs por repo:** `?repo=` nos 4 endpoints + `SpecAppService`/`SpecDriftDetector` com dir por request + página + dialogs passando o repo; testes (workdir confinamento, clone ausente → vazio, status update no clone).
- [ ] **T5 — Terminal cwd:** `Open(repo)` no hub + `TerminalSessionManager`/`PtySessionFactory`/`PtySession` workdir + página enviando a seleção; testes do manager (cwd repassado, fallback).
- [ ] **T6 — VS Code:** `RestartAsync` no manager + endpoint + `/editor` default folder por seleção + botão Restart + reload do iframe; testes do manager (kill→spawn→listen, concorrência, 404/503).
- [ ] **T7 — Validação:** `dotnet build -c Release` (warnings=errors), `dotnet test` completo, `dotnet format --verify-no-changes`; smoke manual: seleção persiste, telas seguem o combo, terminal `pwd`, VS Code pasta + restart real, rail/mobile.
- [ ] **T8 — Docs + Done + PR:** docs bilíngues, `Status = Done`, PR em `feature/devin-20260920-global-repo-selector`, merge, redeploy do host.

**7.1 Validation strategy (.NET):**

- Unit: `SelectedRepositoryService` (com `IGitHubService` fake), `SpecAppService` repo-scoped dir, `TerminalSessionManager` workdir, `CodeServerProcessManager.RestartAsync`.
- Integration: `/api/specs?repo=` (400/404/empty), `POST /api/vscode/restart` (404/503), hub `Open(repo)` cwd.
- Manual/JS: sidebar rail, localStorage persistence, iframe reload.

## 8. Organization Guardrails

- **Branches:** `feature/devin-20260920-global-repo-selector` off `main`; nunca commit/push em `main`/`develop`.
- **Workflows:** `.github/workflows/**` intocado.
- **Security:** o cliente envia apenas `owner/repo`; todo path é resolvido e confinado server-side (`WorkspaceRoot`/`$HOME`); `localStorage` guarda só a string `owner/repo`; code-server continua `auth none` exclusivamente em `127.0.0.1` atrás da auth do app; nenhum token em logs/UI.
- **Scope:** sem clone pela UI, sem watchdog de restart, sem persistência server-side da seleção, sem mudanças em dialogs que já têm seletor próprio.
- **Architecture:** `SpecAppService` resolve o dir por request (sem cache de path); `TerminalSessionManager` permanece transport-agnostic (workdir resolvido no hub); UI só consome contratos/HTTP — sem lógica de path no WASM.

## 9. Definition of Done

- [ ] RF-001…RF-009 implementados.
- [ ] Todos os critérios da seção 6 verificados; edge cases tratados.
- [ ] `dotnet build` limpo (`TreatWarningsAsErrors`), `dotnet test` verde, `dotnet format --verify-no-changes` ok.
- [ ] Smoke manual: seleção global persiste e dirige Board/Gantt/Workflow/Specs/Terminal/VS Code; restart do code-server funciona com o serviço morto; rail e mobile intactos.
- [ ] Docs bilíngues atualizados; spec `Done`; PR aberto e mergeado; host redeployado.

## Open Questions / Pending Ambiguity

- Nenhuma — confirmado com o usuário: nome `global-repo-selector`; persistência em `localStorage`; Specs com clone ausente → empty state (sem fallback silencioso); ordem/agrupamento do menu conforme RF-003; cwd do terminal só em abas novas (`?cmd=` intacto); rail vira ícone que expande; restart = kill+respawn do code-server via `POST /api/vscode/restart`.
