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

- Detecta as CLIs Devin, Claude, Codex, OpenCode e OpenHands no `PATH` do servidor
- Seleciona um agente quando uma issue é movida para `In Progress` ou `Backlog`
- Enfileira a execução em background e transmite logs de stdout/stderr/system
- Hub SignalR: `/agent-log-hub`
- Uma execução bem-sucedida move a issue de `In Progress` para `Review`
- Modal de issue: aba `Agent Config` (link do repositório, preview do argv por CLI, editor de prompt) e aba `Logs do Agente` com Limpar persistente (`DELETE /api/agents/logs/{issueId}`)
- Prompt padrão global na página `/agents` (`GET`/`PUT /api/agents/prompt-template`) com placeholders `{repoUrl}`/`{issueTitle}`/`{issueBody}`

## CLIs de Agentes e Terminal

- Página `/agents`: painel de status de instalação/autenticação de Claude Code, Codex, OpenCode, Devin CLI e Antigravity `agy` (`GET /api/agent-clis`)
- Página `/terminal`: bash PTY interativo via SignalR (`/terminal-hub`) com xterm.js — uma sessão por usuário, timeout de 30 min ocioso, flag `Taskboard:Terminal:Enabled`
- A imagem Docker traz Node.js LTS + os cinco CLIs com `HOME=/data/home` para credenciais persistirem no volume `/data`

## Settings: Skills e RAG MCP

- Agent Skills: instalação global `npx skills add` + `install.sh --all`, verificação e sync por agente com log de processo ao vivo (`GET /api/skills/log`)
- RAG / Knowledge MCP: provisiona o servidor configurado em todos os CLIs habilitados — merge JSON/TOML para Devin, Claude, Codex, OpenCode e OpenHands, e `agy mcp add/remove` para Antigravity — com log de processo ao vivo (`GET /api/mcp/log`); chaves de API nunca aparecem em status ou logs

Veja `.specs/SPEC-*.md` para requisitos completos.
