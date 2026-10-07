# Gap Analysis — 20261007

- Repository: /home/ubuntu/repos/agent-harness | Branch: main | Commit: 7c7b2c4
- Phase reached: verdicts (gate pendente)
- Mode: analyze — foco do pedido: "chat no Harness ficar parecido com Devin Web" —
  executar tarefas, abrir "talk" (voz/thread), rodar aplicações, executar testes;
  layout + funcionalidade; OpenHands como segunda referência; pesquisa web sobre
  agentes autônomos.
- Referências externas: clone local `~/repos/OpenHands` (v1.10.0),
  docs.devin.ai (session tools + release notes), docs.openhands.dev (Agent Canvas).
- Decisão do dono: **execução no host, sandbox fica para fase posterior.**
- Doc irmão com a anatomia completa: `.specs/COMPARISON-devin-web-openhands-harness-chat.md`.

## 1. Source inventory

| Source | Status | Notes |
| --- | --- | --- |
| `.specs/` | present | ~250 specs; série chat recente completa (runs, approvals, plan-mode, attachments, fork/steering, jobs/schedules/FTS, context-mgmt) |
| `ProviderChat.razor` / `AiChat.razor` | present | coluna única: rail + mensagens + composer; modos Chat/Agent |
| `IChatTool` registry | present | ~35 tools; `todo` (ChatTodoStore) existe mas **sem UI** |
| `TerminalHub`/`PtyThreadPane` | present | xterm PTY no host/docker; in-chat só para threads legacy `Kind=terminal` |
| `/terminal`, `/editor`, `GitDiffViewer`, `AgentRunTimeline` | present | superfícies existem, **fora** do chat |
| OpenHands | inspected | tabs Planner/TaskList/Changes/VSCode/Terminal/Browser; AgentState machine; security analyzer; condenser; event stream WS |
| Devin docs | fetched | Shell/IDE/Browser-Computer/Files/Terminal/Preview tabs, ⌘K palette, PR hover cards, wake-on-tab |

## 2. AS-IS × TO-BE matrix

| Topic | AS-IS | TO-BE | Sources |
| --- | --- | --- | --- |
| Chat layout | coluna única (rail + thread + composer) | chat + painel de workspace à direita, tabs por run (host) | Devin tabs; OpenHands `ConversationMain` |
| Run worktree surfaces | diffs só por tool-call (ToolCallCard) e deliverables | aba **Changes** unificada (diff + commits do worktree) | OpenHands Changes; `GitDiffViewer` + `GetDiffAsync` |
| Terminal | `/terminal` separado; PtyThreadPane só p/ threads legacy | aba **Terminal** do run, PTY no worktree (host) | Devin Terminal; PtyThreadPane reusa TerminalHub |
| Editor | `/editor` (code-server iframe) separado | aba **Editor** embutida no workspace do run | Devin IDE; OpenHands VSCode tab |
| Tasks do agente | tool `todo` + `ChatTodoStore` sem render | aba **Tasks** + strip no header | OpenHands task_tracker + TaskList tab; WorkspaceTools.cs:591 |
| Preview de app | não existe | aba **Preview** com element picker → quote no composer | Devin Preview (release notes 10-05) |
| Browser do agente | não existe | aba **Browser** (screenshots via browser_use); interativo depois | OpenHands browser tab |
| Controle do run | stop/cancel + fila; sem pause no chat | pause/resume + state chip + STUCK watchdog | OpenHands AgentState; `runs/{id}/pause` só p/ orchestration (Program.cs:1537) |
| Aprovações | ask/never/allow por tool + plan-review | preset `auto` com classificador de risco (LOW/MED/HIGH) | OpenHands security analyzer + confirmation mode |
| Git na conversa | promote→PR via delegation; sem barra | chips repo/branch/pull/push/PR + PR hover card | OpenHands git-control-bar; Devin PR cards |
| Overview da conversa | linha de contexto (repo/workspace) | peek panel: workspace, git, skills/MCP, políticas, modelo | OpenHands conversation overview |
| Sugestões pós-run | não existe | chips de próximas ações | OpenHands chat-suggestions |
| Voz ("talk") | não existe | mic no composer (MediaRecorder → transcrição) | Devin voice |
| Comando global | slash palette no composer | ⌘K palette + atalhos (mark done, new chat, jump tab) | Devin ⌘K |

## 3. Candidates and verdicts

| Key | Category | Verdict | Priority | Evidence |
| --- | --- | --- | --- | --- |
| GAP-chat-workspace-panel | layout | CONFIRMADO | alta | Devin/OpenHands têm painel direito com tabs por run; Harness espalha Terminal/Editor/Diff/Timeline em páginas separadas — o chat não mostra o trabalho. Maior delta de layout. |
| GAP-chat-tasks-surface | layout | CONFIRMADO | alta | `todo`/`ChatTodoStore` existem (WorkspaceTools.cs:591) mas sem UI — o usuário não vê o plano do agente. Render = read endpoint + aba Tasks. |
| GAP-chat-changes-tab | layout | CONFIRMADO | alta | diff só por tool-call; nenhuma visão unificada worktree×base. `GitDiffViewer` + `GitWorktreeManager.GetDiffAsync` prontos para reuso. |
| GAP-chat-terminal-tab | layout | CONFIRMADO | alta | TerminalHub/PTY existem; falta pane bindado ao worktree do run ativo (PtyThreadPane só cobre thread `terminal`). |
| GAP-chat-run-pause | autonomy | CONFIRMADO | média | endpoints pause/resume só p/ orchestration runs; ChatRun sem pause — usuário só pode cancelar. |
| GAP-chat-agent-state-chip | autonomy | CONFIRMADO | média | header tem `_running` bool + waiting-approval; falta máquina de estados visível (paused/stuck/waiting) estilo OpenHands `AgentState`. |
| GAP-chat-risk-approvals | autonomy | CONFIRMADO | alta | política por tool existe; falta classificação por-chamada (rm -rf ≠ ls). `auto` = LOW auto + MED notice + HIGH ask é o modo autônomo seguro sem sandbox. |
| GAP-chat-git-bar | layout | CONFIRMADO | média | OpenHands tem git-control-bar (repo/branch/pull/push/PR); promote→PR existe mas não no chat. |
| GAP-chat-overview-panel | layout | CONFIRMADO | baixa | context line existe; painel com workspace/git/skills/MCP é incremental. |
| GAP-chat-preview-tab | layout | CONFIRMADO | média | não há preview de app; Devin element-picker dá contexto visual direto p/ o chat. |
| GAP-chat-browser-tool | autonomy | CONFIRMADO | baixa | sem `browser_use` (Playwright): agente não pode testar UI que ele mesmo sobe; alimenta aba Browser (screenshots). Fase 1 só screenshot. |
| GAP-chat-suggestions | ux | CONFIRMADO | baixa | sem suggestion chips pós-run. |
| GAP-chat-command-palette | ux | CONFIRMADO | baixa | só slash palette no composer; sem ⌘K global. |
| GAP-chat-voice-input | ux | CONFIRMADO | baixa | sem mic no composer ("abrir um talk" do pedido — interpretado como voz/thread: side-chat já existe via fork). |
| GAP-sandbox-runtime | architecture | REJEITADO (fase posterior) | — | dono decidiu host-first; VM/container por run fica para fase futura. Boundary de segurança hoje = approval policies + risk classifier + worktree jail + gateway. |
| GAP-chat-detached-runs | autonomy | REJEITADO | — | coberto: ChatRunDispatcherService detached + FIFO + SSE reattach + push (SPEC-20261005-chat-background-resume). |
| GAP-chat-todo-tool | requirements | REJEITADO | — | tool já existe (todo/ChatTodoStore); o gap é a UI (GAP-chat-tasks-surface). |
| GAP-chat-approvals-base | autonomy | REJEITADO | — | policies ask/never/allow + plan-review + permission cards existem; falta só a camada de risco. |
| GAP-chat-context-mgmt | autonomy | REJEITADO | — | condenser + fork/steering + pressure meter existem. |

## 4. Prioridades (CONFIRMADO)

- **P1**: workspace-panel (Tasks + Changes + Terminal), risk-approvals (auto preset)
- **P2**: run-controls (pause/resume + state chip + stuck), git-bar + overview, preview-tab
- **P3**: browser-tool (screenshot), suggestions, ⌘K palette, voice
- **REJEITADO/fase posterior**: sandbox runtime; browser interativo (click-through)

## 5. Gate → SPECs + Issues (APROVADO 2026-10-07)

Draft SPECs criados (7 slices):
- `.specs/SPEC-20261011-chat-workspace-panel.md` — painel direito: tabs Tasks/Changes/Terminal/Editor/Plan → issue #522
- `.specs/SPEC-20261012-chat-run-controls.md` — pause/resume, state chip, stuck watchdog → issue #523
- `.specs/SPEC-20261013-chat-risk-approvals.md` — classificador de risco + preset `auto` → issue #524
- `.specs/SPEC-20261014-chat-git-bar-overview.md` — chips git + promote→PR + overview peek → issue #525
- `.specs/SPEC-20261015-chat-preview-panel.md` — Preview + element picker → issue #526
- `.specs/SPEC-20261016-chat-browser-tool.md` — browser_use (screenshot) + aba Browser → issue #527
- `.specs/SPEC-20261017-chat-polish.md` — suggestions, ⌘K, voice → issue #528

Epic: #521 · slices #522–#528 (label `backlog`) · specs Draft em PR docs-only.

## 6. Pendências

- Gate aprovado → specs + issues criados; próximo: implementar slice 1 (`chat-workspace-panel`) branch/PR próprios.
- Ambiguidade "abrir um talk": interpretado como voice input + side-chat (fork cobre thread paralela) — confirmar se era outra coisa.
- Preview tab depende de haver URL de app no run (hoje nenhum run expõe porta) — spec deve definir convenção (run anuncia porta → chip no header).
- Risk classifier: usar regras estáticas primeiro (path jail, comandos destrutivos, rede); LLM-judge é over-engineering.
- Execução no host implica que terminal/files tabs operam em worktrees de `GitWorktreeManager` quando o run tem worktree — conversa sem run vê o workspace raiz.
