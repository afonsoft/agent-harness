# SPEC-20260928-nav-menu-order

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `nav-menu-order` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WASM` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260928-nav-menu-order` |
| Ticket | [#345](https://github.com/afonsoft/agent-harness/issues/345), PR [#350](https://github.com/afonsoft/agent-harness/pull/350) |
| Status | `Done` |
| Related | `SPEC-20260921-ai-code-chat-ux`, `SPEC-20260922-harness-home-rename` |

## 1. User Story

**As a** usuário do Harness
**I want** que o item "AI Code" apareça logo abaixo de "Board" no menu lateral, seguido imediatamente por "Terminal"
**So that** os dois fluxos principais de trabalho com agentes (conversa estruturada e terminal) fiquem no topo da navegação, junto ao Board, refletindo a ordem de uso real.

**Problem context:**

Ordem atual do sidebar (`NavMenu.razor`, ordem de declaração dos `NavLink`):

```text
Repository selector / rail
Board
Gantt
Workflow
Specs
VS Code
Terminal          ← hoje
— divider —
Cockpit
AI Code           ← hoje
CLI Agents
FinOps
Settings
Skills
Prompts
Issues (external)
```

"AI Code" está na segunda seção (após o divider), longe do Board, e "Terminal" está na primeira seção entre editores. O usuário quer agrupar as três superfícies de trabalho no topo.

## 2. Scope

**In scope:**

- Reordenar os `NavLink` em `src/Taskboard.Blazor/Layout/NavMenu.razor` para a ordem-alvo:
  1. Board
  2. AI Code
  3. Terminal
  4. Gantt
  5. Workflow
  6. Specs
  7. VS Code
  8. — divider —
  9. Cockpit
  10. CLI Agents
  11. FinOps
  12. Settings
  13. Skills
  14. Prompts
  15. Issues (external)
- Preservar todos os atributos existentes de cada `NavLink` (`href`, `aria-label`, `data-label`, `title`/`RailTitle`, `@onclick="CloseSidebarAsync"`, `Icon`, `Match` no Board) — a mudança é exclusivamente de posição no markup.
- Verificação visual/manual: sidebar expandido e rail colapsado mostram a nova ordem; tooltips e `data-label` corretos.

**Out of scope:**

- Renomear labels, trocar ícones ou alterar rotas.
- Mover o divider ou reagrupar outras seções além do especificado.
- Qualquer mudança em `MainLayout.razor`, CSS ou comportamento de colapso do sidebar.
- Reordenação configurável por usuário (não existe persistência de ordem de menu).

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Blazor/Layout/NavMenu.razor` — único arquivo alterado. Os `NavLink` são declarados sequencialmente; mover os blocos de "AI Code" (`href="ai-chat"`, linhas ~61–64) para logo após o Board (linhas ~32–35), e o bloco de "Terminal" (`href="terminal"`, linhas ~52–55) para logo após o AI Code.
- Não há testes que dependem da ordem do menu (confirmado: `tests/` só menciona sidebar em comentário sobre NavLinks mortos em `ServerEndpointsTests.cs:136`).

**Files to read before implementing:**

- `CLAUDE.md` · `.claude/rules/global-rules.md`
- `src/Taskboard.Blazor/Layout/NavMenu.razor`
- `src/Taskboard.Blazor/Layout/MainLayout.razor` (contexto do rail colapsado/`IsSidebarCollapsed`)

**Files to create or modify:**

```text
src/Taskboard.Blazor/Layout/NavMenu.razor   # modified — blocos NavLink reordenados
```

## 4. Requirements

### RF-001: AI Code abaixo de Board

- **Description:** O `NavLink` de "AI Code" (`href="ai-chat"`) deve ser o segundo item de navegação, imediatamente após "Board" e antes de qualquer outro item.
- **Rules:** Manter `aria-label="AI Code"`, `data-label="AI Code"`, `IconName.ChatDots` e `CloseSidebarAsync`.
- **Input → Output:** Render do NavMenu → ordem DOM: Board, AI Code, …

### RF-002: Terminal abaixo de AI Code

- **Description:** O `NavLink` de "Terminal" (`href="terminal"`) deve ser o terceiro item, imediatamente após "AI Code" e antes de "Gantt".
- **Rules:** Manter todos os atributos e o `IconName.Terminal`; Terminal sai da posição entre "VS Code" e o divider.

### RF-003: Demais itens preservados

- **Description:** Gantt, Workflow, Specs, VS Code permanecem na primeira seção após Terminal, nessa ordem; divider e segunda seção (Cockpit, CLI Agents, FinOps, Settings, Skills, Prompts, Issues) inalterados.

**Business rules / invariants:**

- Nenhum `NavLink` é removido ou duplicado — apenas reposicionado.
- `NavLinkMatch.All` permanece apenas no Board.

## 6. Acceptance Criteria

- [ ] **Given** o app renderizado com sidebar expandido **when** o usuário lê o menu de cima para baixo **then** a ordem é Board → AI Code → Terminal → Gantt → Workflow → Specs → VS Code → (divider) → Cockpit → CLI Agents → FinOps → Settings → Skills → Prompts → Issues.
- [ ] **Given** o rail colapsado **when** o usuário passa o mouse sobre o 2º e 3º ícones **then** os tooltips exibem "AI Code" e "Terminal" respectivamente.
- [ ] **Given** qualquer item clicado **when** a navegação ocorre **then** a rota correta abre e o sidebar fecha (`CloseSidebarAsync`).
- [ ] **Given** `dotnet build` + `dotnet test` **when** executados **then** build limpo e suíte verde (nenhuma regressão).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Rail colapsado | `IsSidebarCollapsed == true` | Ordem dos ícones segue a nova ordem; `RailTitle` tooltips corretos |
| Mobile/drawer | Sidebar como overlay | Mesma ordem; fechar ao navegar continua funcionando |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** ler `NavMenu.razor` e confirmar ordem atual e atributos.
- [ ] **T2 — Implementation:** mover o bloco `NavLink` de AI Code para após Board e o de Terminal para após AI Code.
- [ ] **T3 — Verification:** `dotnet build` (TreatWarningsAsErrors) + `dotnet test`; smoke visual documentado (expandido + rail).
- [ ] **T4 — Validation:** preencher DoD (section 9).
- [ ] **T5 — Done + PR:** `Status = Done` e PR na branch `feature/devin-20260928-nav-menu-order`.

**7.1 Validation strategy by type/stack**

- `.NET / Blazor`: build sem warnings, suíte de testes verde; mudança de markup sem lógica — evidência visual documentada (screenshot ou descrição do DOM).

## 8. Organization Guardrails

- **Branches:** nunca commitar em `main`, `master` ou `develop`. Branch `feature/devin-20260928-nav-menu-order`.
- **Workflows:** não modificar `.github/workflows/`.
- **Security:** N/A — sem dados sensíveis.
- **Scope:** apenas reordenação dos três itens; nenhuma outra alteração de UX nesta SPEC.

## 9. Definition of Done

- [ ] RF-001 a RF-003 implementados.
- [ ] Critérios de aceitação verificados (visual + build/test).
- [ ] `dotnet build` limpo (warnings como erros) e `dotnet test` verde.
- [ ] Guardrails respeitados.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/devin-20260928-nav-menu-order`.
