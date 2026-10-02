# Mobile Audit — SPEC-20260930-mobile-responsive-ui (FR-008)

Audit executed 2026-10-02 (slice #424). Method: **source-level audit** of
`.razor` markup + `site.css` media queries — no physical device or DevTools
emulation was run; columns below record the contract enforced in code/CSS
and the source-level result. A manual pass at 360/768/1280px on a device or
emulator is recommended as follow-up verification.

## Global contracts applied

| Contract | Where | Evidence |
|---|---|---|
| `interactive-widget=resizes-content` viewport | `index.html` | meta tag already present; zoom preserved (no `user-scalable`/`maximum-scale`) |
| No `body` `overflow-x` at ≥360px | `site.css` | `overflow-x: clip` guard under `max-width: 575.98px` |
| Touch targets ≥44px | `site.css` | `@media (pointer: coarse), (hover: none)` — `.btn`, `.nav-link`, `.page-link`, `.dropdown-item`, `.list-group-item-action`, `.btn-close`, `.nav-tabs .nav-link` |
| Inputs ≥16px <768px (iOS zoom) | `site.css` | `max-width: 767.98px` — `.form-control`, `.form-select` → `1rem` |
| Modals fullscreen <576px | markup | `Fullscreen="ModalFullscreen.SmallDown"` on **every** `<Modal>` (15 total) |
| Virtual keyboard attrs | markup | `inputmode`/`enterkeyhint`/`autocomplete`/`autocapitalize`/`spellcheck` per field type |
| Global shortcuts | `taskboard.js` | `taskboardShortcuts` — `?`, `Esc`, `Ctrl+K`, `s`, `n` via `data-shortcut-*` targets |

## Screen matrix

| Screen | Route | Findings / fix applied |
|---|---|---|
| Login | `/login` | `autocomplete="username"` / `current-password` added. |
| Home (dashboard) | `/` | Cards flow via Bootstrap grid; no fixed-width markup found. |
| Kanban board | embedded in Home/GitHub | Modals already `SmallDown`; filter input got `enterkeyhint="search"` + `data-shortcut-search`; "Nova Tarefa" got `data-shortcut-new`. |
| Cockpit | `/cockpit` | "New Run" button → `data-shortcut-new`; start modal → `SmallDown`; budget input → `inputmode="decimal"`. |
| CockpitRun | `/cockpit/runs/{id}` | Log viewers scroll internally; approval gate modal → `SmallDown`. |
| AI Chat | `/ai-chat` | Composer → `enterkeyhint="send"`, `autocomplete="off"`, `data-shortcut-command`; 4 modals (repo/model/sandbox/run-agent) → `SmallDown`; composer sticks via `keyboard-inset-height`/`safe-area-inset` padding + `100dvh` shell. |
| Provider chat | component | Composer `enterkeyhint="send"` + `data-shortcut-command`; conversation search → `enterkeyhint="search"` + `data-shortcut-search`. |
| Agents | `/agents` | 3 modals (install/models/custom CLI) → `SmallDown`. |
| Skills | `/skills` | Search → `enterkeyhint="search"` + `data-shortcut-search`; skill modal → `SmallDown`. |
| Specs | `/specs` | Search → `type="search"` + `enterkeyhint` + `data-shortcut-search`; detail/run modals → `SmallDown`. |
| Settings | `/settings` | Key/token fields (`github-token`, `rag-key`, `chat-provider-key`, `chat-search-key`) → `autocomplete="off"`/`autocapitalize="none"`/`autocorrect="off"`/`spellcheck="false"`; URL fields (`rag-url`, `chat-provider-url`, `chat-search-url`) → `inputmode="url"`; password change → `autocomplete` hints; tab strip already horizontally scrollable. |
| Terminal | `/terminal` | xterm viewport is a designed internal scroller; keybar already touch-aware (`pointer: coarse`). |
| Gantt | `/gantt` | Timeline keeps designed internal horizontal scroll (allowed by FR-001). |
| Workflow | `/workflow` | Designer canvas keeps designed pan/scroll. |
| VS Code editor | `/editor` | Full-viewport iframe embed — out of markup scope. |
| Jobs | `/jobs` | Numeric override inputs → compact; tables inherit `.table-responsive` where >4 cols (verified in markup). |
| FinOps | `/finops` | Dense tables reviewed — keep `.table-responsive` wrapper (allowed internal scroll). |
| Prompts | `/prompts/...` | Read-heavy page; no findings. |

## Shortcuts (FR-007)

Active only when a `data-shortcut-*` target exists on the screen; all are
inert inside editable fields (`input`, `textarea`, `select`,
`contenteditable`, `.xterm-helper-textarea`, `.monaco-editor`, `.cm-editor`).

| Key | Action | Targets wired |
|---|---|---|
| `?` | Toggle shortcut help overlay | global |
| `Esc` | Close overlay | global |
| `Ctrl+K` / `Cmd+K` | Focus command/composer (fallback: search) | AiChat, ProviderChat composers |
| `s` | Focus first search input | KanbanBoard, ProviderChat, Skills, Specs |
| `n` | Trigger primary "new" action | Cockpit "New Run", KanbanBoard "Nova Tarefa" |

## Known limitations

- Audit is source-level; real-device pass (iPhone SE / Android 360px) pending.
- `Ctrl+K` may conflict with browser omnibox focus in some browsers; targets
  are wired via `data-shortcut-command` so rebinding is a markup change only.
- Sequenced shortcuts (`g h`) intentionally out of scope per SPEC.
