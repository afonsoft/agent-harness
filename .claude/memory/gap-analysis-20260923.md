# Gap Analysis — 2026-09-23

- Repository: `/home/ubuntu/repos/agent-harness` | Branch: `fix/devin-20260923-terminal-tabs-keyed-render` | Tree: clean
- Phase reached: `specs-written` (aguardando gate de aprovação)
- Mode: `focused` (terminal/mobile UX — pedido explícito do usuário) + sweep leve do restante

---

## 1. Source Inventory

| Source | Status | Notes |
|---|---|---|
| `.specs/` | `present` | 100+ SPECs; nenhum cobre terminal mobile/focus/keys |
| `docs/` | `present` | |
| `.claude/memory/` | `present` | 10 reports anteriores; último 2026-09-22 all-delivered |
| `.claude/rules/` · `AGENTS.md` | `present` | |
| Tests/CI | `present` | baseline 2026-09-22: unit 1017/1017, integration 261/262, cov 77.85% |
| `gh` auth + remotes | `ok` | 0 PRs abertos; 1 issue aberta (#318 spec-status drift, in-progress) |

## 2. Dedup

- #318 (spec status drift epic) → DUPLICADO, já tracked.
- SPEC-20260918-touch-targets cobre targets ≥44px → não é gap novo.
- Paste/copy de teclado coberto por SPEC-20260917-terminal-tabs + SPEC-20260921-terminal-paste-dedup.
- Sidebar mobile já é offcanvas oculta <992px (`MainLayout.razor:9`) → não é gap.

## 3. Candidatos e Veredictos

| Key | Categoria | Veredito | Prioridade | SPEC |
|---|---|---|---|---|
| `GAP-frontend-terminal-focus-mode` | implementation/frontend | **CONFIRMADO** | alta (pedido do usuário) | `.specs/SPEC-20260923-terminal-focus-mode.md` (Draft) |
| `GAP-frontend-terminal-virtual-keybar` | implementation/frontend | **CONFIRMADO** | alta; depende do anterior | `.specs/SPEC-20260923-terminal-virtual-keybar.md` (Draft) |
| Sidebar recolhível no mobile | implementation | REJEITADO | — | offcanvas-lg já oculta <992px; hamburger abre |
| Touch targets ≥44px | implementation | REJEITADO | — | SPEC-20260918-touch-targets |
| Copy/paste teclado | implementation | REJEITADO | — | `terminal.js` attachKeys/attachPaste |
| Spec status drift | automation | DUPLICADO | — | issue #318 |
| `RunTerminal.razor` read-only sem focus | implementation | REJEITADO (escopo) | — | usuário excluiu do escopo; candidato a follow-up |

## 4. Evidências (AS-IS)

- `Terminal.razor:11-12` — `<h1>` + descrição sempre renderizados; sem toggle de layout.
- `MainLayout.razor:28-46` — `.app-topbar` 3.5rem sempre visível (hamburger+title+settings+logout).
- `site.css:1191-1196` — hack `@media ≤575.98px { .terminal-host { height:100dvh } }` estoura viewport (topbar+título+tabs somam acima de 100dvh → página rola).
- `terminal.js` — sem helper de input virtual nem `pasteClipboard`; só `attachPaste` por evento de clipboard.
- Nenhum `fullscreen`/`requestFullscreen`/keybar em `src/` (grep binários excluídos).
- `taskboard.js` — padrão `setSidebarCollapsed` (data-attribute no `<html>`) reutilizável para `setTerminalFocus`.

## 5. Decisões de design (confirmadas pelo usuário)

- Focus esconde topbar + título/descrição; mantém tab strip compacta; botão flutuante de sair.
- Toggle de expandir em todos os dispositivos; keybar só em focus + `(pointer:coarse),(hover:none)`.
- Keybar: Esc, Tab, Shift+Tab, setas, Home/End/PgUp/PgDn, Ctrl sticky, Paste dedicado.
- Sem Fullscreen API (CSS-only `html[data-terminal-focus]`); sem persistência; `Esc` não interceptado.
- Cockpit `RunTerminal` fora de escopo.

## 6. Draft SPECs gerados (aguardando gate)

- `.specs/SPEC-20260923-terminal-focus-mode.md`
- `.specs/SPEC-20260923-terminal-virtual-keybar.md` (depende do anterior)

## 7. Pendências não-spec (housekeeping)

- `followups.md`: ADR da escolha Spectre.Console.Cli e revisão de UX-diff da CLI ainda abertos (não bloqueantes).
- SPEC-20260923-terminal-tabs-keyed-render marcado `Implemented` (PR #337) — branch atual; nenhum PR aberto no gh (provável já mergeado; verificar no próximo ciclo).
