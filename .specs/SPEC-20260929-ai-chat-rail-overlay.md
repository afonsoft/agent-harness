# SPEC-20260929-ai-chat-rail-overlay

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-chat-rail-overlay` |
| Type | `Feature / UI` |
| Stack | `.NET 10 / Blazor WASM` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260929-ai-chat-rail-overlay` |
| Ticket | [#382](https://github.com/afonsoft/agent-harness/issues/382) (Epic [#381](https://github.com/afonsoft/agent-harness/issues/381)) |
| Status | `Done` |
| Related | SPEC-20260922-ai-chat-command-bar, SPEC-20260928-ai-code-ux-simplify, SPEC-20260929-ai-code-ux-fixes |

## 1. User Story

**As a** usuário do AI Code
**I want** que o histórico de threads não ocupe espaço permanente na tela — fique escondido e apareça sob demanda, com botões de Histórico e New chat no canto superior direito sobre o painel de chat
**So that** a área de mensagens/composer use toda a largura disponível e o histórico só apareça quando eu pedir.

**Problem context:**

Hoje o thread rail é um `aside` persistente (264px / 52px colapsado) ao lado do chat. Pedido do usuário: escondê-lo por padrão e promover `History`/`New` a botões no topo-direito do painel de chat; o rail vira overlay (mesmo padrão do drawer mobile) em todas as larguras.

## 2. Scope

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Client/wwwroot/css/site.css` (rail/drawer rules)
- `tests/Taskboard.Tests.Unit/Blazor/AiChatSourceGuardTests.cs` (+1–2 guards)
- `docs/features.md` + `docs/features.pt-br.md`

## 3. Technical Context

- Layout atual: `.ai-chat-layout` flex com `.ai-chat-rail-wrap` (flex item) + `.ai-chat-main`. Mobile (<768px) já usa drawer overlay (`drawer-open` + scrim).
- `SelectThreadFromRailAsync` já fecha o drawer ao selecionar; `NewConversationAsync` já existe e fecha `_railDrawerOpen`.
- `_railCollapsed`/`taskboard.getAiChatRailCollapsed` persistem o colapso — com overlay permanente, o estado de colapso deixa de fazer sentido para a UX nova; manter a leitura/persistência é opcional — decisão: remover o toggle de colapso, o rail overlay sempre renderiza expandido.

## 4. Requirements

### RF-001: Rail oculto por padrão

- **Description:** `.ai-chat-rail-wrap` deixa de ser flex item — vira overlay `position: fixed` (ou absolute dentro do layout) com scrim, exibido apenas quando `_railDrawerOpen` (todas as larguras, não só mobile). Remover o toggle de colapso do header do rail.

### RF-002: Botões no topo-direito do chat

- **Description:** No cabeçalho/conteúdo do painel de chat (`.ai-chat-main`), topo-direito: botão **History** (ícone, abre o overlay — reusa `OpenRailDrawerAsync`, que já recarrega a lista) e botão **New** (reusa `NewConversationAsync`, respeitando `HasEligibleAgent`). Posicionados acima/à direita do painel de chat.

### RF-003: Fechamento

- **Description:** Overlay fecha por: scrim click, seleção de thread, New, e tecla Esc (hook existente de fechamento se houver — senão só scrim/seleção).

### RF-004: Mobile

- **Description:** Sem regressão — no mobile o mesmo overlay serve; o hamburger existente continua abrindo (ou aponta para os mesmos botões).

### RF-005: Docs + guards

- **Description:** Atualizar `features.md`/pt-br (rail overlay on-demand, sem rail persistente); guard de fonte: botões `History`/`New` no topo-direito do chat + `ai-chat-rail-wrap` não é flex item do layout.

## 5. API Contract

Sem mudança.

## 6. Acceptance Criteria

- [x] **Given** página AI Code **when** aberta **then** o rail não ocupa largura; chat usa 100%.
- [x] **Given** clique em History **when** executado **then** overlay com a lista abre sobre o chat (e recarrega).
- [x] **Given** overlay aberto **when** selecionar thread/scrim/New **then** fecha.
- [x] **Given** build/test **then** verde.

## 7. Task Plan

- [x] **T1 — UI:** botões top-right + overlay permanente + remover collapse toggle.
- [x] **T2 — CSS:** overlay em todas as larguras; scrim visível ≥768px também.
- [x] **T3 — Tests/docs:** guards + features.md.
- [x] **T4 — Done + PR.**

## 8. Organization Guardrails

- Apenas Blazor/CSS/tests/docs — sem mudança de API.
- Manter `@key` do `PtyThreadPane` e comportamento de `OpenRailDrawerAsync`/`RefreshThreadsAsync`.
