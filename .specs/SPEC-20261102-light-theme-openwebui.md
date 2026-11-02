# SPEC-20261102-light-theme-openwebui — Tema claro padrão alinhado ao OpenWebUiCode

| Campo | Valor |
| --- | --- |
| Status | **In Progress** |
| Owner | @afonsoft |
| Ticket | #688 |
| Área | UI / Design tokens |

## Contexto

`src/Taskboard.Client/wwwroot/css/tokens-clean.css`
(SPEC-20261023-openwebui-layout-enhancement) unificou os tokens com a estética
OpenWebUI **na variante dark** (`#0f172a`/`#1e293b`, `color #f1f5f9`), aplicada
com `!important` em `body`, `.card`, `.form-control`, `.nav-pills .nav-link`.
O `index.html` já declara `meta color-scheme=light` — inconsistência.

Efeitos observados:

- UI inteira escura; usuário reporta "ruim de navegar".
- `.nav-pills .nav-link` recebe `color/bg !important` → o estado `.active` da
  sidebar (`primary-bg-subtle` + `primary-text-emphasis` em `site.css`) é
  sobrescrito e fica invisível.
- `index.html` referencia `github-dark.min.css` (highlight.js) — code blocks
  escuros.

## Evidência

- AS-IS: `tokens-clean.css` com slate dark; `.nav-pills .nav-link` dentro do
  bundle `!important`; fallbacks `#0d1117`/`#161b22` em `.shortcut-help-card`.
- TO-BE: paleta clara espelhando `~/repos/OpenWebUiCode`
  (`MainLayout.razor`: `bg-white` + `text-gray-700`; `Sidebar.razor`:
  `bg-white` + `border-gray-50`, hover `gray-100`), nav `.active` visível.

## Requisito

1. Reescrever `tokens-clean.css` com rampa gray oklch do OpenWebUiCode:
   `bg-main/sidebar/surface` = `#fff`, `bg-tertiary` = gray-50,
   `bg-hover` = gray-100, `border-subtle` = gray-200, `border-strong` =
   gray-300, texto body = gray-700, muted = gray-500, emphasis = gray-900,
   accent `#3b82f6`/`#2563eb`, `color-scheme: light`,
   `letter-spacing: -0.011em` no body.
2. Excluir `.nav-pills .nav-link` do bundle `bg/color !important`
   (manter apenas o radius override).
3. Fallbacks claros em `.shortcut-help-card` / `.shortcut-help-list kbd`.
4. highlight.js: adicionar `lib/highlight/github.min.css` (tema GitHub light)
   e trocar o `<link>` em `index.html`.
5. Terminais, log consoles e keybar **permanecem escuros** (superfícies de
   console; `#0d1117`/`#161b22` preservados nesses seletores).

## Critérios de Aceite

- [ ] `body`/`.app-shell` renderizam fundo `#fff` e texto gray-700.
- [ ] Link ativo da sidebar visível (`primary-bg-subtle`).
- [ ] Code blocks do chat com tema claro (github.min.css).
- [ ] Terminal/xterm e consoles de log continuam escuros.
- [ ] Build + publish + restart de `harness-server` com `/health` Healthy.

## Fora de escopo

- Toggle de tema/dark mode dedicado (não existe UI de preferência hoje).
- Mudança de layout/componentes (apenas tokens e overrides de cor).
