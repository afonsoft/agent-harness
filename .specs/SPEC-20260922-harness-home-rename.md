# SPEC-20260922-harness-home-rename

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `harness-home-rename` |
| Type | `Refactor` + `Infra` (runtime paths/env migration) |
| Stack | `.NET 10 / systemd --user / Docker / install.sh` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `refactor/20260922-harness-home-rename` |
| Ticket | [#309](https://github.com/afonsoft/agent-harness/issues/309) — solicitado pelo usuário (epic #306) |
| Status | `Approved` |

---

## 1. User Story

**As a** operador do agent-harness
**I want** os caminhos runtime e variáveis de ambiente alinhados ao nome do produto (`Harness`/`agent-harness`)
**So that** a instalação usa `~/.agent-harness`, `HARNESS_*` e `harness.sqlite`, com migração automática da instalação existente `~/.taskboard` sem perda de dados ou downtime manual.

### Problem Context

A decisão de branding (SPEC-20260918-harness-rebranding) manteve identificadores internos `taskboard` — incluindo o diretório de instalação `~/.taskboard`, envs `TASKBOARD_*` e o banco `taskboard.sqlite`. O usuário agora quer completar o rename **na superfície operacional** (paths, envs, arquivo de DB, serviço systemd, docker-compose), preservando namespaces/projetos/`taskctl` internos.

**Evidência do AS-IS** (inventário desta auditoria):

- `install.sh:10` `TASKBOARD_HOME=${TASKBOARD_HOME:-$HOME/.taskboard}`; aceita legado `TASKBOARD_DIR`.
- Envs lidos no código (`RuntimeConfigurationService` catalog + `TaskboardEnvironment` + `AdminUser` + `ApiKeyAuthenticationHandler` + CLI/MCP `TaskboardApiClient`): `TASKBOARD_PORT`, `TASKBOARD_URL`, `TASKBOARD_API_KEY`, `TASKBOARD_DATA_DIR`, `TASKBOARD_ADMIN_USERNAME`, `TASKBOARD_ADMIN_PASSWORD`, `TASKBOARD_SKILLS_REPO`, `TASKBOARD_RAG_NAME`, `TASKBOARD_RAG_URL`, `TASKBOARD_RAG_API_KEY`, `TASKBOARD_TERMINAL_ENABLED`, `TASKBOARD_DEFAULT_PROMPT`, `TASKBOARD_HOME`, `TASKBOARD_REPO`, `TASKBOARD_DIR`.
- `taskboard.sqlite` hard-coded: `TaskboardDbContextFactory.cs:19`, `Program.cs:96,575`, `appsettings.json` (`Data Source=.data/taskboard.sqlite`), `appsettings.Production.json` (`Taskboard:DataDir=/var/taskboard/data`).
- `WithoutTaskboardEnv.cs` — remove envs `TASKBOARD_*` de processos de agente (precisa remover `HARNESS_*` também).
- systemd: `~/.config/systemd/user/taskboard-server.service` → `ExecStart=~/.taskboard/bin/taskboard-server`, `Environment=TASKBOARD_HOME=...`.
- `install.sh` gera wrappers `bin/taskboard-server`, `bin/taskboard-mcp` e a unit acima.
- `Dockerfile:50` `ENV TASKBOARD_DATA_DIR=/data`; `docker-compose.yml` envs + comentado `TASKBOARD__ACP__SESSIONRUNS` + volume `taskboard-data`.
- Manifest de skills sync: `.taskboard-skills.json` (`SkillsSyncService.cs:25`).
- `IWorkspaceIsolationService` doc: worktrees em `~/.taskboard/worktrees/{runId}`.
- docs: `README.md`, `docs/installation*.md`, `docs/api*.md`, `docs/architecture/*`, `.claude/skills/manage-taskboard/*` (8+ arquivos).

**Verificado — não depende do path:** o MCP provisioning registra o servidor por **URL HTTP** (`McpProvisioningService` `mcp add -t http <url>`), não pelo path do wrapper → migração não quebra `~/.claude.json` existente.

---

## 2. Scope

### In scope

1. **Env vars**: `TASKBOARD_*` → `HARNESS_*` (todas as 15 listadas). Leitura com **fallback**: `HARNESS_X` primeiro; se ausente, `TASKBOARD_X` com warning de deprecação. Fallback removido num ciclo futuro.
2. **Home dir**: default `~/.taskboard` → `~/.agent-harness` (`HARNESS_HOME`, legado `HARNESS_DIR`; fallback `TASKBOARD_HOME`/`TASKBOARD_DIR`).
3. **DB**: `taskboard.sqlite` → `harness.sqlite`. **Migração no startup**: se `harness.sqlite` não existe e `taskboard.sqlite` existe no data dir → rename (inclui `-wal`/`-shm` se presentes). Idempotente.
4. **Seção de config `Taskboard:`**: **mantida** internamente (decisão de branding + chaves já persistidas na tabela de overrides do SQLite e `admin.json` usam `Taskboard:*` — renomear exigiria migração de dados). Adicionar shim no startup: envs `HARNESS__*` são traduzidos para `Taskboard:*` via `AddInMemoryCollection` (ordem: depois dos providers padrão → `HARNESS__*` vence). `Taskboard__*` continua funcionando nativamente (compat).
5. **`WithoutTaskboardEnv`** → renomear para `WithoutHarnessEnv`, removendo **ambos** os prefixos (`HARNESS_*` e `TASKBOARD_*`) do env de processos de agente.
6. **install.sh**: `HARNESS_HOME` default `~/.agent-harness`; arquivo `env` gerado emite `HARNESS_*`; wrappers `bin/harness-server` e `bin/harness-mcp`; unit `harness-server.service`; modo **migrate**: detecta `~/.taskboard` existente → executa migração (runbook abaixo).
7. **systemd (deploy local)**: nova unit `harness-server.service` (`ExecStart=~/.agent-harness/bin/harness-server`, `Environment=HARNESS_HOME=...`); migração: `stop+disable taskboard-server` → `mv ~/.taskboard ~/.agent-harness` → escrever nova unit → `daemon-reload` → `enable --now` → remover unit antiga.
8. **docker-compose.yml**: comentado `TASKBOARD__ACP__SESSIONRUNS` → `HARNESS__ACP__SESSIONRUNS`; `Admin__Password`/`GITHUB_TOKEN` inalterados; volume `taskboard-data` → `agent-harness-data` **documentado como breaking para volumes existentes** (migração: `docker run --rm -v taskboard-data:/from -v agent-harness-data:/to alpine cp -a /from/. /to/` ou manter nome antigo — recomendar manter `taskboard-data` por compat e anotar).
9. **Dockerfile**: `ENV TASKBOARD_DATA_DIR=/data` → `HARNESS_DATA_DIR=/data`; comentários atualizados.
10. **Manifest de skills**: `.taskboard-skills.json` → `.harness-skills.json` com leitura-fallback do nome antigo (um ciclo).
11. **Docs**: atualizar todos os refs (`README.md`, `docs/installation*`, `docs/api*`, `docs/architecture/*`, `manage-taskboard` skill, `CLAUDE.md` nota de branding — ajustar a frase "env vars TASKBOARD_* NÃO são renomeados" para refletir a nova decisão: envs/paths renomeados, namespaces/projetos/taskctl mantidos).
12. **Testes**: atualizar testes que setam `TASKBOARD_*` (factory, `RuntimeConfigurationServiceTests`, `McpEndpointsTests`) e adicionar cobertura do fallback + da migração do sqlite.

### Out of scope

- Renomear namespaces `Taskboard.*`, projetos `.csproj`, solution, assembly names, CLI `taskctl`, seção de config `Taskboard:`, connection-string name `Taskboard`, GitHub repo name — decisão de branding preservada.
- Renomear imagem/container Docker (`agent-harness` já correto).
- Mudar porta default 47823.
- CI/workflows (nenhum uso de `TASKBOARD_*` encontrado nos workflows — confirmar na implementação).
- Remover o fallback `TASKBOARD_*` (fica para ciclo futuro, com spec própria).

---

## 3. Technical Context

**Onde acontece:**

| Área | Arquivos |
|---|---|
| Env catalog + env reads | `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` (11 EnvAlias), `src/Taskboard.Application.Contracts/Configuration/TaskboardEnvironment.cs`, `src/Taskboard.Server/Services/AdminUser.cs`, `src/Taskboard.Server/Auth/ApiKeyAuthenticationHandler.cs` |
| DB filename | `src/Taskboard.EntityFrameworkCore/Data/TaskboardDbContextFactory.cs`, `src/Taskboard.Server/Program.cs` (2 refs + migração), `src/Taskboard.Server/appsettings.json`, `appsettings.Production.json` |
| Env shim `HARNESS__*` | `src/Taskboard.Server/Program.cs` (após `CreateBuilder`, antes do uso) |
| Agent env scrub | `src/Taskboard.Integrations/Execution/WithoutTaskboardEnv.cs` → `WithoutHarnessEnv.cs` |
| Skills manifest | `src/Taskboard.Integrations/Skills/SkillsSyncService.cs`, `SyncManifest.cs` |
| CLI/MCP env | `src/Taskboard.Cli/Program.cs`, `Taskboard.Cli/Services/TaskboardApiClient.cs`, `src/Taskboard.Mcp/Program.cs`, `Taskboard.Mcp/Services/TaskboardApiClient.cs` |
| Install/deploy | `install.sh` (home, env file, wrappers, unit), `Dockerfile`, `docker-compose.yml` |
| Testes | `tests/Taskboard.Tests.Integration/TaskboardWebApplicationFactory.cs`, `McpEndpointsTests.cs`, `tests/Taskboard.Tests.Unit/Application/RuntimeConfigurationServiceTests.cs` |
| Docs | `README.md`, `docs/installation*.md`, `docs/api*.md`, `docs/architecture/*`, `.claude/skills/manage-taskboard/`, `CLAUDE.md` |

**Helper de resolução de env** (padrão a implementar): método único `ResolveEnv("HARNESS_PORT", "TASKBOARD_PORT")` — novo primeiro, legado com `ILogger` warning uma vez por chave.

**Migração do sqlite** (em `Program.cs`, antes de criar `SqliteConfigurationProvider`): se `!File.Exists(harness.sqlite) && File.Exists(taskboard.sqlite)` → `File.Move` incluindo sidecars `-wal`/`-shm`; log `info` da migração.

**Runbook de migração do host local** (executado via `install.sh --migrate` ou documentado):

```bash
systemctl --user stop taskboard-server
systemctl --user disable taskboard-server
mv ~/.taskboard ~/.agent-harness
mv ~/.agent-harness/data/taskboard.sqlite ~/.agent-harness/data/harness.sqlite  # ou deixa o startup migrar
# regenerar env file com HARNESS_* (install.sh) ou sed 's/^export TASKBOARD_/export HARNESS_/'
# nova unit harness-server.service → daemon-reload → enable --now
rm ~/.config/systemd/user/taskboard-server.service
curl -sf http://127.0.0.1:47823/health
```

---

## 4. Requirements

- **RF-001:** Toda env `TASKBOARD_*` documentada tem equivalente `HARNESS_*`; `HARNESS_*` tem precedência; `TASKBOARD_*` funciona com warning de deprecação (uma vez por processo/chave).
- **RF-002:** `HARNESS__*` (duplo underscore) binds à seção `Taskboard:` via shim; `Taskboard__*` continua válido.
- **RF-003:** Data dir default `~/.agent-harness` (fora do container); `HARNESS_HOME`/`HARNESS_DATA_DIR` configuráveis; legados `TASKBOARD_HOME`/`TASKBOARD_DIR`/`TASKBOARD_DATA_DIR` com fallback.
- **RF-004:** Startup migra `taskboard.sqlite` → `harness.sqlite` (com `-wal`/`-shm`), idempotente, sem perda.
- **RF-005:** `install.sh` instala em `~/.agent-harness` com wrappers `harness-server`/`harness-mcp` e unit `harness-server.service`; `--migrate` executa o runbook do §3 sem intervenção manual.
- **RF-006:** Processos de agente recebem env sem `HARNESS_*` nem `TASKBOARD_*` (scrub dos dois prefixos).
- **RF-007:** `.harness-skills.json` é o manifest canônico; `.taskboard-skills.json` ainda é lido (fallback).
- **RF-008:** docker-compose/documentação refletem `HARNESS_*`; volume nome — decisão documentada (recomendado: manter `taskboard-data` para não órfão volumes existentes, com nota).
- **RF-009:** `CLAUDE.md`/docs atualizados — a regra "env vars TASKBOARD_* não são renomeados" passa a "envs/paths operacionais usam HARNESS_/~/.agent-harness; namespaces/projetos/taskctl mantêm taskboard".

## 5. Acceptance Criteria (BDD)

- **AC1:** Servidor sobe com `HARNESS_PORT`/`HARNESS_DATA_DIR`/`HARNESS_ADMIN_PASSWORD` e zero `TASKBOARD_*` setadas → funciona idêntico.
- **AC2:** Com apenas `TASKBOARD_PORT` setada → funciona + log de deprecação.
- **AC3:** `HARNESS__ACP__SESSIONRUNS=true` ativa `Taskboard:Acp:SessionRuns`; `Taskboard__Acp__SessionRuns` também funciona.
- **AC4:** Data dir contendo só `taskboard.sqlite` → após boot existe `harness.sqlite` com os mesmos dados (verificar via endpoint) e `taskboard.sqlite` ausente.
- **AC5:** `install.sh --migrate` num host com `~/.taskboard` + unit `taskboard-server` → termina com `~/.agent-harness`, `harness-server.service` active, `GET /health` 200, e unit antiga removida.
- **AC6:** Agente spawnado pelo harness tem `env` sem variáveis `HARNESS_*`/`TASKBOARD_*` (teste de integração ou unit do `WithoutHarnessEnv`).
- **AC7:** Suite verde: unit + integration (inclui testes novos de fallback/migração).
- **AC8:** `grep -rn "TASKBOARD_\|\.taskboard\|taskboard\.sqlite" src/ install.sh Dockerfile docker-compose.yml` → só ocorrências de fallback/compat comentadas como tal.

## 6. Task Plan

- [ ] **T1:** `ResolveEnv` helper + troca dos EnvAlias no `RuntimeConfigurationService` + `TaskboardEnvironment` + `AdminUser` + `ApiKeyAuthenticationHandler` + CLI + MCP (fallback com warning).
- [ ] **T2:** Shim `HARNESS__*`→`Taskboard:*` no `Program.cs` + testes de binding.
- [ ] **T3:** Rename `taskboard.sqlite`→`harness.sqlite` + migração de startup + testes.
- [ ] **T4:** `WithoutHarnessEnv` (dois prefixos) + teste.
- [ ] **T5:** `.harness-skills.json` + fallback de leitura.
- [ ] **T6:** `install.sh`: novos defaults/nomes + modo `--migrate` (runbook §3).
- [ ] **T7:** `Dockerfile` + `docker-compose.yml` + `appsettings.Production.json` path.
- [ ] **T8:** Docs + `CLAUDE.md` regra + `manage-taskboard` skill refs.
- [ ] **T9:** Suite verde + smoke do deploy migrado neste host (`systemctl --user status harness-server`, `/health`).

## 7. Organization Guardrails

- Branch `refactor/20260922-harness-home-rename`; PR normal (squash).
- **Não** toca `.github/workflows/**`.
- Migração do host local (stop/mv/systemd) executada só após merge + aprovação explícita — é operação com downtime breve do serviço.
- Fallbacks marcados `// DEPRECATED:` com data alvo de remoção.

## 8. Definition of Done

- [ ] AC1–AC8 verificados.
- [ ] Suite unit+integration verde.
- [ ] Docs sincronizados; breaking change documentado (envs antigas deprecated, não removidas).
- [ ] Runbook de migração validado no host local pós-merge.
