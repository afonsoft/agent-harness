# SPEC-20260929-ai-chat-capabilities

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-chat-capabilities` |
| Type | `Bugfix / Application + Blazor` |
| Stack | `.NET 10 / ACP / PTY` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260929-ai-chat-capabilities` |
| Ticket | [#369](https://github.com/afonsoft/agent-harness/issues/369) (Epic [#366](https://github.com/afonsoft/agent-harness/issues/366)) |
| Status | `Approved` |
| Related | `SPEC-20260928-ai-code-generic-cli` (PR #353) |

## 1. User Story

**As a** usuário do AI Code
**I want** que o modo Chat só seja oferecido a CLIs com suporte ACP, que threads com definição customizada executem, e que o fork preserve a configuração da conversa
**So that** a UI não prometa caminhos que o servidor recusa e threads derivadas mantenham CLI/contexto.

**Problem context:**

Achados do Devin Review no PR #353 (confirmados no código):

1. 🔴 **Chat oferecido para CLI sem ACP** — o seletor marca `SupportsChat=true` para qualquer agente elegível, mas `TransportFor` (`AgentDiscoveryService.cs:~89-90`) define PTY para agentes sem ACP. Criar Chat com Aider → thread ACP que `EnsureSessionAsync` não consegue servir.
2. 🔴 **Thread com CLI customizada não executa** — definição custom ACP pode ser escolhida na UI; a criação guarda `AgentCliId` mas deixa `AgentType` nulo. `EnqueuePromptAsync` (`AgentSessionManager.cs:~195`) recusa prompt sem `AgentType`; `StartRunAsync`/`ExecuteRunAsync` (`AiChatService.cs:~328-423`) tentam migrar para builtin e não despacham para a definição custom.
3. 🟡 **Fork perde CLI e contexto** — `ForkThreadAsync` (`AiChatService.cs:~653-671`) não copia `AgentCliId`/`ContainerContext`/`Transport`; o fork volta a ACP no host sem CLI vinculada.

## 2. Scope

**In scope:**

- `SupportsChat` calculado pela capacidade ACP real (mapa de transporte), na UI; rejeição server-side de thread `mode=Chat`/ACP para CLI sem suporte.
- Execução de threads com `AgentCliId` custom: decidir e implementar o dispatch (transporte da definição) ou rejeitar criação de modo incompatível com mensagem clara — cobrir `EnqueuePromptAsync`/`StartRunAsync`/`ExecuteRunAsync`.
- `ForkThreadAsync` copiar `Transport`, `AgentCliId`, `ContainerContext` (e demais campos imutáveis de configuração).
- Testes por correção.

**Out of scope:**

- Implementar ACP para CLIs que não o suportam (capability discovery profundo é futuro).
- Docker/ContainerContext execution — SPEC-20260929-docker-cli-context.

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` (~L610-645) — `SupportsChat`.
- `src/Taskboard.Application/AiChat/AiChatService.cs` — criação, `ForkThreadAsync`, `StartRunAsync`, `ExecuteRunAsync`.
- `src/Taskboard.Server/Services/AgentSessionManager.cs` — `EnqueuePromptAsync`/`EnsureSessionAsync` validação.
- `src/Taskboard.Domain/Entities/AiChatThread.cs` — campos de configuração (leitura).
- `src/Taskboard.Integrations/Agents/AgentDiscoveryService.cs` — `TransportFor` (leitura).

## 4. Requirements

### RF-001: `SupportsChat` = capacidade ACP

- **Description:** `SupportsChat` reflete `TransportFor(agentType) == Acp` (ou capability da definição custom); UI desabilita Chat para PTY-only; servidor rejeita com 400 claro.

### RF-002: Thread custom ACP executa ou é recusada na criação

- **Description:** Se a definição custom é ACP, o dispatch de prompt/run usa a definição (spawn pela spec custom); se o modo escolhido é incompatível, a criação falha explicitamente — nunca thread "morta" com `AgentType` nulo que passa na criação e falha no primeiro envio.

### RF-003: Fork preserva configuração

- **Description:** `ForkThreadAsync` copia `Transport`, `AgentCliId`, `ContainerContext` e campos correlatos da thread origem; fork de thread custom/container se comporta como a origem.

## 5. API Contract

Possível novo erro 400 na criação de thread (modo incompatível com a CLI) — documentar no endpoint e no changelog como correção de comportamento.

## 6. Acceptance Criteria

- [ ] **Given** agente PTY-only (ex.: Aider) **when** a UI monta o seletor **then** Chat está desabilitado; chamada direta à API retorna erro claro (testes UI-guard + servidor).
- [ ] **Given** definição custom ACP **when** uma thread Chat é criada e recebe prompt **then** o dispatch usa a definição (teste com session manager fake) **ou** a criação é recusada com erro claro — conforme decisão implementada.
- [ ] **Given** thread com `AgentCliId`/`ContainerContext` **when** é bifurcada **then** o fork carrega os mesmos campos (teste verde).
- [ ] **Given** build/test **then** verde.

## 7. Task Plan (agent execution)

- [ ] **T1 — SupportsChat:** capacidade real na UI + rejeição no servidor.
- [ ] **T2 — Custom dispatch:** execução ou rejeição explícita + testes.
- [ ] **T3 — Fork:** copiar campos + teste.
- [ ] **T4 — Done + PR.**

## 8. Organization Guardrails

- Validação de modo/transporte no servidor (Application/Domain), não só na UI.
- `.github/workflows/**` intocado.

## 9. Definition of Done

- [ ] Nenhum caminho UI→server produz thread inexecutável.
- [ ] Fork preserva configuração.
- [ ] Build/test verde; `Status = Done`.
