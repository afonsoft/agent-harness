# SPEC-20260918-github-comments-history

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `github-comments-history` |
| Type | `Feature` |
| Stack | `.NET 10 / Blazor / MCP / CLI` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260918-github-comments-history` |
| Ticket | — |
| Status | `Done` |

## 1. User Story

**As a** administrador e os agentes CLI executando issues do board GitHub
**I want** que comentários da issue do GitHub apareçam nos cards, possam ser criados pelo card (salvos no GitHub), entrem no prompt do agente, e que histórico + comentários sejam acessíveis via MCP/taskctl
**So that** cada agente receba o contexto acumulado (comentários deixados por execuções/pessoas anteriores) e possa deixar rastro legível para o próximo agente ou etapa.

**Problem context:**
Agentes rodam "cegos" ao histórico da issue: o prompt carrega só título+corpo. O histórico (SPEC-20260918-issue-history) existe só na UI. Comentários do GitHub — o canal natural de handoff entre execuções — não são lidos nem escritos. Aprovado: expor histórico+comentários em **MCP e taskctl**; comentários entram no prompt como **seção automática** (sem placeholder).

## 2. Scope

**In scope:**
- `IGitHubService` + endpoints: `GET /api/github/repos/{o}/{r}/issues/{n}/comments` e `POST .../comments` (Octokit `Issue.Comment`).
- `GET /api/github/issues/{issueId}/comments` (lookup por issueId, como o history endpoint) — decidir na implementação o caminho canônico (recomendado: por `owner/repo/number`, coerente com os demais; history fica por issueId pois `AgentRun` usa essa chave).
- `TaskboardClient`/`HttpGitHubService` (Blazor): listar/criar comentários.
- `TaskDetailDialog`/`KanbanBoard` card: seção/comentários da issue (lista + textarea + Salvar → POST no GitHub); indicador de contagem no card se viável sem N+1.
- `manage-taskboard` skill: documentar histórico e comentários (endpoints novos) no SKILL.md/references.
- MCP `TaskboardTools`: novas tools `get_issue_history`, `list_github_issue_comments`, `add_github_issue_comment`.
- `taskctl`: comandos `issue history` e `issue comment` (list/create) para issues do board GitHub.
- `AgentConfigTab`: ao renderizar o prompt, substitui o placeholder `{issueComments}` pela seção `Comments:` com os comentários da issue (quando existirem). Templates/Overrides antigos sem o placeholder recebem a seção anexada ao final (fallback).
- Testes unit + integration; docs bilíngues.

**Out of scope:**
- Editar/deletar comentários do GitHub (só list+create).
- Comentários no board local (já existem — `/api/tasks/{id}/comments`).
- Paginação avançada (take fixo ~50, como history).
- Reação/thread de comentários, markdown preview no card (comentário é texto; render usa o pipeline existente se aplicável).
- Persistir comentários no SQLite (fonte é o GitHub — sempre fetch live).

## 3. Technical Context

**Where the change happens:**
`IGitHubService`/`GitHubService` (Octokit — `Issue.Comment.GetAllForIssue`/`Create`) + endpoints no `Program.cs` (`github` group). UI: `TaskDetailDialog` (nova seção/aba de comentários) + `AgentConfigTab` (fetch + append no prompt). Agente-surface: `src/Taskboard.Mcp/Tools/TaskboardTools.cs` (novas tools) + `src/Taskboard.Cli` (novos comandos) + `.claude/skills/manage-taskboard/` (docs).

**Files to read before implementing:**
- `src/Taskboard.Application.Contracts/GitHub/IGitHubService.cs` + implementação (`src/Taskboard.Integrations/GitHub/`)
- `src/Taskboard.Server/Program.cs` — grupo `github` (history endpoint ~linha 1465 como padrão)
- `src/Taskboard.Mcp/Tools/TaskboardTools.cs` + `src/Taskboard.Mcp/Services/` (client HTTP do MCP)
- `src/Taskboard.Cli/Program.cs` + `src/Taskboard.Cli/Services/TaskboardApiClient.cs`
- `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor`, `AgentConfigTab.razor`, `KanbanBoard.razor`
- `.claude/skills/manage-taskboard/SKILL.md` + `references/cli.md`
- `tests/.../GitHubIssueMutationEndpointsTests.cs`, `IssueHistoryEndpointsTests.cs` (padrão fake `IGitHubService`)

**Files to create or modify:**
```text
src/Taskboard.Application.Contracts/GitHub/IssueCommentDto.cs      (novo)
src/Taskboard.Application.Contracts/GitHub/IGitHubService.cs       (+comments)
src/Taskboard.Integrations/GitHub/GitHubService.cs                 (Octokit impl)
src/Taskboard.Server/Program.cs                                    (endpoints)
src/Taskboard.Blazor/Services/HttpGitHubService.cs                 (+comments)
src/Taskboard.Blazor/Components/GitHub/IssueCommentsTab.razor      (novo ou seção)
src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor      (aba/seção)
src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor        (append Comments:)
src/Taskboard.Mcp/Tools/TaskboardTools.cs                          (3 tools)
src/Taskboard.Mcp/Services/                                        (client calls)
src/Taskboard.Cli/Program.cs                                       (comandos)
src/Taskboard.Cli/Services/TaskboardApiClient.cs                   (calls)
.claude/skills/manage-taskboard/SKILL.md + references/cli.md       (docs)
tests/Taskboard.Tests.Integration/GitHubCommentsEndpointsTests.cs  (novo)
tests/Taskboard.Tests.Unit/... (DTO/mapping se houver)
docs/api.md, docs/api.pt-br.md, docs/features.md, docs/features.pt-br.md
```

## 4. Requirements

### RF-001: Comentários GitHub via `IGitHubService` + endpoints
- **Description:** `IssueCommentDto { id, authorLogin, body, createdAt, updatedAt?, htmlUrl }`; `IGitHubService.GetIssueCommentsAsync(fullName, number, take)` e `AddIssueCommentAsync(fullName, number, body)` via Octokit. Endpoints `GET`/`POST /api/github/repos/{o}/{r}/issues/{n}/comments` (auth, admin).
- **Rules:** take default 50 newest-last (ordem cronológica do GitHub); `POST` com body vazio → 400; token ausente → erro coerente com os demais endpoints; nunca logar token.
- **Input → Output:** `GET` → `200 { comments: [...] }`; `POST { body }` → `200 { comment }`.

### RF-002: Histórico + comentários nas tools MCP
- **Description:** `TaskboardTools` ganha `get_issue_history` (wrap do `GET .../issues/{issueId}/history`), `list_github_issue_comments` e `add_github_issue_comment` (por `owner/repo/number`).
- **Rules:** mesmas convenções das 13 tools atuais (client HTTP interno, erros amigáveis, `--json`-like output estruturado); descrições pt-BR como as demais.
- **Input → Output:** tool `add_github_issue_comment(owner, repo, number, body)` → comentário criado no GitHub.

### RF-003: taskctl — history e comments do board GitHub
- **Description:** Novos comandos no `taskctl`: `issue history --repo owner/name --issue-id <id>` e `issue comment list|create --repo owner/name --number <n> [--body]`.
- **Rules:** `--json` como os demais; identificador GitHub = `owner/name` + número (board local continua `TASK-<projeto>-<n>` — sem confusão de namespaces).
- **Input → Output:** `taskctl issue comment create --repo a/b --number 42 --body "..."` → comentário no GitHub.

### RF-004: Comentários no card/dialog
- **Description:** Dialog da issue exibe os comentários do GitHub (lista com autor+data+body) e caixa de texto + botão **Comentar** que posta no GitHub. Pode ser nova aba **Comentários** no `TaskDetailDialog` (recomendado — dialog já tem 4 abas) ou seção na aba existente.
- **Rules:** refresh após postar; estados loading/empty/erro; `body` plain-text (GitHub renderiza markdown server-side); contador de comentários no card apenas se vier "de graça" no `IssueDto` — senão fora de escopo (evitar N+1 por card).

### RF-005: Comentários no prompt do agente (seção automática)
- **Description:** `AgentConfigTab` busca os comentários da issue ao montar o prompt e injeta uma seção `Comments:` (cronológica, `autor — data: body` resumido) via placeholder `{issueComments}` **após** `{issueBody}`, somente quando houver comentários. O builtin termina com `{issueComments}`; overrides antigos sem o placeholder recebem append ao final. Cap de tamanho: truncar para caber no prompt (ex.: últimos N comentários, ~2-3k chars).
- **Rules:** seção automática (aprovado — sem placeholder novo); ausência de comentários → sem seção; falha no fetch de comentários não bloqueia a execução (prompt sem a seção + aviso no console/toast informativo, não erro fatal).

### RF-006: Skill `manage-taskboard` atualizada
- **Description:** SKILL.md + `references/cli.md` documentam: como o agente lê o histórico (endpoint/MCP/taskctl), como lista/adiciona comentários na issue do GitHub, e a convenção de deixar um comentário-resumo ao fim de cada etapa (handoff para o próximo agente).
- **Rules:** exemplos com `--json`; reforço de nunca expor tokens; fluxo recomendado "ler histórico + comentários antes de agir; comentar resultado ao terminar".

**Business rules / invariants:**
- Comentários são a fonte de handoff: todo agente que conclui etapa deve poder escrevê-los via API.
- Nada é duplicado em SQLite — GitHub é a fonte da verdade dos comentários; `IssueHistoryEvents` segue sendo a fonte do histórico board-side.
- Endpoints admin-only como o restante do grupo `github`.

## 5. API Contract

**Endpoint:** `GET /api/github/repos/{owner}/{repo}/issues/{number}/comments?take=50`
**Auth:** cookie admin / `X-Api-Key` (mesmo esquema do grupo `github`)
**Response `200`:**
```json
{ "comments": [{ "id": 1, "authorLogin": "octocat", "body": "...", "createdAt": "...", "updatedAt": null, "htmlUrl": "..." }] }
```

**Endpoint:** `POST /api/github/repos/{owner}/{repo}/issues/{number}/comments`
```json
{ "body": "texto" }  →  200 { "comment": {...} } | 400 body vazio | 404 issue
```

**MCP tools (novas):** `get_issue_history(issueId)`, `list_github_issue_comments(owner, repo, number)`, `add_github_issue_comment(owner, repo, number, body)`.

**taskctl (novos):** `issue history`, `issue comment list`, `issue comment create` (flags `--repo`, `--number`/`--issue-id`, `--body`, `--json`).

## 6. Acceptance Criteria

- **Dado** uma issue com comentários no GitHub, **quando** abro o dialog, **então** a aba Comentários lista autor/data/body.
- **Dado** texto na caixa de comentário, **quando** clico Comentar, **então** o comentário aparece no GitHub (e na lista após refresh).
- **Dado** comentários existentes, **quando** abro o Agent Config, **então** o prompt renderizado termina com a seção `Comments:` contendo-os (truncada se longa).
- **Dado** sem comentários, **quando** renderizo o prompt, **então** não há seção Comments.
- **Dado** MCP `add_github_issue_comment`, **quando** um agente a invoca, **então** o comentário é criado na issue do GitHub.
- **Dado** `taskctl issue history --repo a/b --issue-id 1`, **quando** executo, **então** vejo a timeline (mesmos dados da aba Histórico).
- **Edge:** fetch de comentários falhando → execução segue sem a seção (não bloqueia).

## 7. Task Plan

1. **T1** — `IssueCommentDto` + `IGitHubService` comments + endpoints + fake + testes integration.
2. **T2** — UI: aba Comentários no dialog (lista + postar) + `HttpGitHubService`/`TaskboardClient`.
3. **T3** — `AgentConfigTab`: fetch + seção `Comments:` via placeholder `{issueComments}` (fallback append) + testes do placeholder.
4. **T4** — MCP: 3 tools novas + client calls + smoke local.
5. **T5** — taskctl: comandos history/comment + api client + smoke.
6. **T6** — Skill manage-taskboard atualizada (SKILL.md + references/cli.md).
7. **T7** — Build + suites + docs bilíngues + PR + merge + deploy.

## 8. Organization Guardrails

- Branch `feature/devin-20260918-github-comments-history`; nada em `main`.
- `GITHUB_TOKEN` nunca em logs/responses; comentários passam pelo Octokit com o token do servidor (não do cliente).
- Novos endpoints admin-only; sem novos pacotes (Octokit já cobre comments).
- Não quebrar contratos existentes (history endpoint, prompt render, MCP tools atuais).

## 9. Definition of Done

- [x] `GET`/`POST` comments funcionais com auth; 400/404 corretos.
- [x] Aba Comentários no dialog com postar→GitHub.
- [x] Prompt do agente inclui seção `Comments:` automática (cap de tamanho, ausente quando vazio).
- [x] MCP: `get_issue_history`, `list_github_issue_comments`, `add_github_issue_comment`.
- [x] taskctl: `issue history`, `issue comment list/create` com `--json`.
- [x] SKILL.md/references do manage-taskboard documentam histórico + comentários + convenção de handoff.
- [x] Testes verdes; docs en/pt-br; PR merged + deploy.
