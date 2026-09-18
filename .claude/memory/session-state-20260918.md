# Session State — 2026-09-18

Estado consolidado do trabalho entregue no `taskboard-ai` (main) — referência
para próximas sessões. Deploy host: ver `deployment-vps.md`.

## PRs entregues (todos merged em `main` + deploy no host)

| PR | Commit | Entrega |
|---|---|---|
| #95 | `cba4a4f` | Terminal multi-abas: `TerminalSessionManager` (registry user/session, cap 8, idle 30min), hub com `sessionId` (Open/Input/Resize/Close), tabs Bootstrap na `/terminal`, Ctrl+C/V nativos |
| #96 | `c26682c` | VS Code Web: `WorkspaceService` (`Taskboard:WorkspaceRoot` default `~/repos`), `VscodeInstallService`, `CodeServerProcessManager` (spawn lazy 127.0.0.1:8377), proxy YARP `/vscode/{**}` autenticado, página `/editor`, "Open in VS Code" no card |
| #97 | `82e6378` | Fix loop de redirect `/vscode` ↔ `/vscode/` — middleware com path exato + `UseForwardedHeaders` + nginx `X-Forwarded-Proto` (`$tb_xfp`) |
| #98 | `8ed0654` | Terminal UX: full-height (flex até o fim da página), scrollbar xterm estilizada, tabs `nav-tabs` Bootstrap legíveis com `btn-close` + foco na ativa |
| #99 | `36557be` | code-server flags: `VSCODE_PROXY_URI=/vscode/proxy/{{port}}`, `--disable-workspace-trust`, `--app-name Taskboard` |
| #100 | `2940a45` | Default agent prompt em en-US (clone em `~/repos`, skills, MCP knowledge, manage-taskboard); override pt-BR removido do SQLite (`ConfigurationOverrides`) |
| #101 | `a81a2ab` | Skill detail: markdown renderizado (MarkdownRenderer/Markdig+sanitize), toggle Rendered/Source, botão Copy, modal `modal-70` |
| #102 | `1ca5423` | Histórico da issue: entidade `IssueHistoryEvent` + tabela EF (migration `AddIssueHistoryEvents`), eventos column-moved/edited/closed best-effort, `GET /api/github/issues/{issueId}/history` (merge eventos + AgentRun), aba **Histórico** no dialog, `GET /api/vscode/open?repo=` → 302 direto ao editor em nova aba |
| #103 | `bbd7683` | Fix race 502 code-server: `EnsureStartedAsync` aguarda a porta aceitar TCP (probe 250ms, timeout 30s); `Running` = vivo **e** com bind |

## Fatos arquiteturais importantes

- **Histórico**: `IssueHistoryEvent` (Domain, `Entity<Guid>`) em `IssueHistoryEvents`; kinds `ColumnMoved|Edited|Closed`; keyed por `issue.Id` GitHub (= chave dos `AgentRun`). Gravação best-effort (nunca falha a mutação). Eventos pré-deploy não existem retroativamente.
- **VS Code**: code-server standalone em `~/.local/bin/code-server` (v4.137.0 instalado no host); spawn lazy; `Running` = processo vivo + porta com bind; `ResolveCardWorkdir` → `~/repos/<repo>` sanitizado.
- **Prompt default**: `AgentPromptTemplate.Builtin` (en-US) + override persistido via `Taskboard:Agents:DefaultPrompt` na tabela `ConfigurationOverrides` (SQLite `~/.taskboard/data`). Mudar builtin NÃO muda override persistido — remover a linha faz o builtin valer.
- **Testes**: 395 unit + ~124 integration verdes; factory limpa env vars do deploy (`TASKBOARD_*`, `Taskboard__*`, `GITHUB_TOKEN`) — hermeticidade.
- **Sanitização de logs**: redact `Bearer` ANTES do padrão `key:value` (bug corrigido — "Authorization: Bearer tok" vazava).

## Pendências / contexto para próximos SPECs (em escrita)

- Seleção de modelo LLM por CLI (tiers Lite/Normal/Ultra) + badge no card.
- Rename do projeto taskboard → harness (branding/docs).
- Prompt default usando skill `orchestrator`.
- Comentários GitHub nos cards + inclusão no prompt do agente + histórico no MCP/taskctl + skill manage-taskboard atualizada.
