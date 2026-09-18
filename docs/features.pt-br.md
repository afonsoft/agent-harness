# Funcionalidades

## Board Principal

- Projetos e tarefas com status, prioridades, labels
- Board Kanban ordenado por `sort_order`
- Comentários, anexos, relações e atividades de tarefas
- Suporte a Markdown GFM + mermaid (read-only)
- Concorrência otimista com coluna `version`

## Tempo Real

- Stream SSE global: `/api/events`
- SSE por thread de IA: `/api/local/ai/threads/:id/events`
- Polling fallback para modo cloud (`GET /api/meta`)

## Automação

- Workspaces de workflow (config JSON do board)
- Engine de control-flow
- Auto-claim de `todo` → `in_progress` para agentes Codex

## CLI

`taskctl` — console System.CommandLine com subcomandos:
- `project`, `issue`, `comment`, `attachment`, `label`, `ai`, `context`, `search`
- Saída JSON via `--json`

## MCP Server

13 tools expondo operações de projeto/issue/comentário/anexo/label/busca.

## AI Chat

- Threads, runs, events
- Catalog de modelos
- Composer candidates / rebind
- Abstração de provider (OpenAI, Claude, Azure OpenAI)

## Cloud

- Companion loopback local
- Proxy Cloudflare D1/R2
- Basic Auth

## Integrações

- Sincronização Jira
- Kanban GitHub em `/github-board` através de `IGitHubService`
- Labels do board GitHub: `backlog`, `in-progress`, `review`, `done`
- Autenticação do GitHub através de `GITHUB_TOKEN`
- Harness DeepSeek
- Helpers de execução (`CodexExecutableResolver`, `ProcessTreeSignaler`, `ExecutableCommand`)

## Orquestração de Agentes

- Detecta as CLIs Devin, Claude, Codex, OpenCode, OpenHands, Antigravity, Kimi, Grok, Aider, Cline, Continue, Copilot, Qwen e Kiro no `PATH` do servidor
- Seleciona um agente quando uma issue é movida para `In Progress` ou `Backlog`
- Enfileira a execução em background e transmite logs de stdout/stderr/system
- Hub SignalR: `/agent-log-hub`
- Uma execução bem-sucedida move a issue de `In Progress` para `Review`
- Modal de issue (~80vw): aba `Agent Config` (link do repositório, CLI do agente + combo de **tier de modelo** Lite/Normal/Ultra mapeado para modelos reais por CLI — desabilitado "gerenciado pela CLI" quando o CLI não tem flag — preview do argv refletindo o tier, editor de prompt), aba `Logs do Agente` com Limpar persistente (`DELETE /api/agents/logs/{issueId}`), aba `Histórico` — timeline unificada das mutações do board (movimentações de coluna, edições, fechamentos persistidos como `IssueHistoryEvent`) combinada com as execuções de agente (`GET /api/github/issues/{issueId}/history`), e aba `Comentários` — lista/posta comentários da issue no GitHub; o prompt do agente renderizado anexa os comentários automaticamente numa seção `Comments:` limitada (canal de handoff entre agentes)
- Prompt padrão global na página `/agents` (`GET`/`PUT /api/agents/prompt-template`) com placeholders `{repoUrl}`/`{issueTitle}`/`{issueBody}`/`{issueComments}`

## CLIs de Agentes e Terminal

- Página `/agents`: painel de status de instalação/autenticação de 13 CLIs — Claude Code, Codex, OpenCode, Devin CLI, Antigravity `agy`, Kimi Code, Grok, Aider, Cline, Continue, GitHub Copilot CLI, Qwen Code e Kiro CLI (`GET /api/agent-clis`); o botão **Models** em CLIs instaladas abre um dialog para redefinir o modelo de cada tier Lite/Normal/Ultra com dropdown editável e busca, alimentado pelos modelos que a própria CLI reporta (`GET /api/agents/{type}/models/available` — `opencode models`, `devin models list`, `agy models`) mesclados ao catálogo curado (`GET/PUT/DELETE /api/agents/{type}/models` — overrides em JSON no `ConfigurationOverrides`, vencendo a tabela curada `AgentCliModels` na execução)
- Instalação gerenciada pela UI: `POST /api/agent-clis/{kind}/install` executa o comando allowlisted (npm/pipx/curl) em background com popup de console de logs ao vivo (`GET /api/agent-clis/{kind}/install/status`) — fecha sozinho em sucesso; ação Login aparece após instalar
- Página `/terminal`: bash PTYs interativos via SignalR (`/terminal-hub`) com xterm.js — **múltiplas sessões em abas** (até 8 por usuário, `Open()` retorna `sessionId` roteado na mesma conexão), timeout de 30 min ocioso por aba, fechar/reabrir por aba, `?cmd=` pré-preenche a primeira aba; teclas de terminal nativo: Ctrl+C copia a seleção (ou envia SIGINT), Ctrl+V / Ctrl+Shift+V / Shift+Insert colam, flag `Taskboard:Terminal:Enabled`
- A imagem Docker traz Node.js LTS + os cinco CLIs com `HOME=/data/home` para credenciais persistirem no volume `/data`

## VS Code Web e Workspace

- Página `/editor` (menu lateral "VS Code"): VS Code Web via `code-server` gerenciado — iframe embutido, toolbar de caminho, abrir em nova aba; abre em `$HOME` por padrão, `?repo=owner/name` abre o workdir do repo do card
- Instalação gerenciada: `POST /api/vscode/install` executa o instalador standalone allowlisted em background com console de log ao vivo (`GET /api/vscode/install/status`)
- code-server roda como processo filho lazy em `127.0.0.1` com `--auth none --disable-workspace-trust --app-name Taskboard`, acessível apenas pelo proxy YARP autenticado em `/vscode/{**}` (com WebSocket, prefixo removido); `EnsureStartedAsync` aguarda a porta aceitar conexão (probe TCP, timeout 30s) para o primeiro request não cair num 502 de race; `VSCODE_PROXY_URI=/vscode/proxy/{{port}}` mantém os links de portas funcionando sob o subpath
- Workspace root `Taskboard:WorkspaceRoot` (padrão `~/repos`, criado automaticamente): cwd default dos agent runs e clones; workdir do card resolve para `<root>/<repo>` (sanitizado, sem traversal) via `GET /api/vscode/workdir`
- "Open in VS Code" no dialog da issue abre o editor em nova aba do navegador via `GET /api/vscode/open?repo=<fullName>` → 302 para `/vscode/?folder=<workdir do card>` — veja e edite os mesmos arquivos que o agente está alterando

## Settings: Skills e RAG MCP

- Agent Skills: instalação global `npx skills add` + `install.sh --all`, verificação e sync por agente com log de processo ao vivo (`GET /api/skills/log`)
- RAG / Knowledge MCP: provisiona o servidor configurado em todos os CLIs habilitados — merge JSON/TOML para Devin, Claude, Codex, OpenCode, OpenHands, Kimi, Grok, Qwen, Copilot e Kiro; `agy mcp add/remove` para Antigravity; `cline mcp add/remove` para Cline; arquivo JSON em `~/.continue/mcpServers/` para Continue (Aider não suporta MCP) — com log de processo ao vivo (`GET /api/mcp/log`); chaves de API nunca aparecem em status ou logs. **Sync** provisiona a URL salva (bloqueado enquanto não há URL salva — nunca remove implicitamente); **Remove** é a ação explícita de desprovisionar; badges por agente distinguem `configured`/`updated`/`removed`/`failed`

Veja `.specs/SPEC-*.md` para requisitos completos.
