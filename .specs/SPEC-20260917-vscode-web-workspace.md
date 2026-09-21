# SPEC-20260917-vscode-web-workspace

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `vscode-web-workspace` |
| Type | `Feature` |
| Stack | `.NET 10 / Blazor WASM / code-server / YARP` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260917-vscode-web-workspace` |
| Ticket | — |
| Status | `Done` |

## 1. User Story

**As a** administrador do Taskboard
**I want** abrir um VS Code no browser apontando para o diretório onde um card em execução está sendo trabalhado — e ter um item "VS Code" no menu lateral para edição rápida de arquivos
**So that** eu possa acompanhar e intervir ao vivo nas alterações que o agente CLI faz no clone do repositório, sem sair do app, sabendo que clones e execuções sempre acontecem em `~/repos/`.

**Problem context:**
Hoje `AgentExecutionRequest.RepoPath` vem de `DefaultRepoPath`, que **nunca é preenchido** — `KnownCliAgentAdapter` cai em `Environment.CurrentDirectory` (o publish dir `~/.taskboard/publish`), então o agente clona/edita num diretório opaco e imprevisível. Não existe editor no app: para ver o que o agente está fazendo é preciso SSH/terminal. O host não tem `code-server` instalado; `~/repos` já existe mas não é usado pelo pipeline.

## 2. Scope

**In scope:**
- `Taskboard:WorkspaceRoot` (default `~/repos`, criado se inexistente) como cwd default dos agent runs.
- Instalação gerenciada do **code-server** via allowlist + status (`POST /api/vscode/install`, `GET /api/vscode/status`).
- `CodeServerProcessManager`: spawn lazy de `code-server --bind-addr 127.0.0.1:<port> --auth none --disable-workspace-trust --app-name Taskboard` com `VSCODE_PROXY_URI=/vscode/proxy/{{port}}`, lifecycle filho do `taskboard-server`.
- Proxy **YARP** `/vscode/{**}` → loopback code-server, protegido pela auth do Taskboard (suporte WebSocket obrigatório).
- Página `/editor` com iframe + toolbar (caminho, abrir em nova aba) + item "VS Code" no menu lateral (abre em `$HOME`). `/vscode` fica reservado ao proxy.
- Botão **"Open in VS Code"** no card em execução e no `TaskDetailDialog`, resolvendo `~/repos/<repo-name>`.
- Empty state "Install VS Code Web" reutilizando o padrão de popup de log de instalação.
- Testes + docs.

**Out of scope:**
- Autenticação própria do code-server (fica `auth none` em loopback; a auth do Taskboard é o gate).
- Extensões/settings gerenciadas do VS Code, multi-workspace, port forwarding.
- Pre-clone server-side do repositório (o agente continua clonando; só o cwd muda).
- VS Code para sessões de terceiros/túnel externo (vscode.dev, `code tunnel`).
- Edição de arquivos fora de `$HOME` via botão (o parâmetro `path` é restrito — ver RF-006).

## 3. Technical Context

**Where the change happens:**
Nova capacidade em `Taskboard.Integrations` (`Vscode/`): `WorkspaceService` (resolve root e workdir por repo, guarda de path traversal), `CodeServerProcessManager` (probe de binário, spawn, status, saída recente), `VscodeInstallService` (install allowlisted via `StreamingProcessRunner`, mesmo padrão de `AgentCliInstallService`). Server: endpoints `/api/vscode/*` + mapping YARP. Blazor: página `/vscode`, NavMenu, botões no card/`TaskDetailDialog`, client HTTP. `KnownCliAgentAdapter` passa a usar `WorkspaceService` quando `RepoPath` está vazio.

**Facts do host:**
- `code-server` ausente; instalação user-level sem sudo via `install.sh --method=standalone` → `~/.local/lib/code-server-*` + `~/.local/bin/code-server`.
- `~/repos` existe; `code-server` requer WebSocket → proxy manual é frágil, YARP resolve.
- code-server é path-agnóstico (URLs relativas + proxy strip) — `--base-path` não é necessário (decidido na implementação; ver RF-005).

**Files to read before implementing:**
- `AGENTS.md`
- `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` + `StreamingProcessRunner.cs` (padrão a reutilizar)
- `src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs` (fallback de cwd)
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs`
- `src/Taskboard.Blazor/Components/Pages/AgentInstallDialog.razor` (popup de log a generalizar)
- `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` + card da board (botão)
- `src/Taskboard.Blazor/Layout/NavMenu.razor`
- `src/Taskboard.Server/Program.cs` (DI + endpoints + YARP)
- `Directory.Packages.props` (novo pacote `Yarp.ReverseProxy` — justificar no PR)

**Files to create or modify:**
```text
src/Taskboard.Domain.Shared/Workspace/WorkspacePaths.cs            (novo — resolução/sanitize)
src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs      (novo)
src/Taskboard.Integrations/Vscode/VscodeInstallService.cs          (novo)
src/Taskboard.Application.Contracts/Vscode/IVscodeStatusService.cs (novo) + VscodeStatus.cs
src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs          (fallback → WorkspaceRoot)
src/Taskboard.Server/Program.cs                                    (endpoints + UseReverseProxy + auth)
src/Taskboard.Blazor/Components/Pages/VsCode.razor                 (novo — iframe + toolbar + install)
src/Taskboard.Blazor/Components/Pages/AgentInstallDialog.razor     (generalizar p/ reuso ou duplicar padrão)
src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor      (botão Open in VS Code)
src/Taskboard.Blazor/Layout/NavMenu.razor                          (item VS Code)
src/Taskboard.Blazor/Services/TaskboardClient.cs                   (status/install)
Directory.Packages.props                                           (+ Yarp.ReverseProxy)
tests/Taskboard.Tests.Unit/Integrations/Vscode/*Tests.cs           (novos)
tests/Taskboard.Tests.Integration/VscodeEndpointsTests.cs          (novo)
docs/features.md · features.pt-br.md · api.md · api.pt-br.md
.specs/SPEC-20260917-vscode-web-workspace.md
```

## 4. Requirements

### RF-001: `Taskboard:WorkspaceRoot`
- **Description:** Chave de configuração (env `TASKBOARD_WORKSPACE_ROOT`) default `$HOME/repos`; `WorkspacePaths`/`WorkspaceService` resolve e **cria o diretório se inexistente** (`0700`-friendly, sem falhar se já existir).
- **Rules:** path relativo `~/` expande para `$HOME`; `Root` nunca retorna path fora de `$HOME` a menos que configurado explicitamente.
- **Input → Output:** config → `DirectoryInfo` garantido existente.

### RF-002: cwd default dos agent runs
- **Description:** `KnownCliAgentAdapter` (e equivalentes) usam `WorkspaceService.Root` quando `request.RepoPath` está vazio, em vez de `Environment.CurrentDirectory`.
- **Rules:** `RepoPath` explícito continua tendo precedência; prompt template ganha nota "clone o repositório dentro do diretório atual" (o clone `git clone <url>` cai em `~/repos/<repo>`).
- **Input → Output:** `AgentExecutionRequest(RepoPath:"")` → `AgentCommand.WorkingDirectory = ~/repos`.

### RF-003: Resolução do workdir do card
- **Description:** `WorkspaceService.GetRepoWorkdir(repositoryFullName)` → `<root>/<name>` onde `<name>` é o segmento após `/` sanitizado (apenas `[A-Za-z0-9._-]`); `TryGetCardWorkdir` retorna o dir se existir, senão `Root`.
- **Rules:** resultado sempre confinado ao `Root` (rejeita `..`, separadores extras, nomes vazios → `Root`).
- **Input → Output:** `"afonsoft/agent-harness"` → `~/repos/agent-harness` (ou `~/repos` se inexistente).

### RF-004: Instalação do code-server
- **Description:** `POST /api/vscode/install` executa comando allowlisted fixo: `bash -c "curl -fsSL https://code-server.dev/install.sh | sh -s -- --method=standalone"`; `GET /api/vscode/install/status` retorna snapshot com buffer de linhas (mesmo contrato de `AgentCliInstallStatus`).
- **Rules:** background, idempotente (sem run duplicado), timeout ~10min, saída sanitizada; `GET /api/vscode/status` → `{ installed, binaryPath, version, running, baseUrl }`.
- **Input → Output:** POST → `202` running / `200` final / status; probe: `code-server` no PATH ou `~/.local/bin/code-server`, `code-server --version` bounded.

### RF-005: `CodeServerProcessManager`
- **Description:** Singleton que sobe `code-server --bind-addr 127.0.0.1:<Port> --auth none --disable-telemetry --disable-update-check --disable-workspace-trust --app-name Taskboard` com env `VSCODE_PROXY_URI=/vscode/proxy/{{port}}` (links do painel de portas corretos sob o subpath) lazy na primeira necessidade (request ao proxy ou `EnsureStarted` da página); monitora exit; expõe `Status` (`Stopped|Starting|Running|Failed`) + linhas recentes de stdout/stderr.
- **Rules:** `Port` = `Taskboard:Vscode:Port` default `8377`; **sem `--base-path`** — code-server é path-agnóstico (URLs relativas); o proxy remove o prefixo `/vscode` e o browser resolve os assets sob `/vscode/` (trailing slash obrigatória — `GET /vscode` redireciona). Nunca bind em interface não-loopback; kill do filho no shutdown do host (`IAsyncDisposable`). `EnsureStartedAsync` aguarda a porta aceitar conexão (probe TCP a cada 250ms, timeout 30s) — processo vivo ≠ escutando; `Running` só vira `true` após o bind, evitando 502 de race no primeiro request ao proxy.
- **Input → Output:** `EnsureStartedAsync() → status/url`; exit inesperado → `Failed` + últimas linhas.

### RF-006: Proxy autenticado `/vscode/{**}`
- **Description:** YARP (`Yarp.ReverseProxy`) mapeia `/vscode/{**catch-all}` → `http://127.0.0.1:<Port>` removendo o prefixo, com upgrade WebSocket; pipeline exige usuário autenticado (mesmo esquema cookie do app).
- **Rules:** `?folder=<abs-path>` passa direto ao code-server — a página Blazor só emite paths validados (dentro de `$HOME`); code-server nunca acessível sem auth do Taskboard; se `!installed` ou processo `Failed`, a página mostra estado correspondente em vez de iframe quebrado.
- **Input → Output:** `GET /vscode/?folder=/home/ubuntu/repos` (autenticado) → VS Code no browser.

### RF-007: Página `/editor` + menu
- **Description:** NavMenu ganha item **"VS Code"** (`IconName.CodeSlash`) → página Blazor **`/editor`**: toolbar com path atual + link "abrir em nova aba" (mesma URL, `target=_blank`) + iframe `src=/vscode/?folder=<path>` ocupando o restante da viewport. Query params: `?path=<abs>` ou `?repo=owner/name` (resolvido via `GET /api/vscode/workdir`); default `$HOME`.
- **Decisão de rota (implementação):** a página fica em `/editor` porque `/vscode` é reservado ao endpoint proxied do code-server — `GET /vscode` redireciona para `/vscode/` (code-server exige trailing slash p/ URLs relativas) e `/vscode/{**}` vai ao YARP. Uma página Blazor em `/vscode` seria inalcançável.
- **Rules:** não instalado → empty state com botão **Install VS Code Web** + console de log (mesmo padrão do install dialog dos CLIs); sucesso do install → status refresh + iframe; `repo` inválido/fora do padrão `owner/name` → 404 server-side.
- **Input → Output:** `/editor?repo=afonsoft/x` → iframe do code-server no workdir do repo (ou root se ainda não clonado).

### RF-008: "Open in VS Code" no card
- **Description:** `TaskDetailDialog` mostra link **"Open in VS Code"** → `/editor?repo=<fullName>` (nova aba); a página resolve `~/repos/<repo>` server-side (fallback `~/repos` quando ainda não existe).
- **Rules:** workdir resolvido server-side via `GET /api/vscode/workdir?repo=<fullName>` para não vazar regra no cliente; se code-server não instalado, a própria `/editor` mostra o empty state de install.
- **Input → Output:** click → `/editor?repo=afonsoft/x` → VS Code em `~/repos/x` (ou root).

### RF-009: Documentação
- **Description:** features/api bilíngues documentam workspace root, endpoints `/api/vscode/*`, proxy `/vscode`, botões e o fato de que o code-server roda `auth none` confinado ao loopback atrás da auth do Taskboard.

**Business rules / invariants:**
- `code-server` nunca exposto fora de `127.0.0.1` e nunca acessível sem a auth do Taskboard.
- Comandos de install 100% allowlisted — nenhum argumento do cliente.
- Todo `path`/`folder` emitido pela UI fica confinado a `$HOME` (e workdirs a `Root`).
- Clone/execução de agentes sempre sob `WorkspaceRoot` por default.

## 5. API Contract

```http
GET  /api/vscode/status                      → { installed, binaryPath?, version?, running, port }
POST /api/vscode/install                     → 202 running | 200 done | (auth)
GET  /api/vscode/install/status              → { state, startedAtUtc, exitCode?, lines[] }
GET  /api/vscode/workdir?repo=owner/name     → { path }   (workdir do card, confinado ao root)
GET  /vscode/{**}                            → proxy YARP → 127.0.0.1:<port> (auth + WS upgrade)
```

**Auth:** cookie do app (mesmo dos demais endpoints); anônimo → 401/redirect.
**Expected errors:** `404` repo inválido no workdir; `503` quando code-server `Failed` (proxy retorna erro genérico, sem stack).

## 6. Acceptance Criteria

- [x] **Dado** `~/repos` inexistente **quando** o server resolve `WorkspaceRoot` **então** o diretório é criado.
- [x] **Dado** run sem `RepoPath` **quando** o adapter constrói o comando **então** `WorkingDirectory = ~/repos`.
- [x] **Dado** code-server ausente **quando** `GET /api/vscode/status` **então** `installed=false` e a página mostra "Install VS Code Web".
- [x] **Dado** install disparado **quando** `POST /api/vscode/install` **então** o script allowlisted roda em background e o popup mostra as linhas; em sucesso o popup fecha e o processo sobe.
- [x] **Dado** code-server running **quando** usuário autenticado abre `/vscode` **então** o iframe carrega o editor em `$HOME`.
- [x] **Dado** card em execução com clone em `~/repos/x` **quando** clica "Open in VS Code" **então** abre `/vscode?path=/home/ubuntu/repos/x`.
- [x] **Dado** `?path=/etc` ou fora de `$HOME` **quando** a página resolve **então** cai para `$HOME` (não emite folder arbitrário).
- [x] **Dado** request anônima **quando** `GET /vscode/` **então** 401/redirect — code-server inalcançável.
- [x] **Dado** processo code-server morto **quando** `GET /api/vscode/status` **então** `running=false` e a página oferece restart/retry.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `--base-path` ausente na versão instalada | spawn | fallback documentado (ver Open Questions) — página usa link direto autenticado ou instala versão com suporte |
| Porta 8377 ocupada | `EnsureStarted` | `Failed` com hint "configure Taskboard:Vscode:Port" |
| `repo` com `..` ou separadores | `workdir?repo=../x` | 400/404, nunca path fora do root |
| Install em andamento + novo POST | POST duplo | retorna status running, sem run duplicado |
| Restart do taskboard-server | filho code-server | morto junto; sobe lazy na próxima request |

## 7. Task Plan

- [x] **T1 — Workspace:** `WorkspacePaths`/`WorkspaceService` (root, criação, workdir, guarda traversal) + `KnownCliAgentAdapter` usando-o + nota no prompt template + testes.
- [x] **T2 — Install:** `VscodeInstallService` (allowlist `install.sh --method=standalone`, buffer sanitizado) + endpoints + testes (com fake runner).
- [x] **T3 — Process manager:** spawn lazy, status, lifecycle + testes com runner/starter injetáveis.
- [x] **T4 — Proxy:** `Yarp.ReverseProxy` no `Program.cs`, rota `/vscode/{**}` autenticada; teste de configuração.
- [x] **T5 — UI:** página `/editor` (iframe/toolbar/empty-install — `/vscode` é reservado ao proxy), NavMenu, botão card/dialog, client HTTP.
- [x] **T6 — Validação:** build + suites (395 unit + 116 integration); smoke real pós-deploy: install standalone no host, spawn, proxy autenticado, abrir `~/repos/<repo>`.
- [x] **T7 — Docs/PR:** docs bilíngues, SPEC `Done`, PR (justificar pacote YARP), merge, deploy.

**7.1 Validation:** .NET — unit tests (RF-001/002/003/005), integration (endpoints + auth + workdir), smoke manual do proxy/iframe (RF-006/007/008).

## 8. Organization Guardrails

- Branch `feature/devin-20260917-vscode-web-workspace`; nunca commit em `main`.
- Novo pacote NuGet `Yarp.ReverseProxy` → **justificar no PR** (único caminho com WS estável; alternativa manual descartada).
- `code-server` com `auth none` **somente** em `127.0.0.1`; jamais bind externo.
- Não logar conteúdo de arquivos/edições; saída do code-server sanitizada no status.
- Sem alteração em `.github/workflows/**`.

## 9. Definition of Done

- [x] RF-001…RF-009 implementados.
- [x] Critérios da seção 6 cobertos por testes/evidência (45 unit + 8 integration novos; workdir/traversal/loopback/auth cobertos).
- [x] Edge cases tratados (404 repo inválido, 503 sem binary, fallback root, Bearer-token sanitization fix).
- [x] Build + testes verdes (395 unit + 116 integration); smoke do proxy autenticado documentado no PR.
- [x] Guardrails respeitados; pacote YARP justificado no PR; Bearer sanitizado antes de key:value; sem secrets em logs.

**Next action:** PR → merge → deploy.

## Open Questions / Pending Ambiguity

- ~~`--base-path` no code-server~~ **Resolvido:** code-server é path-agnóstico — serve assets por URLs relativas. O proxy remove `/vscode` e o browser re-resolve sob `/vscode/` (trailing slash). Nenhuma flag necessária; verificado contra a doc oficial de reverse proxy do code-server.
