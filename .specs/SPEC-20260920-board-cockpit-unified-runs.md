# SPEC-20260920-board-cockpit-unified-runs

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `board-cockpit-unified-runs` |
| Type | `Feature` (unificação Board → PipelineEngine → Cockpit + terminal de run) |
| Stack | `.NET 10 / ASP.NET Core / SignalR / Blazor WASM / xterm.js` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | — |
| Ticket | [#257](https://github.com/afonsoft/agent-harness/issues/257) — user request (board↔cockpit unification) |
| Status | `Done` |

---

## 1. User Story

**As a** usuário do Board e do Cockpit
**I want** que todo agente disparado pelo Board rode como pipeline run no cockpit — com link direto, escolha de Agent CLI no "New Run" e terminal de logs
**So that** existe uma única superfície de execução (worktree + eventos + FinOps + approvals) em vez de dois fluxos paralelos.

### Problem Context

Hoje existem **dois caminhos de execução**:

1. **Board** (`KanbanBoard` → `AgentSelectionModal` → `IAgentOrchestrationService.EnqueueAsync`): cria `AgentRun` legado, streama logs via `/agent-log-hub`, não gera worktree de pipeline, não emite eventos de cockpit, não aparece em `/cockpit`.
2. **Cockpit** (`POST /api/harness/runs` → `PipelineEngine`): fluxo completo — worktree isolado, timeline de eventos, approval gates, verification, FinOps, steer, create-PR.

O usuário quer: runs do Board **são** pipeline runs (aparecem no cockpit, seguem o fluxo completo), o Board ganha link direto para a sessão do run, o modal "New Run" permite escolher o Agent CLI (e pré-preenche o repositório do combo global), e o cockpit oferece uma visão de terminal com os logs do agente — interativo se possível.

### Decisões já tomadas (entrevista)

- **Q1**: Board vira cliente do `PipelineEngine`; `AgentRun` legado permanece read-only (histórico/runs antigas), nenhuma run nova é criada por ele.
- **Q2**: novo template `single-agent` (1 estágio `AgentWork` + 1 `Verification` opcional); o combo Agent CLI habilita apenas com `single-agent` selecionado. Templates fixos mantêm os agentes do próprio spec.
- **Q3**: aba "Terminal" read-only (xterm.js) como requisito principal; PTY interativo com stdin no processo do agente é **stretch goal** (`se possível`).

---

## 2. Scope

### In scope

- **Template `single-agent`**: `PipelineTemplates` ganha definição `single-agent` — estágio `builder` (`AgentWork`, agente parametrizável) + estágio `verifier` (`Verification`, opcional via flag). O template declara o agente default (`Codex/Normal`); o agente efetivo vem do request (`AgentType` + `ModelTier` no `PipelineStartRequest`).
- **Contrato**: `PipelineStartRequest` ganha `AgentType? AgentOverride`, `AgentModelTier? TierOverride`, `bool SkipVerification = false`. Aplicável somente a `single-agent` — `400` se override enviado para outro template.
- **Cockpit "New Run"**:
  - Combo **Agent CLI** (`GetAvailableAgentsAsync` — somente `Available`), habilitado somente quando `TemplateId == "single-agent"`; inclui seletor de tier.
  - Checkbox "Run verification stage" (default on) quando `single-agent`.
  - `Repository` **pré-preenchido** com `SelectedRepositoryService` (mantém edição livre).
- **Board unificado**: `KanbanBoard`/`AgentSelectionModal` deixa de chamar `EnqueueAsync`; converte a seleção em `PipelineStartRequest` (`single-agent`, agente+tier escolhidos, `IssueId` da issue, `InitialPrompt` = prompt atual do board) → `POST /api/harness/pipelines/start` → recebe `PipelineExecutionId`.
- **Link direto**: após disparar, o Board exibe toast/cta **"Open in Cockpit"** → `/cockpit/runs/{id}`; a linha de run na UI do board ganha link para a sessão correspondente.
- **Terminal da run** (`/cockpit/runs/{id}`): nova aba/seção "Terminal" que renderiza os eventos `agent_output` do run numa view xterm.js read-only (stream, ANSI colors, auto-scroll, reattach = replay do buffer de eventos já persistido/reamostrado via REST replay).
- **Issue history**: run iniciada pelo Board registra `IssueHistoryEvent` vinculando issue ↔ `PipelineExecutionId` (preserva a timeline unificada por issue).

### Out of scope

- Remover/migrar o `AgentOrchestrationService` e entidade `AgentRun` (read-only legado; remoção fica para spec futura).
- PTY interativo com stdin no processo do agente (stretch — ver §8 Riscos).
- Editar agentes/tiers por estágio em templates multi-stage.
- MCP/`taskctl` para pipeline runs (já coberto pelo fluxo REST existente).

---

## 3. Technical Context

- `src/Taskboard.Application/Harness/PipelineTemplates.cs` — adicionar `SingleAgent` template.
- `src/Taskboard.Application.Contracts/Harness/PipelineDtos.cs` — `PipelineStartRequest` + overrides.
- `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs` — aplicar overrides ao instanciar estágios; validação 400.
- `src/Taskboard.Server/Program.cs` — endpoint já existe (`/api/harness/pipelines/start`); apenas pass-through dos novos campos.
- `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — combo Agent CLI + checkbox verification + prefill `SelectedRepositoryService`.
- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` + `AgentSelectionModal.razor` — trocar `EnqueueAsync` → `Client.StartRunAsync`; toast com link.
- `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` + novo `Components/Cockpit/RunTerminal.razor` — aba terminal xterm.js (reusar init do `/terminal` com `readOnly`).
- `src/Taskboard.Blazor/Services/TaskboardClient.cs` — `StartRunAsync` já existe; estender request.
- `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` — `IssueId` já existe; registrar `IssueHistoryEvent` via app service (GitHub module) ou evento.
- JS: reuso de `terminal.js`/xterm.js init; nenhum addon novo.

---

## 4. Requirements

| # | Requisito | Verificação |
|---|---|---|
| R1 | `POST /api/harness/pipelines/start` aceita `AgentOverride`/`TierOverride`/`SkipVerification` somente para `single-agent`; override em outro template → `400` | integration test |
| R2 | `single-agent` cria execução com 1 `AgentWork` (agente = override ou default Codex) + 1 `Verification` (omitido quando `SkipVerification`) | unit test do app service |
| R3 | Cockpit modal pré-preenche repo do `SelectedRepositoryService` e habilita combo CLI só em `single-agent` | bUnit/manual |
| R4 | Board "Queue agent" cria pipeline run e retorna id; UI mostra link `/cockpit/runs/{id}` | integration + manual |
| R5 | Runs do board aparecem na lista `/cockpit` com template `single-agent` | manual |
| R6 | Aba Terminal na run renderiza `agent_output` (stdout/stderr) em xterm read-only com replay para late-join | manual |
| R7 | `IssueHistoryEvent` registra vínculo issue ↔ pipeline run | integration test |
| R8 | Execução passa por worktree, events, FinOps e approvals do fluxo padrão — sem caminho paralelo | revisão |

---

## 5. API Contract

```http
POST /api/harness/pipelines/start
{
  "templateId": "single-agent",
  "repositoryFullName": "owner/repo",
  "repositoryPath": "/home/ubuntu/repos/repo",
  "baseBranch": "main",
  "issueId": "<issue-guid|null>",
  "initialPrompt": "<prompt>",
  "agentOverride": "Codex|Claude|OpenCode|Devin|...",
  "tierOverride": "Lite|Normal|Ultra",
  "skipVerification": false,
  "maxBudgetUsd": null
}
→ 200 { pipelineExecutionId, ... } | 400 override-em-template-fixo | 404 repo inválido
```

`GET /api/harness/pipelines` passa a incluir `single-agent` com `stageKeys: ["builder", "verifier"]`.

---

## 6. Acceptance Criteria (BDD)

- **AC1 — Board → cockpit**: dado que clico "Run agent" numa issue do Board, escolho Claude/Normal e confirmo, então uma `PipelineExecution` `single-agent` é criada, recebo link "Open in Cockpit", e o run aparece em `/cockpit` e em `/cockpit/runs/{id}` com timeline ao vivo.
- **AC2 — Combo CLI**: dado `TemplateId = single-agent` no modal, o combo Agent CLI lista só agentes `Available`; com outro template o combo fica desabilitado.
- **AC3 — Repo prefill**: o campo Repository abre com o valor do combo global e aceita edição.
- **AC4 — Override validation**: `agentOverride` com `templateId != single-agent` → `400`; sem override → estágio usa default do template.
- **AC5 — Terminal read-only**: a aba Terminal exibe o stream do agente em tempo real e, recarregando a página, repete o histórico (replay de eventos).
- **AC6 — Fluxo completo**: run do board passa por worktree isolation, events no hub, métricas FinOps e, se aplicável, approval/verification — idêntico a um run iniciado pelo cockpit.
- **AC7 — SkipVerification**: checkbox off → execução tem apenas o estágio builder.

---

## 7. Task Plan

- [x] **T1 — Contrato+template**: `PipelineTemplates.SingleAgent`, novos campos em `PipelineStartRequest`, mapeamento no `PipelineExecutionAppService` + validação 400 + testes unitários.
- [x] **T2 — Board unificado**: `AgentSelectionModal`/`KanbanBoard` → `StartRunAsync`, toast+link `/cockpit/runs/{id}`, `IssueHistoryEvent` vínculo; testes de integração.
- [x] **T3 — Cockpit modal**: combo Agent CLI + tier + checkbox verification (condicionais a `single-agent`) + prefill `SelectedRepositoryService`.
- [x] **T4 — RunTerminal**: componente xterm.js read-only na página de run; replay de `agent_output`; wiring dos eventos live.
- [x] **T5 — Docs+verificação**: `docs/api.md`/`features.md` (en+pt-br), SPEC → `Done`, suíte verde.

## 8. Organization Guardrails

- Branch `feature/devin-20260920-board-cockpit-unified-runs` a partir de `main`; PR → squash merge.
- Sem secrets/logs sensíveis; outputs do agente passam pelo pipeline existente (scrub já aplicado a logs persistidos).
- Não remover `AgentRun`/`AgentOrchestrationService` neste PR (legado read-only).
- Hard rules: testes junto, `dotnet build` 0 warnings, cobertura ≥ gate.

## 9. Definition of Done

- [ ] R1–R8 implementados com testes.
- [ ] AC1–AC7 verificados (inclui smoke manual board→cockpit→terminal).
- [ ] Docs atualizadas en+pt-br; SPEC `Done`; PR mergeado.

## 10. Riscos & Open Questions

1. **PTY interativo (stretch)**: `LocalCliAgentAcpClient` usa pipes (stdout/stderr redirect). Terminal com stdin exigiria hospedar o agente num PTY (`TerminalHub`-like) — redesenho do adapter; fica fora do escopo como stretch documentado.
2. `RepositoryPath` é exigido pelo request hoje — o board precisa resolver path local a partir do `owner/repo` selecionado (mesma resolução usada pelo modal do cockpit/terminal).
3. Runs legacy `AgentRun` ainda aparecem na aba Logs/histórico do board — manter até spec de deprecação.
