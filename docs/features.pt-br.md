# Funcionalidades

## Board Principal

- Kanban baseado no GitHub: issues são a fonte de verdade (colunas via labels, prioridades via labels `priority:*`)
- Mutações do board persistem `IssueHistoryEvent` para a timeline unificada
- Comentários da issue no GitHub como canal de handoff entre agentes
- Suporte a Markdown GFM + mermaid (read-only)

## Seletor global de repositório

- Um único combobox de repositório fica na barra lateral acima do item Board — `SelectedRepositoryService` (WASM, scoped) lista os repos via GitHub, mantém o `owner/repo` atual, persiste em `localStorage["harness.selectedRepo"]` e dispara `Changed` para todas as páginas que usam repo; texto livre `owner/repo` continua funcionando quando a listagem falha ou não há token
- Agrupamento do menu: itens que usam repo no topo — Board, Gantt, Workflow, Specs, VS Code, Terminal — depois um divisor `---`, depois AI Chat, CLI Agents, FinOps, Settings, Skills, Prompts e um link externo Issues (abre `github.com/afonsoft/agent-harness/issues` em nova aba)
- Rail de ícones colapsado: o seletor vira um ícone de pasta com o repo atual como tooltip; clicar expande a sidebar
- Consumidores: Board/Gantt/Workflow perdem o combo próprio e recarregam ao mudar; `/specs` lê `~/repos/<name>/.specs` do clone local (empty state quando não clonado); abas novas do terminal abrem em `~/repos/<name>`; `/editor` abre por padrão no workdir do clone, a menos que `?path`/`?repo` seja passado

## Tempo Real

- Stream SSE global: `/api/events`
- SSE por thread de IA: `/api/local/ai/threads/:id/events`
- Polling fallback para modo cloud (`GET /api/meta`)

## Automação

- `/workflow` — monitor read-only de GitHub Actions (workflows, runs, status, duração, links diretos)
- Execução de agentes sobre cards de issues do GitHub

## CLI

`taskctl` — console Spectre.Console.Cli com comandos:
- `context:current`, `ghissue:history`, `ghissue:comments`, `ghissue:comment`, `cloud:login`, `cloud:status`, `cloud:logout`
- Saída JSON via `--json`

## MCP Server

4 tools: `get_issue_history`, `list_github_issue_comments`, `add_github_issue_comment`, `cloud_status`.

## AI Chat

- UI de chat completa em `/ai-chat`: sidebar de threads (criar via CLI de agente elegível + picker de modelo + picker de sandbox, excluir com confirmação), área de mensagens com renderização markdown, composer, indicador "digitando" e auto-scroll
- Toda thread é vinculada a uma CLI de agente elegível — não existe provider de LLM direto no servidor (SPEC-20260921-ai-chat-cli-backend). O catálogo de modelos lista modelos por agente: reportados pela CLI (`opencode models`, `devin models list`…) combinados com a tabela curada e overrides de tier salvos; "CLI default" deixa o agente escolher o modelo
- Threads `assistant` executam one-shot pela CLI vinculada (`IAgentAcpClient`) com o transcript como prompt — o run falha com erro claro se nenhum agente for elegível; threads legadas sem agente fazem bind automático ao primeiro CLI elegível no próximo run; desabilitar um agente interrompe suas threads no run
- Threads `agent` mantêm a sessão ACP interativa (`AgentSessionManager`); o modelo escolhido chega à sessão via flag de modelo da CLI
- UX do modo agent (SPEC-20260921-ai-code-chat-ux): renderers estruturados de tool call (edit/write → diff inline com path e contagens +/-, execute/terminal → output colapsável com badge de exit code, read → preview truncado, desconhecido → card genérico, edições consecutivas → card `changes` agregado); fila FIFO de prompts — `POST .../queue` persiste evento `queued` (cancelável via `DELETE .../queue/{eventId}`) e o backend despacha ao fim de cada turno; medidor de contexto alimentado por `usage_update` do ACP (eventos `metric` — warning ≥80%, danger ≥95%, oculto sem dados); fork em qualquer evento (`POST .../fork` → `<título> (source: fork)` copia eventos até aquele ponto) e retry do último prompt do usuário (`POST .../retry`, cancela o turno ativo antes); quick-switch de modelo/modo via `session/set_config_option`/`session/set_mode` quando o peer os anuncia; payloads acima de 8k chars colapsam atrás de "show more"
- Respostas do assistente em streaming via SSE (deltas `ai_chat.event` + status `ai_chat.run`); snapshot JSON via `Accept: application/json`
- Sub-tarefa **Run agent**: picker (repositório + CLI de agente elegível + tier de modelo) enfileira `POST /api/agents/executions` com o contexto da thread como instruções (últimas 20 mensagens, cap ~8k); fila e status final voltam como eventos da thread
- `MockLLMProvider` é só para dev/teste (`Taskboard:AiChat:MockProvider=true`) — nunca é o default de produção

## Cloud

- Companion loopback local
- Proxy Cloudflare D1/R2
- Basic Auth

## Integrações

- Sincronização Jira
- Kanban GitHub em `/github-board` através de `IGitHubService`
- Labels do board GitHub: `backlog`, `in-progress`, `review`, `done`
- Gantt em `/gantt` alimentado pelas issues do GitHub: barras `createdAt → closedAt` (issues abertas vão até hoje com estilo "em andamento"), losangos de `due_on` do milestone, barras coloridas por coluna e painel de métricas de kanban — lead time (média/mediana), cycle time (primeira saída do backlog → done), WIP, aging mediano das abertas e throughput semanal — calculados no servidor a partir dos eventos `labeled`/`unlabeled` do timeline mesclados com os `IssueHistoryEvent` locais
- Página `/workflow`: monitor read-only de GitHub Actions por repositório — cards de workflow com badge da conclusão do último run (runs vivos pulsam e dirigem o auto-refresh de 60s), expand mostra os 10 últimos runs (status, branch, SHA, actor, duração, link ↗ para o GitHub)
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
- Página `/terminal`: bash PTYs interativos via SignalR (`/terminal-hub`) com xterm.js — **múltiplas sessões em abas** (até 8 por usuário, `Open()` retorna `sessionId` roteado na mesma conexão), timeout de 30 min ocioso por aba, fechar/reabrir por aba, `?cmd=` pré-preenche a primeira aba; abas novas abrem no workdir do repositório selecionado (`~/repos/<name>`, caindo para a raiz do workspace); teclas de terminal nativo: Ctrl+C copia a seleção (ou envia SIGINT), Ctrl+V / Ctrl+Shift+V / Shift+Insert colam, flag `Taskboard:Terminal:Enabled`
- A imagem Docker traz Node.js LTS + os cinco CLIs com `HOME=/data/home` para credenciais persistirem no volume `/data`

## VS Code Web e Workspace

- Página `/editor` (menu lateral "VS Code"): VS Code Web via `code-server` gerenciado — iframe embutido, toolbar de caminho, abrir em nova aba; abre por padrão no workdir do repo selecionado globalmente (`~/repos/<name>`; `?path=`/`?repo=owner/name` sempre vencem); o botão **Restart** mata e recria o code-server (`POST /api/vscode/restart`)
- Instalação gerenciada: `POST /api/vscode/install` executa o instalador standalone allowlisted em background com console de log ao vivo (`GET /api/vscode/install/status`)
- code-server roda como processo filho lazy em `127.0.0.1` com `--auth none --disable-workspace-trust --app-name Taskboard`, acessível apenas pelo proxy YARP autenticado em `/vscode/{**}` (com WebSocket, prefixo removido); `EnsureStartedAsync` aguarda a porta aceitar conexão (probe TCP, timeout 30s) para o primeiro request não cair num 502 de race; `VSCODE_PROXY_URI=/vscode/proxy/{{port}}` mantém os links de portas funcionando sob o subpath
- Workspace root `Taskboard:WorkspaceRoot` (padrão `~/repos`, criado automaticamente): cwd default dos agent runs e clones; workdir do card resolve para `<root>/<repo>` (sanitizado, sem traversal) via `GET /api/vscode/workdir`
- "Open in VS Code" no dialog da issue abre o editor em nova aba do navegador via `GET /api/vscode/open?repo=<fullName>` → 302 para `/vscode/?folder=<workdir do card>` — veja e edite os mesmos arquivos que o agente está alterando

## Settings: Skills e RAG MCP

- Agent Skills: instalação global `npx skills add` + `install.sh --all`, verificação e sync por agente com log de processo ao vivo (`GET /api/skills/log`); o cache do repositório se auto-recupera — um `skills-cache` inacessível (ex.: deixado por outro usuário) é movido para `skills-cache.inaccessible-<timestamp>` e re-clonado automaticamente, reportado como step `cache-prepare`
- RAG / Knowledge MCP: provisiona o servidor configurado em todos os CLIs habilitados — merge JSON/TOML para Devin, Claude, Codex, OpenCode, OpenHands, Kimi, Grok, Qwen, Copilot e Kiro; `agy mcp add/remove` para Antigravity; `cline mcp add/remove` para Cline; arquivo JSON em `~/.continue/mcpServers/` para Continue (Aider não suporta MCP) — com log de processo ao vivo (`GET /api/mcp/log`); chaves de API nunca aparecem em status ou logs. **Sync** provisiona a URL salva (bloqueado enquanto não há URL salva — nunca remove implicitamente); **Remove** é a ação explícita de desprovisionar; badges por agente distinguem `configured`/`updated`/`removed`/`failed`

## ADE Harness (E6–E11)

- **Isolamento de workspace**: cada run de agente ganha um Git worktree dedicado em `{worktreeRoot}/{runId}` — `Taskboard:WorktreeRoot`, default `~/repos` para os worktrees ficarem ao lado dos clones no workspace — o cwd do agente é o worktree, nunca o checkout vivo. Endpoints de ciclo de vida `POST /api/harness/worktrees`, `GET …/{runId}`, `GET …/{runId}/diff` (diff estruturado por arquivo), `DELETE …/{runId}` (teardown + `git worktree prune`).
- **Contexto & memória**: `POST /api/harness/context/compile` mescla arquivos de instrução (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, copilot-instructions — recursivo), bloco `<env>`+git e itens `<project_memory>` escopados pelo remote `origin`; `IContextCompactor` sumariza o miolo a 80% do orçamento de tokens mantendo system prompt + últimas 5 turmas. CRUD de memória em `POST|GET|DELETE /api/harness/memory`.
- **Gateway de segurança**: `POST /api/harness/security/evaluate` classifica comandos pré-dispatch (Safe/WorkspaceWrite/Dangerous, fail-closed para binários desconhecidos/substituições/globs) com path jail + detecção de escape por symlink (escape é deny duro) e scrubbing de segredos por regex (`ghp_`, `sk-`, AWS, Bearer, PEM).
- **Loop de verificação**: `POST /api/harness/verification/run` roda `dotnet format → build → test` com parsing TRX + cobertura e persiste um `VerificationReport`; opt-in por run via `verifySolutionFile`/`verifyMinCoverage`/`verifyMaxAttempts` no `AgentExecutionRequest` — falhas re-invocam o agente com `feedbackPrompt`, retries esgotados marcam `Failed` + `EscalatedToHuman`.
- **Métricas de CLI**: ingestão incremental read-only dos SQLite dos CLIs de agente (camada de extractors do E10, nunca escreve em DBs externos, nunca lê conteúdo de mensagens). `POST /api/local/cli-metrics/sync` (single-flight, 404 quando `Taskboard:CliMetrics:Enabled=false`), `GET sources|summary?period|sessions`; sync periódico a cada `SyncIntervalMinutes` (padrão 15). `/agents` mostra sessões-7d, tokens, última atividade e badge de saúde por fonte; linhas brutas expurgo após `RetentionDays` (padrão 90), agregados diários persistem. `ICliUsageMetricsProvider` expõe agregados de tokens por kind/modelo para o feed FinOps.

- **Cockpit (HITL)**: `/cockpit` lista os runs de pipeline e inicia novos (template, repo — pré-preenchido pelo seletor global — branch, teto de budget). O diálogo New Run funciona para qualquer template: um checkbox **Single agent** vincula um único CLI (Auto ou escolhido dentre os CLIs disponíveis) mais um tier a todos os estágios AgentWork, e uma seção colapsável **Options** expõe seletores de Agent CLI (`Auto` primeiro, depois só CLIs instalados+autenticados+habilitados) e tier (Lite/Normal/Ultra) por estágio; sem CLI disponível o diálogo avisa e desabilita o Start. Cada card de estágio mostra o CLI efetivo e o contador `×N` de tentativas, com a cadeia de fallback (`tried: A → B`) no hover. `/cockpit/runs/{id}` transmite uma timeline estruturada ao vivo (cards de stage / agent_output / verification / approval / steer / tool_call) pelo grupo SignalR `/harness-cockpit-hub` com replay via REST na reconexão, aba **Logs** xterm.js read-only que renderiza o stream `agent_output` do run em tempo real com follow-scroll (pill "↓ novos logs"), filtros de stage/stream, botões copiar/baixar/limpar e badge ao vivo, aba **Arquivos** explorer que navega o worktree do run (`GET …/worktrees/{runId}/files` + `…/files/content` — árvore lazy, read-only, `.git` oculto, confinamento de path, cap de 512 KB), aba **Diff** que lista os arquivos alterados em cards colapsáveis por arquivo (`GET …/worktrees/{runId}/diff`, `+/-` por arquivo via `--numstat`, truncado acima de 2 MB), barra de steer (`POST …/runs/{id}/steer` — enfileirado no prompt do próximo estágio), controles de pause/resume (`POST …/runs/{id}/pause`/`resume` — congela o DAG entre estágios, o estágio em voo completa), gates de aprovação (`POST …/approvals/{requestId}`, convenção `stage:<key>`), botão de stop e `create-pr` em um clique que commita + dá push no worktree, abre o PR no GitHub e move a issue vinculada para `in_review` com o link do PR comentado. O cabeçalho mostra métricas FinOps ao vivo (tokens in/out, custo USD, tempo decorrido).
- **Board → Cockpit (runs unificados)**: mover uma issue para Backlog/Em Progresso e escolher um agente agora inicia uma execução `single-agent` real do pipeline (worktree + eventos + FinOps), não um `AgentRun` legado. Um banner leva direto a `/cockpit/runs/{id}` e a aba Histórico da issue registra uma entrada `pipeline-run`. Registros `AgentRun` antigos permanecem read-only na timeline.

Veja `.specs/SPEC-*.md` para requisitos completos.
