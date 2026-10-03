# SPEC-20261003-ai-code-devin-layout: AI Code estilo Devin + tools de workspace + testes no Docker

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | AI Code Devin-style layout + workspace tools + Docker test stage |
| Product / System | agent-harness |
| Module / Bounded Context | Presentation + Integrations + DevOps |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/1790993711-aicode-devin-layout` |
| Technical owner | afonsoft |
| Status | In Progress |
| Date | 2026-10-03 |
| Target agent | Devin |
| Related SPECs | SPEC-20260922-ai-chat-command-bar, SPEC-20260929-ai-chat-rail-overlay, SPEC-20260929-ai-code-provider-chat, SPEC-20261001-chat-skills-slash-commands, SPEC-20261001-chat-ux-compact |

---

## 1. Executive Summary

### Problem

`/ai-chat` ainda usa selects de texto no command bar e botões Send/Stop/Retry
com rótulos — distante do chat do Devin (toggle segmentado Agent|Ask no topo,
composer em caixa única arredondada com toolbar de ícones e send circular) e
do OpenCode TUI (slash commands, Esc para interromper, coluna de leitura
centralizada). As tools do provider chat não cobrem fluxos de trabalho de
coding agent (edição cirúrgica, busca em arquivos, glob, git read-only,
execução de testes, todo list). Não há como subir testes/validações via
Docker sem montar a solução localmente.

### Objective

- **Header Devin**: `AI Code` + texto de contexto (workspace · repo) +
  toggle segmentado Chat/Assistant/Agent com ícones + botões de ícone
  (History, New).
- **Composer Devin**: caixa única arredondada; textarea em cima; toolbar de
  ícones (CLI, view, contexto, repo, model, sandbox) embaixo; ações
  circulares Send/Stop/Retry à direita.
- **OpenCode parity**: `/` abre palette de slash commands no composer de
  thread (builtins `/new` `/agent` `/tools` `/clear` `/help` + skills);
  `Esc` interrompe o turno do agente; coluna de mensagens centralizada
  (~48rem) estilo chat.
- **Novas tools**: `edit_file`, `search_files`, `find_files`, `git`,
  `run_tests`, `todo` registradas no provider chat.
- **Docker**: stage `test` no Dockerfile (format verify → test) e serviços
  `tests`/`validate` no compose sob profiles opt-in.

---

## 2. Requisitos Funcionais

- **RF-001** — Header: texto de contexto exibe `workspace · repo` do thread
  ativo ou da configuração pendente; em modo provider exibe "Provider chat".
- **RF-002** — Toggle segmentado no topo substitui o select `Mode` do command
  bar; ícones `ChatDots`/`ChatLeftText`/`Robot`; o segmento ativo mostra o
  rótulo (escondido <md). Trocar de modo com thread vinculado a outro modo
  desvincula (próximo Send materializa thread novo); `provider` apenas troca
  a superfície.
- **RF-003** — Composer em `.ai-chat-composer-box`: textarea sem borda,
  toolbar `.ai-chat-toolbar` com controles `.ai-chat-ctl` (ícone + select
  compacto), chips `.ai-chat-chip` quando vinculado, ações `.ai-chat-sendbtn`
  circulares (primary/danger/secondary) em `.ai-chat-toolbar-end`.
- **RF-004** — Slash palette no composer de thread: reutiliza
  `SlashCommandPalette`; builtins executam localmente; `/<skill> args`
  injeta `[skill:name]` + `<skill>` com o conteúdo SKILL.md (mesmo formato
  do ProviderChat).
- **RF-005** — `Esc` no composer interrompe turno de agente em execução
  (equivalente ao botão Stop); `Esc` com palette aberta fecha a palette.
- **RF-006** — `.ai-chat-messages` centralizado em coluna de leitura
  (~48rem) via `padding-inline` calculado; `.ai-chat-composer-box` com
  `max-width: 48rem; margin-inline: auto`.
- **RF-007** — Novas tools do provider chat (IChatTool): `edit_file`
  (search/replace exato, `replace_all` para múltiplos matches),
  `search_files` (regex, path-jailed, saída `path:line`, scrub de segredos),
  `find_files` (glob `**`/`*`/`?`/`{a,b}`), `git` (read-only
  `status|log|diff|branch`), `run_tests` (`dotnet test`, RequiresConfirmation),
  `todo` (lista/checklist persistida por conversa em `<dataDir>/chat-todos.json`).
- **RF-008** — Dockerfile stage `test`: `dotnet format --verify-no-changes` +
  `dotnet test` falham o build da imagem. Compose: `tests` (profile `test`)
  e `validate` (profile `validate`).
- **RF-009** — `edit_file`, `run_tests` e `todo` entram em
  `ChatCapabilityRegistry.MutatingTools`.

## 3. Requisitos Não-Funcionais

- **NFR-001** — Path confinement preservado: novas tools que tocam o
  filesystem validam via `PathJailValidator` (edit_file/search_files/
  find_files/git scoped path).
- **NFR-002** — Saídas de tools passam por `ISecretRedactor` quando podem
  conter segredos (search_files, git, run_tests).
- **NFR-003** — Build com `TreatWarningsAsErrors`; `dotnet format`
  verificado antes do commit (gate de CI).
- **NFR-004** — `.dockerignore` não pode excluir `tests/`/`docs/` (o stage
  de teste precisa do contexto completo).

## 4. Implementação

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — header toggle,
  composer box, slash palette, Esc-interrupt, `CurrentMode`/`SetModeAsync`/
  `ContextText`.
- `src/Taskboard.Client/wwwroot/css/site.css` — `.ai-chat-modeswitch`,
  `.ai-chat-iconbtn`, `.ai-chat-composer-box`, `.ai-chat-toolbar`,
  `.ai-chat-ctl`, `.ai-chat-chip`, `.ai-chat-sendbtn`, coluna central.
- `src/Taskboard.Integrations/Chat/Tools/WorkspaceTools.cs` — 6 novas tools.
- `src/Taskboard.Application.Contracts/Chat/ChatTodoStore.cs` — store do
  `todo` por `ConversationId`.
- `src/Taskboard.Server/Program.cs` — registro das tools.
- `tests/Taskboard.Tests.Unit/Chat/WorkspaceToolsTests.cs` — 16 testes BDD.
- `Dockerfile`, `docker-compose.yml`, `.dockerignore` — stage/serviços.

## 5. Critérios de Aceite

- [x] `dotnet build` Release — 0 warnings.
- [x] `dotnet test` — suíte verde incluindo 16 testes novos.
- [x] `dotnet format --verify-no-changes` no diff.
- [ ] `docker build --target test .` executa a suíte na imagem.
- [ ] UI: toggle/troca de modo, slash palette, Esc-interrupt e composer
      verificados visualmente.

## 6. OpenCode TUI — ideias avaliadas (sugestões futuras)

Adotadas neste SPEC: slash palette, Esc-interrupt, todo list, git tool.

Sugeridas para follow-up (documentadas no PR):

- **@file mention**: `@path` no composer anexa conteúdo do arquivo ao prompt.
- **Session rename/auto-title por resumo** (já existe `AiChatThreadTitle.Derive`
  — extensão: re-titular pelo 1º turno do agente).
- **Diff view das edições do agente** (OpenCode mostra diff inline; hoje
  `ToolCallChangesCard` já agrupa mudanças — evoluir para unified diff).
- **Model picker rápido no composer** (`Ctrl+M` cicla modelos).
- **Session export/share** (markdown da conversa).
