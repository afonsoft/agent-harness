# SPEC-20260921-docker-runtime-paths

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `docker-runtime-paths` |
| Type | `Bugfix + Feature` |
| Stack | `Docker / docker-compose / .NET 10 runtime image` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260921-docker-runtime-paths` |
| Ticket | [#275](https://github.com/afonsoft/agent-harness/issues/275) |
| Status | `Approved` |

## 1. User Story

**As a** operador que sobe o Harness via Docker
**I want** que todos os paths efetivos (`DataDir`, workspace `~/repos`, worktrees, credenciais dos CLIs) vivam sob o volume `/data` mapeado no servidor
**So that** o banco, o workspace dos agentes e os logins dos CLIs sobrevivem a `docker compose down`/`up` e recriação do container — e o workdir padrão é sempre `~/repos`, nunca `/` ou um path fora do volume.

**Problem context — erros confirmados no Dockerfile atual (validado com `docker build` + `docker run` + volume):**

1. **O SQLite não vai para o volume.** `appsettings.Production.json` seta `Taskboard:DataDir = /var/taskboard/data` e o runtime image roda com `ASPNETCORE_ENVIRONMENT=Production` → o banco é criado em **`/var/taskboard/data/taskboard.sqlite`**, fora do volume `/data` (verificado em container: `/data` só contém `home/`; o DB e `skills-cache` ficam em `/var/taskboard/data/`). Resultado: `taskboard.sqlite`, `admin.json`, `ConfigurationOverrides` e `skills-cache` são **perdidos** a cada recriação — embora `docs/installation.md` afirme que basta montar `/data`. Fix: `ENV TASKBOARD_DATA_DIR=/data` (env vence appsettings; o file `/var/taskboard/data` continua válido para installs bare-metal em produção).
2. **Não existe `docker-compose.yml`** no repo — o usuário precisa montar o `docker run` à mão, e nada garante o mapeamento certo.
3. **`VOLUME /data` não declarado** — `docker run` sem `-v` cria storage efêmero sem aviso.
4. **`git` "dubious ownership"**: quando `~/repos` do host é bind-mounted, o uid do container difere do dono dos arquivos → `git` recusa operações dentro dos clones (`detected dubious ownership`), quebrando clones, worktrees e agent runs.
5. **Sem healthcheck** — compose não sabe quando o app está pronto (`/health` já existe).
6. **Sem documentação do `user:` para bind mount de `~/repos`**: rodando como root (default), arquivos criados pelos agentes no host ficam `root:root`; rodando como `user: $UID`, o named volume herdado do root da imagem não é gravável. A estratégia precisa ser explícita.

**Paths confirmados corretos no código (não mexer):** `WorkspaceService`/`WorkspacePaths.ResolveRoot` → `$HOME/repos` (com `HOME=/data/home` → `/data/home/repos`); worktrees → `~/.taskboard/worktrees`; credenciais de CLIs → `$HOME/.claude`, `.codex`, `.config/*` — todos sob `/data` **desde que** `HOME` se mantenha e o volume seja montado.

## 2. Scope

**In scope:**
- `Dockerfile`: `ENV TASKBOARD_DATA_DIR=/data`, `VOLUME /data`, `HEALTHCHECK` via `/health`, `git config --system --add safe.directory '*'`, `mkdir -p /data/home/repos`.
- `docker-compose.yml` novo: service `harness`, `build: .`, `ports: 47823`, volume nomeado `taskboard-data → /data`, bind mount opcional `${HOME}/repos → /data/home/repos` (opt-in via env), `Admin__Password`/`GITHUB_TOKEN`/`TASKBOARD_*` env passthrough, `restart: unless-stopped`, healthcheck.
- `.env.example` com as variáveis suportadas.
- `docs/installation.md` + `.pt-br`: seção Docker/compose atualizada com o comando real e o mapa de paths.
- Validação: `docker build` limpo + `docker compose up` com volume montado → assert `taskboard.sqlite` em `/data`, workspace root `/data/home/repos`, app respondendo em `/health`.

**Out of scope:**
- Imagem publicada em registry / CI de docker build (decisão separada).
- Non-root por default — os CLIs de agente fazem `npm -g`, instaladores e writes arbitrários; documentar `user:` como opção do operador.
- Multi-container (code-server separado, cloudflared) — code-server já é spawnado in-process.

## 3. Technical Context

**Files to read before implementing:**
- `Dockerfile` — estágio runtime (HOME, dirs, CLIs)
- `src/Taskboard.Application.Contracts/Configuration/TaskboardEnvironment.cs` — `GetDataDir` lê `TASKBOARD_DATA_DIR` → `Taskboard:DataDir` → `{ContentRoot}/.data`
- `src/Taskboard.Server/Program.cs:91,189,343-354` — `dataDir`, `homeDir`, `WorkspaceService`
- `src/Taskboard.Domain.Shared/Workspace/WorkspacePaths.cs` — `~/repos` default
- `docs/installation.md:215-226` — instrução docker atual (diz "mount ~/.taskboard/data at /data" mas sem o env o DB não vai pra lá)

**Files to create or modify:**
```text
Dockerfile                  [mod]
docker-compose.yml          [novo]
.env.example                [novo]
docs/installation.md        [mod]
docs/installation.pt-br.md  [mod]
```

## 4. Requirements

### RF-001: DataDir dentro do volume
- **Description:** `ENV TASKBOARD_DATA_DIR=/data` no runtime stage → `taskboard.sqlite`, `admin.json`, overrides e `skills-cache` ficam persistidos no volume `/data`, em vez de `/var/taskboard/data` (appsettings.Production) ou `/app/.data`.
- **Rules:** usar `TASKBOARD_DATA_DIR` (env direto — `GetDataDir` não lê `Taskboard__DataDir`); não mudar `appsettings.Production.json` (válido para systemd/bare-metal).

### RF-002: Workspace sempre `~/repos`
- **Description:** `HOME=/data/home` já garante `WorkspaceRoot=/data/home/repos`; pré-criar `/data/home/repos` na imagem e garantir que nada no compose redefina `Taskboard:WorkspaceRoot` para fora de `$HOME`.
- **Rules:** workdir padrão de agents/fluxo/cockpit/terminal = `~/repos` (container) = `/data/home/repos` no volume; bind mount opcional `${HARNESS_REPOS_DIR:-$HOME/repos}` → `/data/home/repos` para os agentes operarem nos repos reais do servidor.

### RF-003: docker-compose.yml
- **Description:** service único `harness`: `build: .`, `image: agent-harness:latest`, `ports: "47823:47823"`, `volumes: taskboard-data:/data` (+ bind opcional de repos), `environment:` com `Admin__Password`, `GITHUB_TOKEN` opcional, `TASKBOARD_*` documentados, `restart: unless-stopped`, `healthcheck` em `GET /health`.
- **Rules:** compose funciona com `docker compose up -d` puro; credenciais de CLI persistem porque `HOME=/data/home` está no volume.

### RF-004: Git safe.directory
- **Description:** `git config --system --add safe.directory '*'` na imagem — bind mounts do host têm ownership diverso e o git recusa operar sem isso.
- **Security note:** aceitável porque o container é single-tenant self-hosted; documentar no compose comment.

### RF-005: Healthcheck
- **Description:** `HEALTHCHECK CMD curl -fsS http://localhost:47823/health || exit 1` na imagem + `healthcheck:` equivalente no compose.

### RF-006: Docs e .env.example
- **Description:** atualizar a seção Docker (en + pt-br) com `docker compose up -d`, mapa de paths (`/data`, `/data/home`, `/data/home/repos`, `/data/taskboard.sqlite`), e a nota de `user:` para bind mounts.

## 5. API Contract

Nenhum endpoint novo. Contrato de runtime:

```yaml
volumes:
  - taskboard-data:/data        # DB, overrides, skills-cache
  # - ${HARNESS_REPOS_DIR}:     # opcional — repos reais do servidor
  #   /data/home/repos
environment:
  - TASKBOARD_DATA_DIR=/data    # setado na imagem
  - HOME=/data/home             # setado na imagem
  - Admin__Password=...
```

## 6. Acceptance Criteria

- [ ] **Dado** `docker compose up -d` com volume `taskboard-data` **quando** o app sobe **então** `taskboard.sqlite` é criado em `/data` (não `/app/.data`).
- [ ] **Dado** container rodando **quando** um agente/terminal/cockpit resolve o workdir default **então** o path é `/data/home/repos` (= `~/repos`).
- [ ] **Dado** `docker compose down && up` **então** DB, credenciais de CLI e workspace sobrevivem.
- [ ] **Dado** `~/repos` bind-mounted **quando** um agente faz `git status`/clone dentro do repo **então** não há erro de dubious ownership.
- [ ] **Dado** a imagem **quando** `docker inspect` **então** `VOLUME /data` e `HEALTHCHECK` presentes.
- [ ] **Dado** docs **quando** leio installation.md **então** o comando compose e o mapa de paths estão corretos.

## 7. Test Plan

- `docker build -t agent-harness:test .` sem erro.
- `docker compose -f docker-compose.yml config` válido.
- `docker compose up -d` → `curl localhost:47823/health` → `Healthy`; `docker exec` confirma `/data/taskboard.sqlite` e `/data/home/repos`.
- Recreate: `compose down && up` → settings/DB persistidos.
- Bind mount: `HARNESS_REPOS_DIR=~/repos docker compose up` → `git status` dentro de um repo do host funciona no container.

## 8. Tasks

- [ ] **T1 — Dockerfile:** `TASKBOARD_DATA_DIR`, `VOLUME`, `HEALTHCHECK`, git safe.directory, `mkdir /data/home/repos`.
- [ ] **T2 — compose + .env.example.**
- [ ] **T3 — docs en/pt-br.**
- [ ] **T4 — validação real:** build + compose up + asserts do Test Plan.
