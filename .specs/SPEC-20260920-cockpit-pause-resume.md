# SPEC-20260920-cockpit-pause-resume

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cockpit-pause-resume` |
| Type | `Feature` (pipeline pause primitive + cockpit controls) |
| Stack | `.NET 10 / ASP.NET Core / SignalR / Blazor WASM` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | — |
| Ticket | [#250](https://github.com/afonsoft/agent-harness/issues/250) — GAP-implementation-cockpit-pause-resume (gap-analysis-20260920) |
| Status | `Done` |

---

## 1. User Story

**As a** operador do cockpit (`/cockpit/runs/{id}`)
**I want** pausar e resumir uma pipeline run sem cancelá-la
**So that** posso segurar a execução entre estágios para inspeção manual sem perder o progresso do DAG.

### Problem Context

`SPEC-20260919-ade-cockpit-hitl` (entregue via PR #245) exigia "Run Action Buttons: Pause, Resume, Stop (Cancel), e Create Pull Request" e a task T4 foi marcada como concluída — porém o `PipelineEngine` não possui primitiva de pause, e `RunControlBar` só implementa Steer/Stop/Create-PR. O desvio foi documentado no PR mas a acceptance criterion permanece sem implementação.

**Evidência:**

- TO-BE: `.specs/SPEC-20260919-ade-cockpit-hitl.md` linha 48 (AC "Pause, Resume, Stop") e task T4 marcada `[x]` ("Implementar `RunControlBar` com Steer, Pause, Cancel e modal de aprovação").
- AS-IS: `grep -rniE 'pause|resume' src/Taskboard.Blazor/Components/Cockpit/ src/Taskboard.Application/Harness/` → 0 matches; `RunControlBar.razor` expõe apenas steer/stop/create-pr; `PipelineEngine` tem apenas cancel via `CancellationToken`.
- Impacto: usuário não consegue suspender um run para intervenção — a única opção é Stop (terminal).

---

## 2. Scope

### In scope

- **Domain/engine**: primitiva de pause no `PipelineEngine` — `PauseRunAsync(id)` suspende a execução **entre estágios** (checkpoint no boundary do stage; o estágio em voo completa ou é cooperativamente sinalizado); `ResumeRunAsync(id)` retoma do próximo estágio pendente. Estado `Paused` (ou flag `IsPaused`) persistido no `PipelineExecution` com `long Version` (optimistic concurrency já existente).
- **REST**: `POST /api/harness/runs/{id}/pause` e `POST /api/harness/runs/{id}/resume` → `200` com o run atualizado; `409` se estado não transiciona (ex.: pause em run já `Paused`/`Succeeded`/`Cancelled`); `404` run inexistente.
- **SignalR**: eventos `RunPaused`/`RunResumed` publicados no `/harness-cockpit-hub` no grupo do run (mesmo padrão `ReceiveCockpitEvent`).
- **UI**: `RunControlBar` ganha botões Pause (visível quando Running) e Resume (visível quando Paused); timeline exibe eventos de pause/resume; badge de estado no header da página de detalhe reflete `Paused`.
- **Reaper**: `StaleRunReaper` não deve colher runs `Paused` como stale (pause legítimo pode durar além do threshold) — ou threshold ampliado para paused; decidir e documentar.
- **SPEC origem**: corrigir `SPEC-20260919-ade-cockpit-hitl` T4/AC para refletir o que foi entregue ou marcar este spec como continuação.

### Out of scope

- Pause intra-estágio cooperativo dentro de tool calls individuais do agente (checkpoint é no boundary do stage).
- Persistir estado de pause para sobreviver restart do host (aceitável: runs paused viram stale após restart; documentar).

---

## 3. Acceptance Criteria (BDD)

- **AC1 — Pause entre estágios:** Dado um run `Running` com estágio em voo, quando `POST /runs/{id}/pause` é chamado, então o run transiciona para `Paused`, o estágio corrente completa (ou recebe sinal cooperativo), nenhum estágio novo inicia, e `RunPaused` é publicado no hub.
- **AC2 — Resume:** Dado um run `Paused`, quando `POST /runs/{id}/resume` é chamado, então o próximo estágio pendente é despachado, o run volta para `Running`, e `RunResumed` é publicado.
- **AC3 — UI:** Dado run `Running`, o `RunControlBar` exibe Pause (não Resume); dado `Paused`, exibe Resume (não Pause); clicar chama o endpoint e atualiza estado via evento do hub.
- **AC4 — Conflitos de estado:** pause em `Paused`/`Succeeded`/`Failed`/`Cancelled` → `409`; resume fora de `Paused` → `409`; ids inexistentes → `404`.
- **AC5 — Reaper:** run `Paused` por mais de 15 min não é colhido como stale (ou é colhido com threshold distinto, conforme decisão registrada).
- **AC6 — Persistência:** `Paused` sobrevive a reload da página e replay de eventos (REST replay inclui o estado paused).

---

## 4. Tasks

- [x] **T1 — Domain:** estado `Paused` no `PipelineExecution` (+ EF config/migration se novo valor enum ou coluna), transições `Pause()`/`Resume()` com guard de estado, `PipelineStatus` mapping.
- [x] **T2 — Engine:** checkpoint de pause no loop do `PipelineEngine` (checar flag entre estágios; `WaitHandle`/`SemaphoreSlim` ou canal de controle como o `ISteerQueue`); eventos `RunPaused`/`RunResumed` via `ICockpitEventStream`.
- [x] **T3 — REST:** endpoints pause/resume com mapeamento de erros (404/409) + testes de integração (`CockpitEndpointsTests`).
- [x] **T4 — UI:** botões em `RunControlBar`, badge `Paused`, cartões de timeline para os eventos novos.
- [x] **T5 — Reaper + docs:** ajuste do `StaleRunReaper`, `docs/api.md` (+pt-br), `docs/features.md` (+pt-br), correção da T4/AC na SPEC origem, este SPEC → `Done`.

---

## 5. Verification

- `dotnet build` 0 warnings/0 errors.
- `dotnet test`: novos unit tests para transições `Pause`/`Resume` (válidas + inválidas) e engine checkpoint; integration tests dos endpoints; suíte completa verde.
- Smoke manual: iniciar run no `/cockpit`, pausar entre estágios, verificar timeline + badge, resumir e confirmar continuação.

---

## 6. Risks & Open Questions

1. Pause cooperativo intra-estágio exigiria plumbing no adapter do agente — fora de escopo; boundary de estágio é o contrato.
2. Decisão necessária: `Paused` como novo membro de `PipelineStatus` (migration segura — SQLite guarda string/int) vs flag separada `IsPaused`.
3. Restart do host com run paused: política = stale após threshold ampliado ou resume manual apenas — registrar no código + docs.
   - **Decisão registrada (2026-09-20):** não existe reaper para `PipelineExecution` — o `StaleRunReaper`/`StaleAgentRunReaperService` só reconcilia `AgentRun` legado, então execuções pausadas nunca são colhidas como stale. O tick do `PipelineEngine` também ignora execuções `Paused` (sem dispatch, sem worktree attach, sem budget-cap cancel). Runs pausadas aguardam resume/cancel manual; após restart do host elas permanecem `Paused` e podem ser resumidas — os estágios pendentes são redespachados normalmente.
