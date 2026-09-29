# SPEC-20260929-docker-cli-context

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `docker-cli-context` |
| Type | `Bugfix / Integrations` |
| Stack | `.NET 10 / Docker / PTY / ACP` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260929-docker-cli-context` |
| Ticket | [#368](https://github.com/afonsoft/agent-harness/issues/368) (Epic [#366](https://github.com/afonsoft/agent-harness/issues/366)) |
| Status | `Approved` |
| Related | `SPEC-20260928-ai-code-generic-cli` (PR #353), `SPEC-20260929-pty-session-security` (allowlist de contêiner) |

## 1. User Story

**As a** usuário do AI Code
**I want** que threads com `ContainerContext` executem a CLI *dentro* do contêiner correto — inclusive CLIs que existem só no contêiner e modo Chat (ACP)
**So that** o seletor de contexto Docker seja real e não uma decoração: hoje ele produz comandos que falham ou executam no host.

**Problem context:**

Achados do Devin Review no PR #353 (confirmados no código atual):

1. 🔴 **Caminho do host no `docker exec`** — `ThreadPtyResolver` (`~L82`) resolve `path = discovery.ResolveExecutablePath(agentType)` no host e injeta o caminho absoluto local no argv; `DockerCliSpawner.BuildExecArgs` apenas concatena após `docker exec`. Ex.: `/home/harness/.local/bin/claude` não existe dentro do contêiner `dev` → spawn falha.
2. 🔴 **CLI exclusiva do contêiner é recusada** — a criação de thread PTY exige `ResolveExecutablePath` no host e o seletor (`AiChat.razor ~L610-645`) só lista agentes instalados no host, apesar de `DockerCliDiscovery.ListContainersAsync` já reportar CLIs por contêiner.
3. 🔴 **Chat ignora ContainerContext** — `ContainerContext` é persistido na thread, mas `AgentSessionManager.EnsureSessionAsync` (`~L103-109`) inicia a sessão ACP sem o contexto e `AiChatService.ExecuteRunAsync` (`~L422-423`) usa o runner one-shot sem passá-lo — a CLI executa no host.

## 2. Scope

**In scope:**

- `docker exec` usar o **nome do binário** (`AgentCliMap`/`AgentCliSpec.Binary`) em vez do caminho absoluto do host quando `ContainerContext` está definido.
- Criação/seleção de thread com `ContainerContext` validar a CLI contra a descoberta *daquele contêiner* e listar CLIs container-only no seletor.
- Modo Chat (ACP) honrar `ContainerContext`: spawn da sessão ACP e do runner one-shot dentro do contêiner.
- Workdir dentro do contêiner (path mapping host→container) se necessário para o spawn funcionar.
- Testes por correção.

**Out of scope:**

- Allowlist/autorização de contêiner (SPEC-20260929-pty-session-security, RF-002).
- Suporte a runtimes de contêiner além de Docker.
- Pools/persistência de `docker exec` entre mensagens (sessão já gerencia lifecycle).

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Server/Services/ThreadPtyResolver.cs` — argv docker com nome de binário.
- `src/Taskboard.Integrations/Agents/DockerCliSpawner.cs` — `BuildExecArgs` (talvez receber nome vs path).
- `src/Taskboard.Integrations/Agents/DockerCliDiscovery.cs` — reusar `ListContainersAsync` para validação/descoberta por contêiner.
- `src/Taskboard.Server/Services/AgentSessionManager.cs` (`EnsureSessionAsync`) e `src/Taskboard.Application/AiChat/AiChatService.cs` (`ExecuteRunAsync`) — propagação do `ContainerContext` no modo Chat.
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — seletor inclui CLIs por contêiner.

**Files to read before implementing:**

- `src/Taskboard.Server/Services/ThreadPtyResolver.cs`
- `src/Taskboard.Integrations/Agents/DockerCliSpawner.cs`, `DockerCliDiscovery.cs`, `AgentDiscoveryService.cs`
- `src/Taskboard.Server/Services/AgentSessionManager.cs`
- `src/Taskboard.Application/AiChat/AiChatService.cs` (`ExecuteRunAsync`, `StartRunAsync`)
- `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` (`Binary` por spec)

## 4. Requirements

### RF-001: `docker exec` com nome do binário

- **Description:** Com `ContainerContext`, o argv interno usa `spec.Binary` (nome, resolvido pelo PATH do contêiner) — não `ResolveExecutablePath` do host. Execução local continua usando o caminho absoluto.

### RF-002: Descoberta por contêiner

- **Description:** Validação de elegibilidade e o seletor de agente consideram `DockerCliDiscovery` do contêiner alvo quando `ContainerContext` está definido; CLIs presentes só no contêiner são criáveis/listáveis (marcadas como "container: nome").

### RF-003: Chat honra ContainerContext

- **Description:** `EnsureSessionAsync` e o caminho de run one-shot usam o `ContainerContext` da thread para spawnar a CLI via `docker exec` (ACP ou PTY conforme transporte), com env/workdir coerentes.

### RF-004: Workdir no contêiner

- **Description:** Definir e documentar o workdir dentro do contêiner (ex.: `/workspaces/...` ou o workdir configurado no contêiner); paths de host não devem vazar para dentro do `docker exec`.

## 5. API Contract

Sem mudança de contrato: `containerContext` já existe no payload de criação; passa a ser efetivamente aplicado. Documentar em `docs/` que CLIs do contêiner aparecem no seletor.

## 6. Acceptance Criteria

- [ ] **Given** CLI instalada só no contêiner `dev` **when** thread PTY é criada com `containerContext=dev` **then** a criação é aceita e o `docker exec` usa o nome do binário (teste com fake discovery).
- [ ] **Given** CLI no host em `/home/harness/.local/bin/x` e no contêiner em `/usr/bin/x` **when** `containerContext=dev` **then** o argv interno é `x` (nome), não o path do host (teste verde).
- [ ] **Given** thread Chat com `containerContext` **when** a primeira mensagem é enviada **then** a sessão é iniciada dentro do contêiner (teste de integração com spawner fake).
- [ ] **Given** build/test **then** verde.

## 7. Task Plan (agent execution)

- [ ] **T1 — argv:** nome de binário no `docker exec` + teste.
- [ ] **T2 — discovery:** validação/seleção por contêiner + testes.
- [ ] **T3 — chat:** propagação do contexto no ACP/run + testes.
- [ ] **T4 — UI:** seletor lista CLIs por contêiner.
- [ ] **T5 — Docs + Done + PR.**

## 8. Organization Guardrails

- Sem regra de negócio em endpoints; decisões em Application/Integrations.
- Depende da allowlist de contêiner do SPEC-20260929-pty-session-security para nomes não-autorizados (sequência sugerida: security primeiro).
- `.github/workflows/**` intocado.

## 9. Definition of Done

- [ ] `docker exec` funciona para CLIs com path divergente e para CLIs container-only.
- [ ] Chat executa dentro do contêiner quando configurado.
- [ ] Build/test verde; `Status = Done`.
