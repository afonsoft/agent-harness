# SPEC-20261001-chat-ux-compact: layout compacto, status de execução e mini-terminal

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | AI Chat Compact UX (status chips, mini-terminal, dynamic rendering) |
| Product / System | agent-harness |
| Module / Bounded Context | Presentation |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261001-chat-ux` |
| Technical owner | afonsoft |
| Status | Done — entregue via [PR #411](https://github.com/afonsoft/agent-harness/pull/411) (merged 2026-09-30) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Related SPECs | SPEC-20260930-mobile-responsive-ui, SPEC-20261001-chat-default-mode, SPEC-20261001-chat-agent-delegation, SPEC-20261001-chat-mcp-client, SPEC-20261001-chat-skills-slash-commands |

---

## 1. Executive Summary

### Problem

`/ai-chat` tem header pesado (título + descrição + mode select + CLI
select + model button + badges + contexto) que em mobile consome ~40%
da viewport; o ProviderChat tem sidebar própria que duplica o rail do
host; tool cards só aparecem **depois** do resultado — durante uma
chamada `shell_exec` de 20s não há indicação do que está rodando; o
markdown não tem syntax highlight nem diferenciação visual por tipo
(bash vs texto vs tool output); e não existe um lugar para ver a saída
viva de um comando/agente.

OpenCode/ZCode resolve com conversation timeline + tool-call rows com
status live + side pane de run; Open WebUI com chips de status
("Searching…", "Running tool…"); aaPanel renderiza saída de terminal
inline.

### Objective

- **Header compacto**: uma linha — botão histórico, título do thread,
  chip de status, menu "⋯" com os seletores.
- **Histórico sempre oculto** atrás de botão/drawer (mobile = overlay,
  já padrão do rail).
- **Status live**: chip animado mostrando `Running tool shell_exec…`,
  `Running MCP github/get_file…`, `Running agent…`, `Waiting
  permission…` enquanto processa.
- **Mini-terminal**: painel colapsável que abre quando um comando ou
  agente roda, com saída stream estilo terminal.
- **Renderização dinâmica**: syntax highlight em code blocks, estilo
  próprio para bash/tool output/erros, cores seguindo o tema.

### Expected outcome

Ver seção 5/ACs. Nenhuma mudança de protocolo de negócio — os eventos
novos são `chat.status`/`chat.activity` no SSE existente.

### Out of scope

- Terminal interativo (input livre) — o mini-terminal é read-only de
  saída nesta iteração; input de confirmação usa o fluxo de permissão
  existente.
- Reimplementação do xterm.js para agent runs (o `PtyThreadPane`
  continua sendo a view completa; o mini-terminal é um resumo inline).
- Temas customizáveis pelo usuário.

---

## 2. Agent Role

> Blazor/CSS engineer — componentes, streaming UI, JS interop leve,
> mobile-first.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Sem nova lib de componentes; highlight via `highlight.js` (CDN-free —
  bundle em `wwwroot/lib`) ou renderização server-side com classes
  estáveis — decisão na seção de design; não usar npm build step novo.
- ANSI escape codes de saída de tool devem ser sanitizados — nunca
  injetar raw ANSI no DOM.
- Respeitar `prefers-reduced-motion` nos spinners/progress.

---

## 4. Product Context

### Technical context

- `AiChat.razor` (~1623 linhas): rail drawer (`OpenRailDrawerAsync` +
  scrim), header com 4+ controles, tool cards (`ToolCallCard`,
  `ToolCallChangesCard`), `.ai-chat-spinner` existe.
- `ProviderChat.razor` (~475 linhas): sidebar própria
  (`provider-chat-sidebar`), header com selects, `<details>` por tool,
  `MarkdownRenderer.ToSafeHtml` (Markdig + sanitizer, sem highlight).
- `site.css` — classes `ai-chat-*`, `provider-chat-*` existentes.
- `taskboard.js` — hosts JS interop (scroll, keyboard).
- SSE: `chat.tool_call`, `chat.tool_result`, `chat.delta`,
  `chat.done` — estendido com `chat.status`.

### Relevant files

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Blazor/Components/Chat/ProviderChat.razor`
- `src/Taskboard.Blazor/Components/Chat/ChatActivityPanel.razor` (novo)
- `src/Taskboard.Blazor/Components/Chat/ChatStatusChip.razor` (novo)
- `src/Taskboard.Blazor/Components/Chat/SlashCommandPalette.razor`
  (SPEC-skills)
- `src/Taskboard.Client/wwwroot/css/site.css`
- `src/Taskboard.Client/wwwroot/js/taskboard.js`
- `src/Taskboard.Application.Contracts/Rendering/MarkdownRenderer.cs`

---

## 5. Functional Requirements

### FR-001: Header compacto

Uma linha de 44–48px (mobile) / 40px (desktop):

```
[☰ histórico] [título do thread ⌄]    [status chip] [⋯]
```

- `⋯` abre popover com: mode select, CLI select, model select, session
  mode, context info — os controles que hoje ocupam o header.
- Título truncado com `⌄` abre menu: renomear, nova conversa, exportar.
- Abaixo de 576px os labels "Provider"/"Model" somem — só ícones.

### FR-002: Histórico oculto por default

- Nenhum rail visível ao abrir `/ai-chat` (qualquer viewport); o botão
  ☰ abre drawer sobre o conteúdo (mobile) ou rail lateral ≤320px
  (desktop ≥1200px, opcional pin).
- ProviderChat: remover a sidebar própria — o histórico de providers
  migra para o mesmo drawer do host (uma lista unificada marcando a
  origem: `chat`/`provider`/`agent`), evitando dois históricos
  paralelos na mesma tela.
- Fechar o drawer nunca desmonta o thread ativo; `Esc` e toque no scrim
  fecham.

### FR-003: Status de execução (`chat.status`)

Novo evento SSE no stream do `ChatService`:

```json
{ "phase": "thinking|running_tool|running_mcp|running_agent|running_subagent|waiting_permission|idle",
  "label": "shell_exec", "server": "github?", "tool": "get_file?",
  "elapsed_ms": 1230 }
```

- `ChatStatusChip` fixo acima do composer: spinner + label +
  elapsed. Vazio/`idle` some.
- Fases colorem o chip: tool=info, mcp=violet, agent=warning,
  permission=danger pulsante, erro=danger.
- O mesmo evento alimenta o ToolCallCard para transição
  pending→done.

### FR-004: Mini-terminal

`ChatActivityPanel.razor` — painel ancorado acima do composer:

- Abre automaticamente (colapsável, ~160px, expansível a 50vh) quando
  começa `shell_exec`, `run_cli`, `code_interpreter`, `run_agent` ou
  MCP tool marcada `exec`.
- Conteúdo: header (ícone kind + nome + elapsed + botão fechar) + corpo
  monospace com stream de stdout/stderr do tool (via resultado parcial
  quando o tool suporta; senão stderr+saída final) + linha de exit code.
- ANSI → HTML sanitizado em renderer dedicado (`AnsiRenderer`) — só
  cores/estilos básicos; demais escapes removidos.
- Até 3 execuções empilhadas; fechado por usuário fica fechado até a
  próxima execução (reabre só se havia fechado por auto-dismiss).
- Agente (`run_agent` wait=true): mesmo painel mostra últimas linhas do
  log do run + link "abrir terminal completo" → view existente.

### FR-005: Renderização dinâmica

- `MarkdownRenderer`: preservar `class="language-x"` nos `<code>` de
  fence (Markdig já emite; sanitizer precisa permitir `class` em
  `code`/`pre` — hoje `class` está na allowlist de atributos).
- Highlight client-side: `taskboard.js` `highlightChatCode(el)` usando
  highlight.js vendored em `wwwroot/lib/highlight/` (~120KB subset:
  bash, cs, js, ts, json, xml, python, sql, diff); chamado após cada
  render de mensagem/stream.
- Tokens visuais (CSS vars):
  - `--chat-bash-bg`/header: bloco ```bash``` ganha header "terminal"
    com ícone `$` e fundo console.
  - tool output: card `<details>` (mantém) + corpo `.chat-tool-output`
    com max-height + scroll.
  - erros/warnings: `text-bg-danger`/`warning` já; adicionar ícone.
  - menções a arquivos (`src/...`) viram `<code>` estilizado via
    extensão de inline — opcional/fase 2 (listada como nice-to-have).
- Light/dark: todas as cores via CSS vars — sem cor fixa.

### FR-006: Composer

- Composer sticky com `env(safe-area-inset-bottom)` +
  `interactive-widget=resizes-content` (SPEC-mobile) — nunca coberto
  pelo teclado virtual; status chip e mini-terminal ficam **acima** do
  composer, dentro da área visível.
- Textarea auto-grow até 40vh; `enterkeyhint="send"`; `Enter` envia,
  `Shift+Enter` quebra linha; botão stop enquanto `_running`.

---

## 6. Business Rules

- Eventos `chat.status` são informativos — falha na renderização nunca
  interrompe o stream de texto.
- Drawer fechado nunca pausa run/tool ativa.
- Mini-terminal nunca vira input — permissões continuam no fluxo de
  permission prompt existente.

---

## 7. Expected Architecture

```
ChatService stream ── SSE ──► ProviderChat/AiChat
   chat.status              ├─ ChatStatusChip (fase+elapsed)
   chat.tool_call/result    ├─ ToolCallCard (pending→done)
   chat.activity (chunks)   └─ ChatActivityPanel (mini-terminal stream)

MarkdownRenderer (server, sanitiza) ──► DOM ──► taskboard.js
                                          highlightChatCode()
```

---

## 8. Edge Cases

- Múltiplas tools em paralelo no mesmo turno → chip mostra a mais
  recente + contador `(2)`; painel empilha execuções.
- Tool rápida (<300ms) → chip não pisca: só renderiza se
  `elapsed>300ms` ou status ainda ativo.
- Resize/teclado aberto → composer+chip permanecem visíveis
  (visualViewport fallback JS para iOS antigo).
- Sem JS highlight → code blocks continuam legíveis (graceful).
- Mensagem gigante → highlight aplica só em blocos `<pre>` visíveis
  (IntersectionObserver) se houver regressão de perf.

---

## 9. Non-Functional Requirements

- First paint do composer+header ≤ atual (sem novos assets blocking;
  highlight.js carrega defer).
- Sem overflow horizontal a 360px; toques ≥44px.
- `prefers-reduced-motion`: spinner vira indicador estático.

---

## 10. Expected Tests

- `Dado_StatusRunningTool_Quando_Stream_Entao_ChipExibeLabel`
- `Dado_ToolExec_Quando_Inicia_Entao_MiniTerminalAbre`
- `Dado_PainelFechado_Quando_NovaExecucao_Entao_Reabre`
- `Dado_MarkdownComBash_Quando_Render_Entao_PreComLanguageClass`
- `Dado_AnsiNaSaida_Quando_Render_Entao_EscapesSanitizados`
- Source-level: header tem 1 linha de ações; sem sidebar dupla no
  ProviderChat.

---

## 11. Acceptance Criteria

1. Header de 1 linha; seletores movidos ao popover `⋯`.
2. Histórico só via drawer; ProviderChat usa o mesmo drawer unificado.
3. Chip mostra "Running tool X"/"Running MCP s/t"/"Running agent" live.
4. Mini-terminal abre em execução e renderiza saída com cores.
5. Code blocks com highlight; bash distinto; tema claro/escuro ok.
6. 360px + teclado virtual: composer/visíveis ok; sem overflow.

---

## 12. Implementation Plan

1. `chat.status` SSE + `IChatActivityReporter` (backend).
2. `ChatStatusChip` + wiring no stream handler.
3. `ChatActivityPanel` + `AnsiRenderer`.
4. Header compacto + drawer unificado.
5. `class` no sanitizer + highlight.js vendored + `highlightChatCode`.
6. Testes + build + revisão mobile.

---

## 13. Rollback Strategy

- Cada pedaço é isolável: chip/painel/highlight podem ser revertidos
  separadamente; layout volta ao atual com o revert do header.

---

## 14. Risks and Mitigations

| Risco | Mitigação |
|---|---|
| highlight.js +120KB | subset vendored + defer; fallback sem JS ok |
| ProviderChat merge de histórico | manter endpoints; só a UI consolida |
| ANSI sanitização | allowlist pequena (cores SGR) + strip restante |

---

## Pending Questions

1. Mini-terminal read-only ok, ou quer input de confirmação inline?
2. Highlight.js vendored ok (sem npm build)?
3. Rail lateral desktop fixo (pin opcional) ou sempre drawer?

---

## Human Approval Checklist

- [ ] Layout de 1 linha + popover aprovado.
- [ ] Histórico unificado (ProviderChat sem sidebar própria) aprovado.
- [ ] Novos eventos `chat.status`/`chat.activity` aceitos.
- [ ] highlight.js vendored aceito.
