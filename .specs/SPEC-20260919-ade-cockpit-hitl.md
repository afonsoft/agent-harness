# SPEC-20260919-ade-cockpit-hitl

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ade-cockpit-hitl` |
| Type | `Feature` (Frontend & UI / Human-in-the-Loop Cockpit) |
| Stack | `Blazor WebAssembly / Blazor.Bootstrap / SignalR / Monaco or Diff2Html / C# 14` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-ade-cockpit-hitl` |
| Ticket | [#170 — E15](https://github.com/afonsoft/taskboard-ai/issues/170) |
| Status | `Approved` |

---

## 1. User Story

**As a** desenvolvedor, arquiteto ou líder técnico
**I want** um Cockpit unificado de controle e observabilidade no Blazor WASM (`/cockpit` ou `/runs/{id}`)
**So that** eu possa acompanhar a execução de equipes de agentes de IA em tempo real com eventos estruturados (pensamento, tool calls, logs), inspecionar diffs de código lado a lado, interagir com gates de aprovação de segurança (HITL) e direcionar o trabalho (steer/pause/cancel) sem alternar para ferramentas externas.

### Problem Context

A interface atual do `taskboard-ai` disponibiliza apenas:
1. Um terminal de logs de texto cru (`TaskLogTab.razor`) em console escuro.
2. Nenhuma separação visual entre raciocínio do modelo (*reasoning/thought*), invocação de ferramentas (*tool calls* com inputs/outputs estruturados) e ações no sistema de arquivos.
3. Não há um visualizador de Git Diff integrado no browser: para saber o que o agente modificou no código, o usuário é forçado a abrir o VS Code Web (`/vscode/`) ou rodar `git diff` no terminal manualmente.
4. Falta de controles de intervenção refinados: o usuário só tem o botão de "Cancelar", sem capacidade de "Steer" (enviar instrução corretiva enquanto o agente trabalha), pausar ou inspecionar passos intermediários.

---

## 2. Scope

### In scope

- **Cockpit Page (`/cockpit` e `/cockpit/runs/{id}`):**
  - Timeline de execução em tempo real alimentada via SignalR (`HarnessCockpitHub`).
  - Renderização de blocos tipados:
    - `ThoughtBlock`: raciocínio e intenções do agente (com colapso/expansão).
    - `ToolCallCard`: nome da tool (ex: `bash`, `read`, `write`, `edit`), parâmetros formatados em JSON/código, saída e status (`Running`, `Success`, `Error`).
    - `VerificationCard`: status dos testes, build e cobertura.
- **Visualizador de Git Diff Integrado:**
  - Componente Blazor `GitDiffViewer` capaz de renderizar side-by-side ou unified diff com syntax highlighting e contagem de linhas adicionadas/removidas.
- **Painel de Controle e Intervenção (HITL):**
  - **Steer Input:** campo de prompt rápido para interjeição do usuário (enviar instrução sem matar a sessão).
  - **Run Action Buttons:** Pause, Resume, Stop (Cancel), e Create Pull Request.
  - **Approval Dialog / Permission Prompt:** Card interativo para aprovar ou rejeitar operações de alto risco (ex: execução de comando `rm`, alteração de migrations ou push remoto) com opções `Allow Once`, `Allow Always (session)` e `Deny`.
- **Métricas de FinOps no cabeçalho:**
  - Contador de tokens gastos (Input/Output/Total), custo estimado em USD e tempo decorrido do run.

### Out of scope

- Editor completo de código com autocomplete (essa função é desempenhada pelo code-server em `/vscode/`).
- Criação visual de diagramas de fluxo de agentes em canvas (foco nesta fase é o cockpit de execução, timeline e diffs).

---

## 3. Technical Context

### Where the change happens

- **Blazor WASM:** `Taskboard.Blazor/Pages/Cockpit/`, `Taskboard.Blazor/Components/Cockpit/`.
- **Contracts:** Eventos estruturados em `Taskboard.Application.Contracts/Harness/Events/`.
- **Server:** SignalR Hub `HarnessCockpitHub.cs` e streaming de eventos por canal de run.

### Files to read before implementing

- `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Server/Hubs/AgentLogHub.cs`
- `.specs/SPEC-20260919-web-cli-agent.md`

### Files to create or modify

```text
src/Taskboard.Application.Contracts/Harness/Events/CockpitEventDto.cs  [new]
src/Taskboard.Server/Hubs/HarnessCockpitHub.cs                        [new]
src/Taskboard.Blazor/Pages/Cockpit/CockpitPage.razor                 [new]
src/Taskboard.Blazor/Pages/Cockpit/RunDetailsPage.razor              [new]
src/Taskboard.Blazor/Components/Cockpit/RunTimeline.razor            [new]
src/Taskboard.Blazor/Components/Cockpit/ToolCallCard.razor           [new]
src/Taskboard.Blazor/Components/Cockpit/ThoughtBlock.razor           [new]
src/Taskboard.Blazor/Components/Cockpit/GitDiffViewer.razor          [new]
src/Taskboard.Blazor/Components/Cockpit/ApprovalGateModal.razor      [new]
src/Taskboard.Blazor/Components/Cockpit/RunControlBar.razor          [new]
src/Taskboard.Client/wwwroot/js/diff-viewer.js                       [new]
```

---

## 4. Requirements

### RF-001: Timeline de Eventos Estruturados
- **Description:** O Cockpit deve receber eventos do SignalR e renderizá-los em ordem cronológica com visual limpo e moderno.
- **Rules:** Tool calls devem exibir parâmetros de entrada recolhidos por padrão, com botão para expandir e ver payload completo.
- **Input → Output:** Stream de `CockpitEventDto` → Cards dinâmicos na tela com rolagem suave.

### RF-002: Visualização de Diffs em Tempo Real
- **Description:** O usuário pode alternar para a aba "Diff" a qualquer momento para ver o código que o agente modificou no worktree até aquele instante.
- **Rules:** O diff deve ser atualizado automaticamente a cada conclusão de etapa ou sob demanda via botão "Refresh Diff".
- **Input → Output:** `GET /api/harness/worktrees/{runId}/diff` → Renderização com realce de sintaxe em `GitDiffViewer`.

### RF-003: Controles de Steer e Intervenção
- **Description:** Permitir ao usuário digitar uma mensagem de instrução que é enviada diretamente ao agente em execução no worktree.
- **Rules:** Se o agente suportar ACP/steer, a instrução é injetada em tempo real; caso contrário, é enfileirada para o próximo turno.
- **Input → Output:** Texto de steer digitado → `POST /api/harness/runs/{id}/steer` → Feedback visual de envio no timeline.

### RF-004: Gate de Decisão Humana (HITL)
- **Description:** Quando o pipeline atinge um estágio `RequiresHumanApproval` ou o agente solicita permissão de segurança, o Cockpit exibe com destaque o modal/card de aprovação.
- **Rules:** O cronômetro ou execução fica pausado até que o usuário clique em `Aprovar` ou `Recusar`. A recusa permite ao usuário fornecer o motivo.
- **Input → Output:** Evento `ApprovalRequired` → Alerta no topo do Cockpit com botões de ação.

### RF-005: Ação de Conclusão / Pull Request
- **Description:** Quando o run atinge status `Completed` e as verificações forem verdes, o Cockpit habilita o botão "Criar Pull Request".
- **Rules:** Ao clicar, o sistema comita o worktree (se pendente), envia o push para o remote e abre o PR via Octokit no repositório GitHub correspondente, exibindo o link resultante.

---

## 5. API Contract

SignalR Hub: `/harness-cockpit-hub`

| Direction | Method | Payload |
|---|---|---|
| client → server | `JoinRunGroup(string runId)` | — |
| client → server | `LeaveRunGroup(string runId)` | — |
| server → client | `ReceiveCockpitEvent(CockpitEventDto)` | `{ runId, timestamp, kind, title, payloadJson }` |
| server → client | `RequireApproval(ApprovalRequestDto)` | `{ runId, requestId, title, description, options }` |

Endpoints REST de Ação:
```http
POST /api/harness/runs/{id}/steer
{ "instruction": "Corrija a formatação do método X" }
→ 202 Accepted

POST /api/harness/runs/{id}/approvals/{requestId}
{ "action": "Allow", "comment": "Aprovado sem ressalvas" }
→ 200 OK

POST /api/harness/runs/{id}/create-pr
{ "title": "feat: nova funcionalidade", "body": "Implementado via Harness ADE Run #123" }
→ 201 Created { "prUrl": "https://github.com/..." }
```

---

## 6. Acceptance Criteria

- [ ] **Given** um run em andamento, **when** o usuário abre a página `/cockpit/runs/{id}`, **then** a timeline conecta ao SignalR e exibe os eventos anteriores e os novos em tempo real.
- [ ] **Given** tool calls executadas pelo agente, **when** renderizadas no Cockpit, **then** exibem ícones distintos (terminal para bash, arquivo para read/write), tempos de execução e código formatado.
- [ ] **Given** arquivos modificados no worktree, **when** o usuário seleciona a aba "Diff", **then** visualiza os arquivos modificados e o diff com cores verde (adições) e vermelho (remoções).
- [ ] **Given** uma requisição de aprovação disparada pelo agente, **when** o modal é exibido, **then** o clique em "Aprovar" destrava a execução do pipeline.
- [ ] **Given** run finalizado com sucesso, **when** o usuário clica em "Criar Pull Request", **then** o PR é aberto no GitHub e o link é exibido.

**Edge cases:**

| Scenario | Input | Expected behavior |
|---|---|---|
| Queda de conexão SignalR | Reconexão de rede | O cliente Blazor reconecta automaticamente com exponential backoff e recarrega os eventos perdidos via API REST. |
| Diff muito grande (> 2MB) | Edição em arquivos de lock ou binários | O componente trunca e exibe aviso amigável: "Diff muito grande para renderização completa, listando resumo dos arquivos". |

---

## 7. Task Plan

- [ ] **T1 — Contracts & SignalR Hub:** Criar `CockpitEventDto`, `ApprovalRequestDto` e implementar `HarnessCockpitHub`.
- [ ] **T2 — Diff Viewer Component:** Implementar componente Blazor interativo para renderização de git diff com realce de sintaxe.
- [ ] **T3 — Timeline & Event Cards:** Desenvolver `RunTimeline`, `ToolCallCard`, `ThoughtBlock` e badges de status.
- [ ] **T4 — Intervenção & Controles:** Implementar `RunControlBar` com Steer, Pause, Cancel e modal de aprovação.
- [ ] **T5 — Navegação & Integração:** Adicionar rota no `NavMenu` (`/cockpit`), integrar com o Kanban e validar o fluxo completo.

---

## 8. Organization Guardrails

- Design responsivo compatível com desktop e telas menores (mobile-first para layout, visualização ampla para diffs).
- Zero lógica de negócio nos componentes Blazor; toda orquestração é mediada por serviços e chamadas de API autenticadas.
- Preservar acessibilidade (ARIA labels e navegação por teclado).

---

## 9. Definition of Done

- [ ] Cockpit funcional acessível em `/cockpit`.
- [ ] Streaming de eventos ao vivo via SignalR validado.
- [ ] Visualização de diffs de worktrees funcionando com dados reais.
- [ ] Ações de Steer, Aprovação e Criação de PR operacionais.
