# Harness

[![.NET Build and Test](https://github.com/afonsoft/agent-harness/actions/workflows/dotnet.yml/badge.svg)](https://github.com/afonsoft/agent-harness/actions/workflows/dotnet.yml)
[![Code Quality](https://github.com/afonsoft/agent-harness/actions/workflows/code-quality.yml/badge.svg)](https://github.com/afonsoft/agent-harness/actions/workflows/code-quality.yml)
[![CodeQL](https://github.com/afonsoft/agent-harness/actions/workflows/codeql.yml/badge.svg)](https://github.com/afonsoft/agent-harness/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **Idioma padrão:** Inglês (en-us). Veja a [README.md](README.md) para a versão em inglês.

**Harness** é uma bancada local-first e AI-native para orquestrar agentes de código de IA — construído em **C# 14 / .NET 10**. O repositório é `agent-harness` (anteriormente `taskboard-ai`); os identificadores técnicos internos mantêm o nome `taskboard` (namespaces, projetos, CLI `taskctl`, env vars `TASKBOARD_*`).

## Visão Geral

O Harness transforma issues do GitHub em um quadro Kanban dirigido por CLIs de agentes de IA. Oferece sistema de tarefas com SQLite, API REST, Server-Sent Events (SSE), CLI `taskctl`, servidor MCP, integração de chat com IA, terminal multi-abas, editor VS Code web embutido (code-server) e UI Blazor WebAssembly — tudo implementado em .NET 10 com ABP N-Layer / DDD.

Capacidades principais:

- **Quadro Kanban GitHub** — colunas baseadas em labels, drag & drop, prioridades, corpos em markdown, comentários de issue (postar pela UI direto no GitHub) e timeline unificada por issue (mutações do board + execuções de agente).
- **Orquestração de agentes** — execute qualquer um dos 13 CLIs de agente (Devin, Claude Code, Codex, OpenCode, Antigravity, Kimi, Grok, Aider, Cline, Continue, Copilot, Qwen, Kiro) numa issue, com prompts por issue, seção `Comments:` de handoff anexada automaticamente ao prompt, streaming de logs via SignalR e histórico de execuções persistido.
- **Isolamento de workspace** — cada execução de agente roda num Git worktree dedicado em `~/.taskboard/worktrees/{runId}` (criar/diff/commit/teardown via `POST|GET|DELETE /api/harness/worktrees`), nunca direto no seu checkout.
- **Loop de verificação** — gate determinístico opt-in de build/test/coverage após cada run (`dotnet format → build → test` com parsing TRX + cobertura); falhas alimentam um loop de retry com `feedbackPrompt` estruturado e escalam para humano após o máximo de tentativas.
- **Gateway de segurança** — classificação de comandos pré-dispatch (Safe/WorkspaceWrite/Dangerous, fail-closed), path jail + detecção de escape por symlink e scrubbing de segredos na saída logada.
- **Contexto & memória** — compilação hierárquica de contexto (`AGENTS.md`/`CLAUDE.md`/`.cursorrules`, bloco env + git, compactação por orçamento de tokens) e itens de memória de projeto escopados pelo remote origin.
- **Métricas de CLI** — ingestão incremental read-only dos SQLite dos CLIs de agente (contagem de sessões, tokens, última atividade por CLI em `/agents`, cursors watermark, detecção de drift, retenção de 90 dias para dados brutos com agregados diários permanentes), mais feed de uso pronto para FinOps.
- **Tiers de modelo** — seletor Lite/Normal/Ultra por CLI mapeado para modelos reais (ex.: Claude `haiku`/`sonnet`/`opus`, Codex `gpt-5.6-luna`, Devin `haiku`/`swe`/`opus`); CLIs sem flag de modelo ficam gerenciados pela própria CLI.
- **Admin de CLIs de agentes** — instale/autentique CLIs pela UI com logs de instalação estilo terminal; ative/desative por agente.
- **Settings de Skills & MCP/RAG** — instale o catálogo `afonsoft/skills` pela UI e provisione um servidor MCP de RAG (URL + key) em todos os configs de agentes suportados.
- **VS Code Web** — code-server gerenciado em `/vscode/` (proxy com espera de readiness, encaminhamento de portas via `VSCODE_PROXY_URI`), mais link "Open in VS Code" por issue.
- **Terminal** — múltiplas abas bash PTY interativas via SignalR.

## Stack Tecnológico

| Camada | Tecnologia | Versão |
|---|---|---|
| Linguagem | C# | 14 |
| Runtime | .NET | 10.0 |
| Web Framework | ASP.NET Core | 10.0 |
| DDD Framework | ABP N-Layer | 9.x |
| ORM | Entity Framework Core | 10.0.12 |
| Banco de dados | SQLite | bundled |
| CLI Parser | System.CommandLine | latest stable |
| MCP SDK | ModelContextProtocol | 2.2.0 |
| Testes | xUnit + Shouldly + NSubstitute | latest stable |
| Frontend | Blazor WebAssembly | .NET 10 |
| Componentes de UI | Blazor.Bootstrap | 4.0.0 |
| Tempo real | ASP.NET Core SignalR | 10.0 |
| Cliente GitHub API | Octokit | 14.0.0 |
| Mediator | MediatR | 12.4.1 |

## Arquitetura

```text
src/
  Taskboard.Domain/                 # Agregados, entidades, value objects, domain events
  Taskboard.Domain.Shared/          # Primitivas compartilhadas do domínio
  Taskboard.Application.Contracts/  # DTOs, interfaces
  Taskboard.Application/            # Commands, queries, handlers (MediatR)
  Taskboard.EntityFrameworkCore/    # EF Core + SQLite + repositórios
  Taskboard.Server/                 # ASP.NET Core Minimal APIs + SSE
  Taskboard.Cli/                    # CLI taskctl (System.CommandLine)
  Taskboard.Mcp/                    # Servidor MCP (ModelContextProtocol SDK)
  Taskboard.AiChat/                 # Threads/runs/events de IA
  Taskboard.Workflow/               # Workspaces e automação de workflow
  Taskboard.Cloud/                  # Companion cloud e sync
  Taskboard.Integrations/           # Jira, GitHub, orquestração de agentes, helpers de execução
  Taskboard.Maui/                   # Desktop Blazor Hybrid (opcional)
  Taskboard.Blazor/                 # UI web Blazor WebAssembly
tests/
  Taskboard.Tests.Unit/             # 686 testes unitários
  Taskboard.Tests.Integration/      # 174 testes de integração
```

## Início Rápido

```bash
git clone https://github.com/afonsoft/agent-harness.git
cd agent-harness
dotnet restore Taskboard.sln
dotnet build Taskboard.sln
dotnet test Taskboard.sln
dotnet run --project src/Taskboard.Server
```

Veja [`docs/installation.pt-br.md`](docs/installation.pt-br.md) para setup detalhado, variáveis de ambiente e resolução de problemas.

## Instalador do CLI

Instale o `taskctl` em `/usr/local/bin`:

```bash
./install-cli.sh
```

Veja [`install-cli.sh`](install-cli.sh) e [`docs/installation.pt-br.md`](docs/installation.pt-br.md) para detalhes.

## Integração Contínua

O GitHub Actions fornece:

- Build e testes em Release, verificação de formatação, gate de cobertura de linhas (atualmente 65%, subindo gradualmente até a meta de 80%) e verificação de pacotes vulneráveis.
- Análise SonarCloud quando o secret `SONAR_TOKEN` está configurado.
- Análise CodeQL para C# e GitHub Actions.
- Atualizações semanais de pacotes NuGet e GitHub Actions através do Dependabot.

## GitHub Kanban e Agentes de IA

Defina `GITHUB_TOKEN` antes de iniciar o servidor:

```bash
export GITHUB_TOKEN=seu-token-do-github
```

Abra `/github-board` para visualizar as issues do GitHub como um board Kanban. Arraste uma issue para **In Progress**, escolha o CLI do agente e o tier de modelo, e acompanhe a execução na aba **Logs**. Os logs dos agentes são transmitidos em tempo real pelo hub SignalR em `/agent-log-hub`.

## Destaques Recentes

- Épicos do harness ADE E6–E11 entregues: worktrees Git isolados por run, compilação de contexto + memória de projeto, gateway de segurança para gating de comandos pré-dispatch, loop de verificação determinístico, leitor read-only dos SQLite dos CLIs e métricas persistentes de uso por CLI com linha de dashboard em `/agents`.
- Tiers de modelo Lite/Normal/Ultra mapeados para modelos reais por CLI.
- Comentários de issue do GitHub como canal de handoff entre agentes (aba na UI + seção `Comments:` automática no prompt + MCP/taskctl).
- Histórico unificado da issue: mutações do board + execuções de agente numa única timeline.
- VS Code Web (code-server gerenciado) com deep links por issue e encaminhamento de portas.
- Terminal PTY multi-abas e página de admin de CLIs com logs de instalação.

## Ordem de Build

Veja [`.specs/CAPABILITY-MAP.md`](.specs/CAPABILITY-MAP.md).

1. `domain-model`
2. `persistence`
3. `rest-api`
4. `cli`
5. `mcp`, `ai-chat`, `cloud`, `workflow-automation`
6. `skill`, `frontend`, `integrations`

## Documentação

- [`docs/README.md`](docs/README.md) — Documentação do sistema
- [`docs/technologies.md`](docs/technologies.md) — Tecnologias e versões
- [`docs/packages.md`](docs/packages.md) — Pacotes NuGet e NPM
- [`docs/plugins.md`](docs/plugins.md) — Plugins e integrações
- [`docs/features.md`](docs/features.md) — Funcionalidades
- [`docs/api.md`](docs/api.md) — API REST e SSE
- [`docs/architecture/`](docs/architecture/) — Diagramas de arquitetura
- [`.specs/`](.specs/) — Especificações SDD

## Agent Harness

- [`CLAUDE.md`](CLAUDE.md) — Fonte única de verdade para agentes
- [`.claude/`](.claude/) — Harness para Claude Code / Devin CLI
- [`.devin/config.json`](.devin/config.json) — Configuração do Devin CLI
- `.claude/skills/` — Catálogo de skills (afonsoft/skills + manage-taskboard); Google Antigravity usa `~/.gemini/skills/` (global)
- [`.claude/memory/orchestrator_stats.md`](.claude/memory/orchestrator_stats.md) — Estado da sessão do orquestrador

O harness de agentes usa skills do [`afonsoft/skills`](https://github.com/afonsoft/skills):

```bash
npx skills add afonsoft/skills
```

O comando instala skills em `.claude/skills/`; o arquivo `skills-lock.json` registra as fontes fixadas.

## Contribuição

- Crie uma branch a partir de `main` ou `develop`.
- Siga as `.specs/` e as regras globais em `.claude/rules/global-rules.md`.
- Garanta que `dotnet build` e `dotnet test` passem.
- Abra um Pull Request.

## Licença

MIT — veja [`LICENSE`](LICENSE).
