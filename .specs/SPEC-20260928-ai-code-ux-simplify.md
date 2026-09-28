# SPEC-20260928-ai-code-ux-simplify

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-code-ux-simplify` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WASM / xterm.js` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260928-ai-code-ux-simplify` |
| Ticket | [#347](https://github.com/afonsoft/agent-harness/issues/347) |
| Status | `Approved` |
| Related | `SPEC-20260921-ai-code-chat-ux`, `SPEC-20260922-ai-chat-command-bar`, `SPEC-20260928-ai-code-generic-cli`, `SPEC-20260928-nav-menu-order` |

## 1. User Story

**As a** usuário do Harness
**I want** uma interface de AI Code mais simples e funcional — lista de conversas sempre visível, criação de thread em um clique e barra de comando compacta
**So that** eu foque na conversa com o agente em vez de navegar por modais e seletores a cada ação.

**Problem context — análise comparativa (clones em `/tmp/cli-ux-analysis/`):**

- **`vultuk/claude-code-web`** mostra que a UX mínima viável é fortíssima: tabs de sessão sempre visíveis + conteúdo em tela cheia + input. Nenhum modal entre o usuário e o agente. Sessões persistem e reattacham.
- **`comfortablynumb/claudito`** escala o mesmo padrão: sidebar de projetos + sub-tabs de agentes com input/toolbar **por tab** (cada tab lembra seu modo, modelo, fonte) — configuração vive no contexto da sessão, não em uma barra global.
- **Estado atual do AI Code** (`AiChat.razor`, ~1270 linhas + 3 cards): funcionalmente rico (threads, queue, tool cards, permission prompts, usage meter, fork, modo agent/assistant) mas **denso**:
  1. Lista de threads só existe dentro de um `Modal` "Threads" — trocar de conversa = 2 cliques + scroll + 1 clique, e o histórico some quando o modal fecha.
  2. Command bar tem ~8 controles visíveis simultâneos (Mode, Agent CLI, repo, workspace, model, sandbox, badges de sessão, usage meter) — a maioria imutável durante a sessão, competindo por atenção com o composer.
  3. Criar thread exige `NewThreadDialog` com várias escolhas antes do primeiro prompt.
  4. O composer é a ação principal mas divide a footer com Retry/Stop/Run agent empilhados.

Objetivo: manter o poder funcional e alcançar a simplicidade dos dois projetos analisados — **uma coluna de threads + uma área de conversa + uma linha de contexto + um composer**.

## 2. Scope

**In scope:**

1. **Thread rail persistente:** painel lateral esquerdo dentro da página AI Code (~260px, colapsável para ícones) listando threads (título, badge agent/model, status live, tempo relativo), com ações inline (delete com confirmação) — substitui o `Modal` "Threads" como superfície primária. Em telas estreitas o rail vira drawer overlay (mesmo padrão do sidebar global).
2. **New thread em um clique:** botão `+` no topo do rail cria thread com defaults sensatos (`mode=agent`, agent CLI = último usado/eligível, repo = `SelectedRepositoryService.Selected`, model/sandbox = default da CLI) e a abre imediatamente — o `NewThreadDialog` deixa de ser obrigatório e passa a ser acessado por `+ ▾` ("New with options…") para quem quer escolher tudo antes.
3. **Command bar compacta:** uma única linha de contexto com chips clicáveis — `[agent]` `[model]` `[sandbox]` `[repo]` — cada chip abre o respectivo popup existente (repo/model/sandbox modais já existem e são reutilizados). Controles de modo e badges read-only de sessão movem-se para um popover `⋯`/header; usage meter compacto permanece visível no header da thread.
4. **Composer como ação principal:** botões Send sempre visível; Retry/Stop aparecem contextualmente (Stop só quando running, Retry só quando há prompt anterior); "Run agent" (assistant→agent handoff) vai para o popover `⋯`.
5. **Hook para threads terminal:** a lista e o painel aceitam `kind=terminal` renderizando `PtyThreadPane` quando `SPEC-20260928-ai-code-generic-cli` existir — aqui só o contrato visual/slot (badge `terminal`, pane placeholder escondido se feature ausente).
6. **Persistência leve de UI:** estado do rail (colapsado/expandido) e último agent CLI usado persistem em `localStorage` via `taskboard.*` JS já existente.

**Out of scope:**

- Mudanças no protocolo SSE/ACP, `AiChatService`, endpoints — zero backend (exceto o que a SPEC-20260928-ai-code-generic-cli traz separadamente).
- Drag-reorder de threads, rename inline (rename existe? manter como está), multi-select/bulk delete.
- Split view lado-a-lado de threads (feature do claude-code-web — anotar como follow-up possível, não nesta SPEC).
- Redesign visual/tema — somente reorganização de layout com classes Bootstrap/CSS existentes.
- Tool cards, permission prompts, plan-mode UI — mantidos como estão (já funcionais).

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — reestruturação do markup: wrapper `.ai-chat-layout` (rail + main), rail markup reaproveitando `OrderedThreads`/thread-item pattern hoje dentro do `_historyModal`, command bar condensada, footer do composer simplificada.
- `src/Taskboard.Blazor/Components/AiChat/` — novos componentes extraídos: `ThreadRail.razor` (lista + ações), `ContextBar.razor` (chips + popovers); `PtyThreadPane.razor` slot (stub) para integração futura.
- `src/Taskboard.Client/wwwroot/css/site.css` — estilos do layout rail/main + drawer mobile (seguir convenções `.ai-chat-*` existentes).
- `src/Taskboard.Client/wwwroot/js/taskboard.js` — helpers `localStorage` para rail state e last-agent (padrão `taskboard.setSidebarCollapsed` já existente).
- `NewThreadDialog.razor` (onde estiver) — mantido, mas acionado só pelo "New with options".

**Files to read before implementing:**

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` (inteiro — markup + code-behind inline)
- `src/Taskboard.Blazor/Components/AiChat/{ToolCallCard,PermissionPromptCard,ToolCallChangesCard}.razor`
- `src/Taskboard.Blazor/Layout/NavMenu.razor` + `MainLayout.razor` (padrão rail colapsável/tooltips)
- `src/Taskboard.Client/wwwroot/js/{taskboard.js,terminal.js}`, `wwwroot/css/site.css` (seções `.ai-chat-*`)
- Referência externa (leitura em /tmp): `/tmp/cli-ux-analysis/claude-code-web/src/public/session-manager.js` (tabs/reattach UX), `/tmp/cli-ux-analysis/claudito/public/` (layout tabs + toolbar por tab)

**Files to create or modify:**

```text
src/Taskboard.Blazor/Components/Pages/AiChat.razor            # modified — layout rail+main, bar compacta
src/Taskboard.Blazor/Components/AiChat/ThreadRail.razor       # new — lista persistente de threads
src/Taskboard.Blazor/Components/AiChat/ContextBar.razor       # new — chips + popovers de contexto
src/Taskboard.Blazor/Components/AiChat/PtyThreadPane.razor    # new — slot/stub para SPEC-20260928-ai-code-generic-cli
src/Taskboard.Client/wwwroot/css/site.css                     # modified — .ai-chat-layout, rail, drawer
src/Taskboard.Client/wwwroot/js/taskboard.js                  # modified — persist rail state/last agent
tests/Taskboard.Tests.Unit/AiChat/*                           # new bUnit — rail render, defaults do one-click new
```

## 4. Requirements

### RF-001: Thread rail persistente

- **Description:** A lista de threads (mesmo conteúdo do modal atual) renderiza em painel lateral sempre visível à esquerda do chat, ordenada por `UpdatedAt` desc, com active state, badge agent/model, status live e delete inline-confirm.
- **Rules:** Colapsar → rail de ~48px com ícones+tooltip (padrão `RailTitle`); mobile <768px → drawer overlay com scrim; estado persistido em localStorage.
- **Input → Output:** threads carregadas → seleção em 1 clique, sem modal.

### RF-002: New thread em um clique

- **Description:** `+` no rail cria e abre thread com defaults; `+ ▾` abre o `NewThreadDialog` completo.
- **Rules:** Defaults: mode `agent`, agent = `localStorage.lastAgent` se ainda elegível senão primeiro elegível, repo = `RepoService.Selected` se houver, model/sandbox = defaults do servidor; nenhum elegível → `+` desabilitado com tooltip apontando Settings.
- **Input → Output:** click `+` → thread criada, ativa e composer focado.

### RF-003: Context bar compacta

- **Description:** Uma linha: chips `[agent]` `[model]` `[sandbox]` `[repo]` (apenas os relevantes ao modo), cada um abrindo o modal existente correspondente; controles restantes sob popover `⋯`.
- **Rules:** Chips de sessão ativa mostram valores live (modelo da sessão, sandbox); read-only onde o backend não permite troca; usage meter permanece à direita no header.
- **Input → Output:** barra de contexto ≤ 56px em desktop.

### RF-004: Composer simplificado

- **Description:** Send sempre visível e primário; Stop visível apenas com run ativo; Retry apenas quando aplicável; "Run agent" no popover `⋯`.
- **Rules:** Nenhuma funcionalidade removida — apenas reposicionada; atalhos de teclado existentes preservados (Ctrl+Enter etc. conforme setting).

### RF-005: Slot de thread terminal

- **Description:** `AiChatThread` com `kind=terminal` renderiza `PtyThreadPane` no lugar das bubbles; sem backend ainda → pane exibe placeholder informativo e o tipo só aparece quando a SPEC-20260928-ai-code-generic-cli adicionar o transport.
- **Rules:** badge `terminal` no rail; ausência do backend não quebra a página.

**Business rules / invariants:**

- Nenhuma feature existente (queue, fork, tool cards, permission prompts, usage, modo, sandbox, modelos de sessão) pode desaparecer — só mudar de lugar.
- O modal "Threads" pode ser removido como entry point, mas o código da lista deve ser compartilhado com o rail (sem duplicação de markup).

## 6. Acceptance Criteria

- [ ] **Given** a página AI Code **when** carrego com threads existentes **then** o rail mostra todas ordenadas sem abrir modal, e selecionar uma troca o chat em 1 clique.
- [ ] **Given** ao menos um agent CLI elegível **when** clico `+` **then** uma thread agent é criada com defaults e o composer fica focado — nenhum diálogo.
- [ ] **Given** nenhuma CLI elegível **when** vejo o rail **then** `+` está desabilitado com tooltip para Settings → Agents.
- [ ] **Given** uma thread ativa **when** observo a context bar **then** só chips relevantes aparecem e cada um abre seu popup (repo/model/sandbox).
- [ ] **Given** run ativo **when** olho o composer **then** Stop visível; sem run → Stop ausente; há prompt anterior → Retry visível.
- [ ] **Given** viewport 375px **when** abro o rail **then** ele aparece como drawer overlay com scrim, e fecha ao selecionar thread.
- [ ] **Given** refresh da página **when** retorno **then** estado do rail e último agent usado persistem.
- [ ] **Given** suíte `dotnet test` **when** executada **then** verde, com bUnit cobrindo rail e one-click-new.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Zero threads | lista vazia | Rail mostra empty state + CTA `+` |
| Título longo / 100+ threads | lista grande | Scroll virtual ou overflow simples; trunc com title |
| Thread running enquanto navego | select outra thread | Thread continua; badge live persiste no rail |
| Rail colapsado | ícone ativo | Tooltip label; clique expande ou seleciona via dropdown |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** ler arquivos da seção 3; mapear o que extrair de `AiChat.razor`.
- [ ] **T2 — Layout + ThreadRail:** wrapper rail/main, extrair lista do modal para componente compartilhado, drawer mobile, collapse persistido.
- [ ] **T3 — One-click new:** defaults + `lastAgent` storage + fallback `NewThreadDialog`.
- [ ] **T4 — Context bar + composer:** chips→modais existentes, popover `⋯`, visibilidade contextual de botões.
- [ ] **T5 — Pty slot + polish:** `PtyThreadPane` stub, badge `terminal`, CSS, a11y (aria, focus).
- [ ] **T6 — Verification:** bUnit (rail, defaults, visibilidade de botões), `dotnet build` + `dotnet test`, smoke manual documentado (desktop expandido/colapsado, mobile drawer).
- [ ] **T7 — Done + PR:** `Status = Done`, PR na branch `feature/devin-20260928-ai-code-ux-simplify`.

**7.1 Validation strategy**

- `.NET/Blazor`: bUnit para componentes novos e estados condicionais; suíte existente verde (regressão zero); evidência visual documentada no PR (antes/depois).
- Sem backend novo → sem contrato de API; coverage gate respeitado para código novo.

## 8. Organization Guardrails

- **Branches:** `feature/devin-20260928-ai-code-ux-simplify`.
- **Workflows:** intocado.
- **Scope:** nada de mudanças de protocolo/backend; PTY real vem da SPEC irmã.
- **Architecture:** componentes burros — estado e chamadas seguem nos serviços existentes (`TaskboardClient`, SSE); regra no code-behind/serviços, não no JS.
- **i18n:** labels em inglês na UI (padrão do produto); docs pt-br se aplicável.

## 9. Definition of Done

- [ ] RF-001 a RF-005 implementados.
- [ ] Acceptance criteria verificados; nenhuma feature existente regredida.
- [ ] bUnit novos verdes; `dotnet build` limpo; suíte completa verde.
- [ ] Guardrails respeitados.
- [ ] `docs/features.md` + `.pt-br` atualizados com o novo layout.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/devin-20260928-ai-code-ux-simplify`.

## Open Questions / Pending Ambiguity — RESOLVIDAS (aprovado pelo usuário)

- `Q1` — **Removido como entry point:** a lista é o rail (mesmo componente compartilhado).
- `Q2` — **Fixa + colapsável** nesta entrega; resize por drag é follow-up.
- `Q3` — **Esta SPEC primeiro;** a generic-cli pluga `PtyThreadPane` no slot pronto.
