# SPEC-20260928-ai-code-generic-cli

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-code-generic-cli` |
| Type | `Feature` |
| Stack | `.NET 10 / Blazor WASM / SignalR / xterm.js / ACP / PTY / Docker` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260928-ai-code-generic-cli` |
| Ticket | [#346](https://github.com/afonsoft/agent-harness/issues/346) |
| Status | `Approved` |
| Related | `SPEC-20260921-ai-chat-cli-backend`, `SPEC-20260921-ai-code-chat-ux`, `SPEC-20260921-docker-runtime-paths`, `SPEC-20260928-ai-code-ux-simplify` |

## 1. User Story

**As a** usuário do Harness
**I want** que o AI Code funcione com qualquer CLI de agente instalada no servidor ou em containers Docker — não apenas as 6 CLIs hardcoded que falam ACP
**So that** eu possa conversar/operar com qualquer ferramenta (`openhands`, `agy`, `aider`, `kimi`, CLIs customizadas internas) pela mesma interface, sem precisar de código novo a cada CLI.

**Problem context — análise comparativa (clones em `/tmp/cli-ux-analysis/`):**

| Abordagem | Repo | Como comunica com a CLI | Trade-off |
| --- | --- | --- | --- |
| **Raw PTY passthrough** | `vultuk/claude-code-web` | `node-pty` spawn → xterm.js via WebSocket; zero parsing | Funciona com **qualquer** CLI interativa (tem bridges idênticas para `claude`, `codex`, `cursor-agent` — só muda o binário). UX é um terminal — sem tool cards, permissions ou métricas estruturadas. |
| **Stream-JSON estruturado** | `comfortablynumb/claudito` | `claude` com `--output-format stream-json --input-format stream-json`; NDJSON tipado (tool_use, permission, plan_mode, AskUserQuestion → `sendToolResult`) | UX rica mas acoplada ao dialeto da CLI; exige adaptador por ferramenta. Abstrai via interface `Agent` (ClaudeBinary, OpencodeAgent, AnthropicSdkAgent) e `ProcessSpawner` (local ou `docker exec` em container por projeto). |
| **ACP (estado atual do Harness)** | agent-harness | JSON-RPC sobre stdio (`--acp`/`acp`); `AcpSessionClient` + dialetos V1/V2 | O mais estruturado dos três, mas **só funciona onde a CLI implementa ACP**. |

**Gaps confirmados no código atual:**

1. `AgentType` enum tem **14 valores** (Devin, Claude, Codex, OpenCode, OpenHands, Antigravity, Kimi, Grok, Aider, Cline, Continue, Copilot, Qwen, Kiro) mas `AgentDiscoveryService.KnownAgents` só mapeia **6** — Kimi, Grok, Aider, Cline, Continue, Copilot, Qwen, Kiro nunca aparecem na UI.
2. `supportsSession` só é `true` para OpenCode/Claude/Codex/Devin — OpenHands e Antigravity são descobertos mas não abrem sessão interativa; CLIs sem ACP não têm caminho algum.
3. Não existe registro de CLI customizada — o usuário não pode declarar "minha CLI interna X" e usá-la no AI Code.
4. Agentes só rodam no mesmo processo/host do server — não há `docker exec` em containers em execução (o padrão `ContainerManager`/`DockerProcessSpawner` do claudito).
5. `claudito-releases` é apenas distribuição/instaladores (sem arquitetura nova a portar); seu único aprendizado relevante — install/update UX — já é coberto por `AgentCliInstallService`.

## 2. Scope

**In scope:**

1. **Catálogo de CLIs extensível:**
   - `AgentCliDefinition` persistido no banco via EF Core (padrão do harness — decisão Q2): `id` (`custom-<slug>`), `displayName`, `executable`, `argsTemplate` (tokens `{prompt}` e `{model}` — decisão Q1), `transport` (`acp` | `pty`), `modelFlag` (opcional), `versionArgs` (default `--version`), `enabled`.
   - `AgentDiscoveryService` passa a unir: os 14 `AgentType` mapeados (completar o mapeamento dos 8 faltantes) **+** defs customizadas — tudo filtrado por resolução no `PATH`.
   - CRUD de defs customizadas via endpoints + seção em Settings/Agents (form simples; reusa `AgentInstallDialog`/`AgentModelConfigDialog` como padrão visual).
2. **Transporte PTY no AI Code (modo terminal-thread):**
   - Novo tipo de sessão de agente: `transport = pty` reutiliza `IPtySession`/`PtySessionFactory`/`TerminalSessionManager` (que já implementa `ReattachAsync` + orphan handling — mesmo padrão de "session persistence + output buffer" do claude-code-web).
   - Threads `pty` aparecem na mesma lista do AI Code com badge `terminal`; o corpo da thread renderiza um xterm.js embed (reusa `taskboardTerminal` de `terminal.js`) em vez de bubbles/chat.
   - Input: caixa de envio escreve no PTY (texto + Enter); output ANSI renderizado direto — sem parsing estruturado, sem tool cards (limitação aceita e explícita na UI).
   - CLIs conhecidas sem ACP (OpenHands, Antigravity e demais do enum sem suporte ACP) ganham sessão interativa via PTY; CLIs customizadas escolhem o transporte na definição.
   - CLIs que **têm** ACP também podem abrir thread `pty` — o usuário escolhe o view mode `chat | terminal` na criação da thread (decisão Q3: terminal cru disponível para qualquer CLI, útil para flags/comandos que o ACP não expõe).
3. **Execução dentro de containers Docker (contexto de execução):**
   - `DockerCliSpawner` implementando a mesma abstração de spawn usada pelos transports: `docker exec -i <container> <cli> [args]` para PTY, e `docker exec` sem TTY para ACP-stdin/stdout.
   - Descoberta opcional por container: `GET /api/agents/docker/containers` (via `docker ps`) + probe `docker exec <c> which <cli>` para marcar CLIs disponíveis por container.
   - Seletor de contexto na thread: `host` (default) | `container:<name>` — visível apenas quando o daemon Docker está acessível (`docker info`); degradado gracioso sem Docker.
4. **Sessão persistente/reattach:** thread `pty` sobrevive a refresh do browser (reattach via `TerminalSessionManager.ReattachAsync` + ring buffer de output no `PtySession`, análogo ao `session-store` do claude-code-web).

**Out of scope:**

- Parser stream-json por CLI (dialetos não-ACP como o `stream-handler` do claudito) — ACP já cobre o caso estruturado; PTY cobre o resto. Novos dialetos estruturados são SPEC futura.
- Multi-container/orquestração de containers (criar/destruir containers por projeto como o `ContainerManager` completo do claudito) — aqui apenas `docker exec` em containers **já em execução**.
- Multi-browser attach simultâneo à mesma thread pty (reattach single-client basta).
- Ralph Loop / run-configs / Slack do claudito — sem equivalente no escopo.
- Auto-aceite de "trust prompt" por varredura de output (hack do claude-code-web) — desnecessário: flags de sandbox/permissão já são passadas no spawn.

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Domain`/`Domain.Shared`: `AgentCliDefinition` (aggregate persistido via EF Core — decisão Q2), `CliTransport` enum (`Acp`, `Pty`), `AgentInfo` + `Transport`/`Context` fields.
- `Taskboard.Integrations/Agents`: `AgentDiscoveryService` (14 enum entries + merge com defs customizadas), novo `DockerCliDiscovery` (docker ps/exec probes), `AgentCliInvocation` (args por transporte), `StreamingProcessRunner`/`PtySessionFactory` — ponto de injeção para spawner docker (`docker exec` em vez de spawn local).
- `Taskboard.Integrations/Terminal`: reuso direto — `PtySession` precisa aceitar comando/args arbitrários (hoje spawna shell); `TerminalSessionManager` ganha vínculo `threadId → sessionId` para reattach.
- `Taskboard.Server`: `AgentSessionManager` roteia por transporte (ACP atual vs PTY novo); `TerminalHub` ou hub paralelo para threads pty; endpoints CRUD de custom CLIs + `agents/docker/containers`.
- `Taskboard.Blazor`: `AiChat.razor` — thread kind `chat|terminal`; componente `AiChat/PtyThreadPane.razor` embed xterm; `NewThreadDialog` + seletor de contexto docker; Settings → Agents: CRUD custom CLIs.
- `Taskboard.Application(.Contracts)`: `AiChatThread` + `Transport`/`ContainerContext`; DTOs de custom CLI e docker container.

**Files to read before implementing:**

- `src/Taskboard.Integrations/Agents/AgentDiscoveryService.cs`, `AgentCliInvocation.cs`, `KnownCliAgentAdapter.cs`, `LocalCliAgentAcpClient.cs`
- `src/Taskboard.Integrations/Terminal/IPtySession.cs`, `PtySession.cs`, `PtySessionFactory.cs`, `TerminalSessionManager.cs`
- `src/Taskboard.Server/Hubs/TerminalHub.cs`, `Services/AgentSessionManager.cs`, `Program.cs` (endpoints `/api/agents/*`)
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`, `Pages/Terminal.razor`, `wwwroot/js/terminal.js`
- `src/Taskboard.Domain/Entities/AiChatThread.cs`, `Domain.Shared/ValueObjects/AgentType.cs`
- Referência externa (leitura em /tmp): `/tmp/cli-ux-analysis/claude-code-web/src/claude-bridge.js`, `src/server.js` (protocolo de sessão); `/tmp/cli-ux-analysis/claudito/src/agents/agent.ts`, `services/docker/docker-service.ts`

**Files to create or modify:**

```text
src/Taskboard.Domain.Shared/ValueObjects/CliTransport.cs                    # new
src/Taskboard.Domain/Entities/AgentCliDefinition.cs                         # new (ou config JSON)
src/Taskboard.Integrations/Agents/AgentDiscoveryService.cs                  # modified — 14 tipos + custom merge
src/Taskboard.Integrations/Agents/DockerCliDiscovery.cs                     # new
src/Taskboard.Integrations/Agents/DockerCliSpawner.cs                       # new — docker exec spawner
src/Taskboard.Integrations/Terminal/PtySession.cs                           # modified — comando/args arbitrários
src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs               # modified — vínculo threadId
src/Taskboard.Server/Services/AgentSessionManager.cs                        # modified — roteamento por transport
src/Taskboard.Server/Program.cs                                             # modified — endpoints custom-cli + docker
src/Taskboard.Application.Contracts/Dtos/AgentCliDefinitionDto.cs           # new
src/Taskboard.Application.Contracts/Dtos/DockerContainerDto.cs              # new
src/Taskboard.Domain/Entities/AiChatThread.cs                               # modified — Transport, ContainerContext
src/Taskboard.Blazor/Components/AiChat/PtyThreadPane.razor                  # new — xterm embed
src/Taskboard.Blazor/Components/Pages/AiChat.razor                          # modified — render por thread kind
src/Taskboard.Blazor/Components/Pages/Settings.razor                        # modified — CRUD custom CLIs (ou Agents.razor)
tests/Taskboard.Tests.Unit/Agents/CustomCliDiscoveryTests.cs                # new
tests/Taskboard.Tests.Unit/Agents/DockerCliSpawnerTests.cs                  # new
tests/Taskboard.Tests.Integration/AgentCliDefinitionEndpointsTests.cs       # new
```

## 4. Requirements

### RF-001: Descoberta completa + custom CLIs

- **Description:** `GET /api/agents` retorna todos os `AgentType` cujo executável resolve no PATH (mapeando os 14 valores do enum) mais cada `AgentCliDefinition` habilitada e resolvível.
- **Rules:** Executável ausente → `AgentStatus.Unavailable` (hoje: omitido/Unavailable parcial); defs customizadas inválidas (executable vazio) são rejeitadas no CRUD com 400.
- **Input → Output:** `GET /api/agents` → lista unificada com `transport` e `source` (`builtin`|`custom`) por agente.

### RF-002: CRUD de CLI customizada

- **Description:** `GET/POST/PUT/DELETE /api/agents/custom` gerencia defs persistidas; campos: `displayName`, `executable`, `argsTemplate` (tokens `{prompt}`, `{model}`), `transport`, `modelFlag?`, `enabled`.
- **Rules:** `transport=acp` exige que a CLI implemente ACP (validação best-effort: probe `--help` contém `acp`? — documentar como warning, não bloqueio); `displayName` único; `id` gerado `custom-<slug>`.
- **Input → Output:** POST body válido → `201` def; duplicado → `409`.

### RF-003: Thread terminal (transport PTY)

- **Description:** `CreateAiChatThreadRequest` aceita `transport=pty`; a thread spawna a CLI via `IPtySession` no workdir da thread; input do usuário é escrito no PTY; output flui por SignalR para o xterm embed.
- **Rules:** Sessão pty por thread é única e reattachable após refresh; fechar a thread não mata a sessão automaticamente (mesmo comportamento de tabs do Terminal: orphan→reattach); resize propaga cols/rows.
- **Input → Output:** prompt + keystrokes → PTY → output ANSI renderizado no pane da thread.

### RF-004: Contexto Docker

- **Description:** Quando o daemon Docker está acessível, `GET /api/agents/docker/containers` lista containers em execução e a thread pode ser criada com `containerContext=<name>`; o spawn vira `docker exec -it <name> <cli> args` (PTY) ou `docker exec -i <name>` (ACP).
- **Rules:** Sem daemon → seletor oculto e campo ignorado (não erro); probe de CLI por container marca `availableIn` por container.
- **Input → Output:** `containerContext` → processo dentro do container, mesmo protocolo de I/O.

### RF-005: Compat/ segurança

- **Description:** Nenhuma thread ACP existente muda de comportamento; defs customizadas e contexto docker nunca executam sem ação explícita do usuário.
- **Rules:** `docker exec` usa exatamente o executável declarado — sem shell interpolation (array de args); log sem segredos.

**Business rules / invariants:**

- Uma thread tem exatamente um `transport` (imutável após criação).
- `transport=pty` implica render terminal — não gera `AiChatEvent` estruturados de tool_use/permission.
- Custom CLI desabilitada não aparece em pickers mas threads existentes dela permanecem legíveis.

## 5. API Contract

**Endpoint:** `GET /api/agents/custom` · `POST /api/agents/custom` · `PUT /api/agents/custom/{id}` · `DELETE /api/agents/custom/{id}`
**Auth:** sessão admin existente (mesmo esquema dos demais endpoints `/api/agents/*`).

**Request (POST/PUT):**
```json
{
  "displayName": "Minha CLI",
  "executable": "mycli",
  "argsTemplate": "--acp {model}",
  "transport": "pty",
  "modelFlag": "--model",
  "versionArgs": "--version",
  "enabled": true
}
```

**Response (success):**
```json
{ "id": "custom-minha-cli", "displayName": "Minha CLI", "transport": "pty", "enabled": true, "resolved": true }
```

**Endpoint:** `GET /api/agents/docker/containers` → `[{ "name": "dev", "image": "…", "availableClis": ["claude","opencode"] }]`; `503` quando daemon indisponível.

**Expected errors:** `400` validação · `404` def/container inexistente · `409` nome duplicado · `503` docker indisponível — formato de erro genérico existente.

## 6. Acceptance Criteria

- [ ] **Given** uma CLI do enum sem mapeamento hoje (ex.: `aider` instalado) **when** `GET /api/agents` **then** ela aparece com `transport` adequado e status real.
- [ ] **Given** uma def custom `transport=pty` salva **when** crio uma thread dela no AI Code **when** envio texto **then** o output ANSI da CLI aparece no pane xterm da thread.
- [ ] **Given** uma thread pty aberta **when** dou refresh no browser **then** a sessão reattacha e o scrollback reaparece.
- [ ] **Given** Docker rodando com um container `dev` contendo `claude` **when** crio thread com `containerContext=dev` **then** a CLI executa dentro do container.
- [ ] **Given** daemon Docker ausente **when** abro o seletor de contexto **then** só "host" aparece, sem erro.
- [ ] **Given** thread ACP existente **when** reabro **then** comportamento idêntico ao de hoje (regressão zero).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Custom CLI com executável inexistente | POST custom | `201` mas `resolved:false`, `Unavailable` na descoberta |
| Nome duplicado | POST custom mesmo `displayName` | `409` |
| Container parado entre seleção e spawn | `containerContext=dev` | Erro claro na thread (`system` event) sem crash |
| CLI pty que morre | process exit | Pane mostra exit code; botão "restart" opcional |
| Thread pty com resize | drag do pane | `ResizeAsync(cols,rows)` propaga ao PTY |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** ler arquivos da seção 3 (incl. referências em `/tmp/cli-ux-analysis/`) e mapear pontos de injeção de spawn.
- [ ] **T2 — Model + discovery:** `CliTransport`, `AgentCliDefinition`, enum mapping completo, merge custom no `AgentDiscoveryService` + endpoints CRUD.
- [ ] **T3 — PTY transport:** `PtySession` com comando arbitrário, `AgentSessionManager` routing, hub/reattach, `PtyThreadPane.razor`.
- [ ] **T4 — Docker context:** `DockerCliDiscovery`, `DockerCliSpawner`, seletor de contexto.
- [ ] **T5 — Verification:** testes unitários (discovery merge, args build, docker cmd construction) + integração (endpoints); bUnit para o pane; `dotnet build` + `dotnet test` completos.
- [ ] **T6 — Validation:** DoD (section 9); smoke manual documentado (thread pty com `bash` como custom CLI é o teste de fumaça mínimo — funciona sem depender de CLI real de agente).
- [ ] **T7 — Done + PR:** `Status = Done`, PR na branch `feature/devin-20260928-ai-code-generic-cli`.

**7.1 Validation strategy**

- `.NET`: testes unitários de regras (discovery merge, template de args, docker cmd escaping — sem docker real via fake spawner); integração para endpoints; cobertura ≥ gate `COVERAGE_THRESHOLD` vigente (ratchet).
- Smoke manual obrigatório documentado no PR: thread pty com custom CLI `bash`, thread pty com `claude`, thread ACP regression.

## 8. Organization Guardrails

- **Branches:** `feature/devin-20260928-ai-code-generic-cli`; nunca `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` intocado.
- **Security:** args arrays sem shell interpolation; não logar envs/credenciais de CLI; `docker exec` restrito a containers existentes (sem `docker run`/pull nesta SPEC).
- **Scope:** dialetos stream-json fora; criação de containers fora.
- **Architecture:** regra de negócio no Application/Domain; spawn mechanics em Integrations; Razor só renderiza.

## 9. Definition of Done

- [ ] RF-001 a RF-005 implementados.
- [ ] Acceptance criteria cobertos por testes/evidência.
- [ ] Edge cases tratados.
- [ ] `dotnet build` limpo, `dotnet test` verde, coverage ≥ gate.
- [ ] Guardrails respeitados; logs sem segredos.
- [ ] SPEC-20260921-ai-chat-cli-backend atualizada se contratos de thread mudarem.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/devin-20260928-ai-code-generic-cli`.

## Open Questions / Pending Ambiguity — RESOLVIDAS (aprovado pelo usuário)

- `Q1` — **Sim:** `argsTemplate` aceita `{prompt}` (one-shot) e `{model}`.
- `Q2` — **EF/DB:** defs customizadas persistidas via EF Core, consistente com o resto do harness.
- `Q3` — **Sim:** seletor "view mode: chat | terminal" por thread; PTY disponível também para CLIs com ACP.
