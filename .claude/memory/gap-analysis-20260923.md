# Gap Analysis — 2026-09-23

- Repository: `/home/ubuntu/repos/agent-harness` | Branch: `fix/devin-20260923-terminal-tabs-keyed-render` | Tree: clean
- Phase reached: `done` (aprovado → issues → implementado → PRs abertos)
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

## 7. Resultado

- Gate: **aprovado** pelo usuário (2026-09-23) — SPECs `Approved`.
- Issues: Epic **#338** (`epic`,`todo`) + slices **#339** (focus-mode) e **#340** (keybar, dependência registrada no body — `gh` local não suporta `--add-linked-issue`).
- Implementação:
  - `feature/devin-20260923-terminal-focus-mode` → **PR #341** (base main): overlay CSS `html[data-terminal-focus]`, topbar off, tab strip compacta, floating restore, hack `100dvh` removido, refit via `fitNow`, `Esc` não interceptado, cleanup no dispose.
  - `feature/devin-20260923-terminal-virtual-keybar` → **PR #342** (empilhado em #341): `.terminal-keybar` só em focus + `(pointer:coarse),(hover:none)`; sequências ANSI via hub `Input`; Ctrl sticky one-shot (`ch & 0x1f`, `v`→paste); Paste dedicado `navigator.clipboard.readText`→`term.paste` + hint fallback; refocus após toque; desarma na troca de aba.
- Issues #339/#340 → `in_pullrequest`; epic atualizada com links dos PRs.
- Verificação: `dotnet build` 0 warn/0 err; unit 1128/1128. Manual touch/desktop pendente (não há harness de UI — evidência manual nos test plans dos PRs).
- SPEC statuses: `Implemented` (aguardando merge) em ambas as branches.

## 8. Pendências não-spec (housekeeping)

- `followups.md`: ADR da escolha Spectre.Console.Cli e revisão de UX-diff da CLI ainda abertos (não bloqueantes).
- SPEC-20260923-terminal-tabs-keyed-render marcado `Implemented` (PR #337) — branch atual; nenhum PR aberto no gh (provável já mergeado; verificar no próximo ciclo).
