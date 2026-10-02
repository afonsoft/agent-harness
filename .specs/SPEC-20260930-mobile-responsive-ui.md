# SPEC-20260930-mobile-responsive-ui: Revisão mobile-first, teclado virtual e atalhos

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Mobile-First Responsive Review, Virtual Keyboard & Shortcuts |
| Product / System | agent-harness |
| Module / Bounded Context | Presentation |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20260930-mobile-responsive-ui` |
| Technical owner | afonsoft |
| Status | Approved |
| Date | 2026-09-30 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O Harness foi desenvolvido desktop-first. Várias telas usam grids densas,
tabelas e barras fixas que podem quebrar ou exigir zoom/scroll horizontal
em viewports estreitas. Não há tratamento explícito do teclado virtual
mobile (overlap de inputs, `inputmode`/`enterkeyhint` ausentes) e não
existem atalhos de teclado para acelerar o uso desktop.

### Objective

1. Auditar todas as telas Blazor contra uma matriz de requisitos mobile e
   corrigir as violações.
2. Adicionar suporte a teclado virtual (viewport `interactive-widget`,
   atributos de input mobile, scroll-into-view de campos focados).
3. Introduzir um conjunto mínimo e documentável de atalhos de teclado
   desktop que não conflitem com digitação, browser ou tecnologia
   assistiva.

### Expected outcome

- Todas as telas utilizáveis a 360px de largura sem scroll horizontal
  da página inteira.
- Inputs acionam o teclado virtual correto (`inputmode`,
  `enterkeyhint`, `autocomplete`) e permanecem visíveis ao focar.
- Atalhos documentados, descobríveis via overlay `?`, e inertes dentro
  de campos de edição.
- Funcionalidade desktop preservada integralmente.

### Out of scope

- Apps nativos (.NET MAUI fase 2).
- Redesign visual/estético (somente correções de layout e usabilidade).
- Atalhos configuráveis pelo usuário (conjunto fixo nesta SPEC).
- Testes de screenshot/visual regression automatizados (matriz manual).

---

## 2. Agent Role

> Frontend/Blazor engineer com foco em responsividade, mobile UX,
> acessibilidade (WCAG 2.2) e interação por teclado.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não alterar contratos HTTP, endpoints ou lógica de negócio.
- Não adicionar biblioteca de UI nova; usar Bootstrap 5.3 +
  Blazor.Bootstrap existentes e CSS em `site.css`.
- Não capturar atalhos que o browser ou leitor de tela já reserve
  (ver BR de atalhos).
- Não modificar `.github/workflows` sem aprovação humana.

---

## 4. Product Context

### Functional context

Telas em escopo (`src/Taskboard.Blazor/Components/Pages/`):

| Tela | Risco mobile principal |
|---|---|
| `BoardView` / Kanban | colunas horizontais, cards densos |
| `Agents` | tabelas/grids de status |
| `AiChat` | composer fixo, histórico, teclado virtual |
| `Cockpit` / `CockpitRun` | painéis multi-coluna, stream de eventos |
| `FinOps` | tabelas/gráficos |
| `Gantt` | timeline horizontal (scroll interno aceitável) |
| `Jobs` | tabelas |
| `Login` | formulário simples — baixo risco |
| `Prompts`, `Skills`, `Specs` | listas/cards |
| `Settings` | formulário longo (ver SPEC-20260930-settings-tabs) |
| `Terminal` | xterm + teclado virtual |
| `VscodeEditor` | editor embutido — viewport cheio |
| `Workflow` | designer/canvas |
| Diálogos: `AgentInstallDialog`, `AgentModelConfigDialog`, `RunAgentDialog` | modais em telas pequenas |

### Technical context

- Blazor WebAssembly (`Taskboard.Client`) + biblioteca `Taskboard.Blazor`.
- `index.html` já tem `<meta name="viewport" content="width=device-width,
  initial-scale=1.0">` — precisa de `interactive-widget=resizes-content`.
- `site.css` (2.056 linhas) já usa 16 media queries, incluindo
  `(pointer: coarse)` — existe consciência mobile parcial.
- Sidebar é `offcanvas-lg` (já vira drawer <992px).
- Bootstrap 5.3 breakpoints: xs <576, sm 576–768, md 768–992,
  lg 992–1200, xl ≥1200.
- JS helpers existentes: `wwwroot/js/taskboard.js`, `terminal.js`, `boot.js`.

### Relevant files

- `src/Taskboard.Client/wwwroot/index.html`
- `src/Taskboard.Client/wwwroot/css/site.css`
- `src/Taskboard.Client/wwwroot/js/taskboard.js`
- `src/Taskboard.Blazor/Layout/MainLayout.razor`, `NavMenu.razor`
- Todas as páginas em `src/Taskboard.Blazor/Components/Pages/*.razor`
- `src/Taskboard.Blazor/Components/{Agents,AiChat,Chat,Cockpit,GitHub,Shared,Specs}/`

---

## 5. Task Definition

### Main task

Tornar toda a UI utilizável em mobile (≥360px), com teclado virtual
ergonômico e um conjunto fixo de atalhos desktop.

### Subtasks

1. **Matriz de auditoria**: percorrer cada tela a 360/768/1280px e
   registrar violações (overflow, alvo de toque <44px, tabela sem
   wrapper, modal não fullscreen) — artefato em
   `docs/mobile-audit.md` (ou seção do SPEC na entrega).
2. **Correções globais** em `site.css` e markup: `.table-responsive`,
   `modal-fullscreen-sm-down`, stacking utilities, alvos de toque.
3. **Teclado virtual**: `interactive-widget=resizes-content` no
   viewport meta; `inputmode`/`enterkeyhint`/`autocomplete`/
   `autocapitalize`/`autocorrect`/`spellcheck` adequados por campo;
   garantir scroll-into-view do campo focado e barras fixas fora da
   área do teclado.
4. **Atalhos**: handler JS global (`taskboard.js`) com guard de
   contexto de edição + overlay `?` de ajuda.
5. **Testes** source-level para os contratos de markup/CSS.

### Do not do

- Não alterar endpoints, handlers de negócio ou payloads.
- Não converter telas para outro framework.
- Não bloquear zoom do usuário (nunca `user-scalable=no` ou
  `maximum-scale`).
- Não implementar atalhos estilo sequência (`g h`) nesta fase — apenas
  teclas únicas/modificadores.

---

## 6. Functional Requirements

### FR-001: Breakpoints e largura mínima

- Largura-alvo mínima: **360px** (cobre 99% dos phones modernos).
- Toda página deve renderizar sem `overflow-x` no `body` a 360px.
- Scroll horizontal interno é permitido apenas em containers
  explicitamente projetados (`.table-responsive`, Gantt timeline,
  Terminal viewport, tab strip de Settings).

### FR-002: Alvos de toque

- Em `(pointer: coarse)` / `(hover: none)`, controles interativos
  (botões, links de nav, toggles, ícones acionáveis, itens de lista
  clicáveis) têm área efetiva ≥44×44px — via `min-height`, `padding`
  ou `::before` de expansão quando o elemento visual for menor.
- Espaçamento mínimo de 8px entre alvos adjacentes quando possível.

### FR-003: Tipografia de inputs

- `font-size` ≥16px em todos os inputs/selects/textareas em viewports
  <768px — evita auto-zoom do iOS Safari.
- (Bootstrap `form-control` já usa 1rem; validar overrides no site.css
  e em classes utilitárias como `form-control-sm`.)

### FR-004: Tabelas e grids densos

- Tabelas com >4 colunas devem estar em `.table-responsive` ou
  renderizar layout alternativo (cards empilhados) <576px — decidir
  por tela na auditoria, registrando a escolha.
- Grids `row-cols-*` revisadas: garantir `row-cols-1` (ou `g-` spacing
  adequado) no breakpoint xs onde fizer sentido.

### FR-005: Modais em mobile

- Todos os diálogos (`AgentInstallDialog`, `AgentModelConfigDialog`,
  `RunAgentDialog` e qualquer `.modal` futuro) usam
  `modal-fullscreen-sm-down` para fullscreen em <576px.
- Botão fechar sempre visível no topo em fullscreen.

### FR-006: Teclado virtual

- `index.html`: viewport meta vira
  `width=device-width, initial-scale=1.0, interactive-widget=resizes-content`.
- Atributos por campo (auditar todos os inputs):
  - tokens/keys de API: `inputmode="text"`, `autocomplete="off"`,
    `autocapitalize="none"`, `autocorrect="off"`, `spellcheck="false"`;
  - URLs (SearxNG, MCP, provider base URL): `inputmode="url"`;
  - campos numéricos: `inputmode="numeric"`/`decimal`;
  - busca: `enterkeyhint="search"`;
  - formulário de login: `autocomplete="username"` /
    `current-password` (já existente — verificar).
- Foco em input → `scrollIntoView({ block: "nearest" })` após abertura
  do teclado quando necessário (usar `visualViewport` via JS apenas se
  o `interactive-widget` não resolver — preferir CSS primeiro).
- Barras fixas (composer do AiChat, footer actions) não podem cobrir o
  campo focado quando o teclado abre: usar
  `padding-bottom: env(keyboard-inset-height, 0)` e/ou layout flex
  com `100dvh`/`100svh` em vez de `100vh` fixo.

### FR-007: Atalhos de teclado (desktop)

Conjunto fixo, todos ignorados quando o foco está em
`input/textarea/select/[contenteditable]` ou dentro de xterm/editor:

| Atalho | Ação |
|---|---|
| `?` (`Shift+/`) | Abre/fecha overlay de ajuda de atalhos |
| `Ctrl+K` | Foca busca/comando global da página atual (se existir) |
| `Esc` | Fecha modal/drawer/overlay aberto (fallback ao comportamento Bootstrap) |
| `n` | (somente em telas com ação primária "novo") aciona o botão primário |
| `s` | Foca a primeira busca/filtro da tela |

- Implementação em `taskboard.js` exposta como `taskboard.shortcuts`
  + wiring por página via `[JSInvokable]`/evento apenas onde houver
  alvo real (não registrar atalho sem ação).
- Overlay `?` lista apenas atalhos efetivamente ativos na tela corrente.
- Atalhos não devem suprimir comportamento nativo do browser
  (`preventDefault` apenas quando o handler realmente atua).

### FR-008: Auditoria documentada

- Entregar `docs/mobile-audit.md` (ou seção equivalente) com a matriz
  tela × breakpoint × resultado × correção aplicada.

---

## 7. Business Rules

- Nenhuma mudança de autenticação/autorização.
- Zoom do usuário nunca é desabilitado.
- `Esc` nunca substitui confirmações destrutivas existentes.
- Atalhos são inertes dentro de campos editáveis — digitação nunca é
  interceptada.

---

## 8. Domain Modeling

Nenhuma alteração de domínio.

---

## 9. Expected Architecture

```
index.html ── viewport meta (interactive-widget)
site.css ── media queries (touch targets, font-size, safe-area)
taskboard.js ── taskboard.shortcuts { register(scope, map), guard(e) }
MainLayout.razor ── overlay "?" host + shortcut scope default
Pages/*.razor ── atributos de input + classes responsivas + registrations
```

Sem novos serviços de backend.

---

## 10. API Contracts

Nenhuma alteração.

---

## 11. Application Contracts

Nenhuma alteração.

---

## 12. Persistence and Data

Nenhuma alteração. Não persistir preferências de atalhos nesta fase.

---

## 13. Integrations

Nenhuma alteração.

---

## 14. Edge Cases and Error Scenarios

- `interactive-widget` não suportado (Safari <15.4, Firefox): fallback
  é o comportamento atual — não regredir.
- Dispositivo com teclado físico + mobile (tablets): guard de campo
  editável continua protegendo a digitação.
- `Esc` com modal + drawer + overlay simultâneos: fecha apenas o
  elemento mais superficial.
- Viewport <360px (ex.: foldables fechados): degradar graciosamente —
  overflow interno permitido, nunca crash.
- xterm no Terminal já captura teclas: handler global nunca deve
  disparar com foco no terminal.

---

## 15. Few-Shot Examples

Viewport meta:

```html
<meta name="viewport"
      content="width=device-width, initial-scale=1.0, interactive-widget=resizes-content" />
```

Guard de atalho (JS):

```js
const editable = e.target.closest("input, textarea, select, [contenteditable], .xterm-helper-textarea");
if (editable && !e.ctrlKey && !e.metaKey) return;
```

Input de API key:

```razor
<input id="chat-provider-key" class="form-control" type="password"
       autocomplete="off" autocapitalize="none" autocorrect="off"
       spellcheck="false" @bind="_chatProviderKey" />
```

---

## 16. Non-Functional Requirements

### Performance

- Handler de atalhos com listener único em `document` (sem listeners
  por página simultâneos).
- Nenhum observer pesado (`visualViewport` apenas se necessário).

### Accessibility

- WCAG 2.2: alvos ≥24px mínimo absoluto (meta 44px), zoom preservado,
  foco visível, atalhos descobríveis.
- Sem armadilha de teclado (`Esc` sempre sai do overlay/modal).

### Responsiveness

- Testado manualmente a 360/390/768/1280px (Chrome DevTools device
  presets: iPhone SE, iPad, desktop).

---

## 17. Mandatory Guardrails

- `dotnet build` com `TreatWarningsAsErrors` limpo.
- Nenhum `user-scalable=no`/`maximum-scale` adicionado.
- Nenhum `// nosonar` ou exclusão de cobertura.
- Mudanças limitadas a `Taskboard.Blazor`, `Taskboard.Client`
  (wwwroot) e testes — nada em `.github/`.

---

## 18. Expected Tests

### Unit tests (source-level, convenção do repo)

Novo `tests/Taskboard.Tests.Unit/Blazor/MobileResponsiveTests.cs`:

- `Dado_IndexHtml_Quando_ParseViewport_Entao_ContemInteractiveWidget`.
- `Dado_Modals_Quando_ParseRazor_Entao_UsamModalFullscreenSmDown`.
- `Dado_InputsMobile_Quando_ParseRazor_Entao_TemInputmodeOuAutocompleteAdequado`
  (campos de URL/key/numeric por `id` conhecido).
- `Dado_IndexHtml_Quando_ParseViewport_Entao_NaoDesabilitaZoomUsuario`.
- `Dado_TaskboardJs_Quando_ParseShortcuts_Entao_ExisteGuardDeCampoEditavel`.

### Integration tests

- Nenhum novo — camada de apresentação apenas.

---

## 19. Acceptance Criteria

1. Matriz de auditoria entregue cobrindo todas as telas listadas.
2. Zero `overflow-x` no `body` a 360px em todas as telas.
3. Viewport meta inclui `interactive-widget=resizes-content` e não
   desabilita zoom.
4. Modais fullscreen <576px.
5. Inputs de URL/key/busca têm `inputmode`/`autocomplete` corretos.
6. `?` abre overlay listando atalhos ativos; `Esc` fecha.
7. Digitar `/`, `n`, `s` dentro de um input não dispara atalhos.
8. Build + suite de testes verdes (baseline 1309 + novos).

---

## 20. Implementation Plan

1. Auditoria: rodar a app, percorrer telas nos 3 breakpoints, registrar
   na matriz.
2. RED: testes de contrato (viewport, modais, inputmode, guard).
3. Correções globais de CSS + atributos de input + modais.
4. `taskboard.shortcuts` + overlay `?` + registros por tela.
5. GREEN + build + revisão manual nos breakpoints.

---

## 21. Rollback Strategy

- Reverter commits por área: (a) viewport/CSS global, (b) modais,
  (c) atributos de input, (d) JS de atalhos — cada uma independente.

---

## 22. Risks and Mitigations

| Risco | Mitigação |
|---|---|
| `Ctrl+K` colide com atalho do browser | Browser modernos não reservam Ctrl+K como crítico (foca omnibox em alguns) — avaliar na auditoria; substituir por `Ctrl+/` se conflitar. |
| xterm/editor capturam teclas | Guard `.xterm-helper-textarea` + `[contenteditable]` já cobre. |
| Auditoria sem device físico | DevTools emulation + `pointer: coarse` media query; registrar limitação na matriz. |
| Regressão visual desktop | Mudanças restritas a media queries mobile e atributos — desktop intocado por padrão. |

---

## 23. Definition of Done

- [x] `docs/mobile-audit.md` (ou equivalente) com matriz completa.
- [x] Todas as telas ok a 360px — contrato source-level (`overflow-x: clip`,
      wrappers projetados) + auditoria registrada; passada visual em device
      real fica como verificação manual pós-merge.
- [x] Teclado virtual: viewport meta + atributos + sem overlap.
- [x] Atalhos implementados, documentados no overlay e inertes em
      campos editáveis.
- [x] Testes source-level verdes; build limpo.

## Execution Notes (2026-10-02, slice #424)

- FR-005: `Fullscreen="ModalFullscreen.SmallDown"` em todos os 15 `<Modal>`
  (KanbanBoard já tinha 3; adicionado em Agents ×3, AiChat ×4, Skills,
  Cockpit, Specs ×2, ApprovalGateModal).
- FR-006: `enterkeyhint="search"`+`data-shortcut-search` nas buscas
  (KanbanBoard, ProviderChat, Skills, Specs); `enterkeyhint="send"` +
  `data-shortcut-command` nos composers; `inputmode="url"`/`"decimal"` e
  hints de autocomplete/autocapitalize/spellcheck nos campos de Settings e
  Login.
- FR-007: `taskboardShortcuts` em `taskboard.js` — listener único em
  `document`, guard `_isEditable` (input/textarea/select/contenteditable/
  xterm/monaco/cm), overlay `?` lista apenas atalhos com alvo vivo;
  `Ctrl+K`/`Cmd+K`, `s`, `n` via `data-shortcut-*`.
- FR-001–003 (site.css): `overflow-x: clip` <576px; `.form-control`/`form-select`
  → `1rem` <768px; alvos ≥44px em `(pointer: coarse)`/`(hover: none)`;
  `.app-shell` ganha `100dvh`; composer usa `env(keyboard-inset-height)`.
- Testes: `Blazor/MobileResponsiveTests.cs` — 6 testes source-level verdes;
  suite unitária 1419/1419 verde.
- Matriz de auditoria: `docs/mobile-audit.md` (source-level; passada em
  device físico pendente, registrada como limitação).

---

## 24. Key Reminder

> The SPEC is the contract. Do not expand scope beyond responsividade,
> teclado virtual e atalhos — sem redesign visual ou novos endpoints.

---

## Audit Note (gap-analysis 2026-10-02)

ACs já satisfeitos vs pendentes — evidência do código em `main`:

- Satisfeito: meta viewport com `interactive-widget=resize-contain` em `wwwroot/index.html` (FR-006 parcial).
- Pendente: `inputmode`/`enterkeyhint` nos inputs de chat (0 ocorrências).
- Pendente: `modal-fullscreen` / comportamento fullscreen de modais <576px (FR-005).
- Pendente: overlay de atalhos com `?` e conjunto FR-007.
- Pendente: auditoria `overflow-x` documentada (FR-008) e testes source-level (§18).

Executar na slice #424.

## Pending Questions

1. Conjunto de atalhos FR-007 está correto, ou há atalhos adicionais
   desejados (ex.: navegação entre telas)?
2. `Ctrl+K` deve ser mantido mesmo podendo colidir com omnibox do
   browser, ou preferir `Ctrl+/`?
3. Em telas com tabelas densas (Jobs, FinOps): preferência por
   `.table-responsive` com scroll ou conversão para cards empilhados
   <576px — por tela ou regra global?
4. Existe algum device/viewport alvo específico além de 360px+?

---

## Human Approval Checklist

- [ ] Escopo de telas aprovado.
- [ ] Conjunto de atalhos aprovado.
- [ ] Padrões de teclado virtual (viewport + atributos) aceitos.
- [ ] Largura mínima de 360px confirmada.
