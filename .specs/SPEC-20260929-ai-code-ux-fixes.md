# SPEC-20260929-ai-code-ux-fixes

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-code-ux-fixes` |
| Type | `Bugfix / Frontend (Blazor)` |
| Stack | `Blazor WASM` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260929-ai-code-ux-fixes` |
| Ticket | [#370](https://github.com/afonsoft/agent-harness/issues/370) (Epic [#366](https://github.com/afonsoft/agent-harness/issues/366)) |
| Status | `Approved` |
| Related | `SPEC-20260928-ai-code-ux-simplify` (PR #352) |

## 1. User Story

**As a** usuário do AI Code
**I want** que o botão New crie a conversa de fato, respeite o repositório selecionado, feche o drawer no mobile e que o rail se mantenha atualizado
**So that** o fluxo principal da página funcione como a UI promete — hoje New é quase um no-op.

**Problem context:**

Achados do Devin Review no PR #352 (confirmados no código):

1. 🔴 **New não cria a conversa** — `NewConversationAsync` (`AiChat.razor:~1209`) só limpa estado/foca composer; a thread só é criada no primeiro `SendMessageAsync`. Não aparece no rail nem recebe defaults.
2. 🟡 **New ignora `SelectedRepositoryService`** — `_cfgRepo` começa vazio/herdado; a criação não resolve a seleção global de repositório.
3. 🟡 **Drawer cobre o composer no mobile** — New não fecha `_railDrawerOpen`; foca o composer atrás do overlay.
4. 🟡 **Rail desatualizado entre abas** — `_threads` só é carregado em `OnInitializedAsync`; abrir o drawer não recarrega (o `ShowHistoryAsync` que recarregava foi removido).
5. 🔍 **Retry habilitado sem prompt** — thread agent vazia mostra Retry; ação termina em aviso evitável.
6. 🔍 **Run agent ignora `_running`/`_sending`** — o item de menu pode disparar run com transcrição incompleta.
7. 🔍 **Rail colapsado (52px)** — verificar que New permanece visível/clicável (dois botões lado a lado cortam conteúdo).
8. 🔍 **`docs/features.md` (+pt-br)** — a SPEC marcou como atualizados mas o PR não os alterou; documentar o rail.

## 2. Scope

**In scope:**

- `NewConversationAsync`: criar thread via `CreateAiChatThreadAsync` com defaults, inserir em `_threads`, selecionar, fechar drawer, focar composer.
- Resolver `_cfgRepo` a partir de `SelectedRepositoryService` ao criar.
- Recarregar `_threads` ao abrir o drawer + estratégia equivalente no rail desktop (focus/visibility ou polling leve), sem perder seleção.
- Desabilitar Retry quando não há prompt; desabilitar Run agent enquanto `_running`/`_sending`.
- CSS do rail colapsado: garantir New visível/clicável (verificação + guard).
- Atualizar `docs/features.md` e a versão pt-br.
- Testes (bUnit/guards) por correção.

**Out of scope:**

- SSE/realtime de threads entre abas (apenas refresh sob demanda).
- Redesign do rail.

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — `NewConversationAsync`, drawer, `_threads`, Retry/Run.
- `src/Taskboard.Blazor/Services/SelectedRepositoryService.cs` — seleção global (leitura/injeção).
- `src/Taskboard.Blazor/Services/TaskboardClient.cs` — `CreateAiChatThreadAsync` (leitura).
- `src/Taskboard.Client/wwwroot/css/site.css` — rail colapsado.
- `docs/features.md`, `docs/pt-br/features.md`.
- `tests/Taskboard.Tests.Unit/Blazor/` — `ThreadRailTests` + novos guards.

## 4. Requirements

### RF-001: New cria a thread

- **Description:** Clique em New cria a conversa via `CreateAiChatThreadAsync` (com defaults de CLI/modelo elegíveis), adiciona a `_threads`, seleciona e foca o composer. Falha na criação → toast controlado.

### RF-002: New honra o repositório global

- **Description:** Na criação, `repo` vem de `SelectedRepositoryService` quando definido; fallback para o da thread anterior só quando o global está vazio.

### RF-003: Drawer mobile fecha ao New

- **Description:** `NewConversationAsync` (e o callback New do rail) fecha `_railDrawerOpen` antes de focar o composer.

### RF-004: Rail reflete mudanças externas

- **Description:** Abrir o drawer recarrega `_threads`; no rail desktop, refresh ao focar a janela ou equivalente — sem perder `_activeThreadId`.

### RF-005: Ações condicionadas ao estado

- **Description:** Retry desabilitado quando a thread não tem prompt; item Run agent desabilitado enquanto `_running || _sending`.

### RF-006: Rail colapsado utilizável

- **Description:** No rail de 52px, New permanece totalmente visível e clicável (CSS + guard de fonte).

### RF-007: Docs

- **Description:** `docs/features.md` e pt-br descrevem o rail persistente, New, drawer mobile e comportamento de Retry/Run.

## 5. API Contract

Sem mudança — usa `CreateAiChatThreadAsync` existente.

## 6. Acceptance Criteria

- [ ] **Given** zero threads e CLI elegível **when** New é clicado **then** uma thread aparece no rail selecionada (teste bUnit ou guard + verificação manual).
- [ ] **Given** seleção global `org/proj-b` **when** New cria a thread **then** `repo=org/proj-b` (teste).
- [ ] **Given** drawer aberto em viewport mobile **when** New é clicado **then** o drawer fecha e o composer fica acessível.
- [ ] **Given** thread criada em outra aba **when** o drawer abre **then** a lista a inclui.
- [ ] **Given** build/test + docs **then** verde e docs atualizados.

## 7. Task Plan (agent execution)

- [ ] **T1 — New:** criação real + repo global + drawer + foco.
- [ ] **T2 — Rail refresh:** recarga no drawer/focus.
- [ ] **T3 — Ações:** Retry/Run condicionados + CSS do rail colapsado.
- [ ] **T4 — Testes + docs.**
- [ ] **T5 — Done + PR.**

## 8. Organization Guardrails

- Somente camada Blazor/Services + docs; sem mudança de contrato de API.
- Não remover cobertura existente de `ThreadRailTests`.

## 9. Definition of Done

- [ ] Fluxo New completo e testado.
- [ ] Rail atualizado cross-aba e mobile funcional.
- [ ] Build/test verde; docs atualizados; `Status = Done`.
