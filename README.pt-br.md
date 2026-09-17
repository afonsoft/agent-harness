# taskboard-ai

[![.NET Build and Test](https://github.com/afonsoft/taskboard-ai/actions/workflows/dotnet.yml/badge.svg)](https://github.com/afonsoft/taskboard-ai/actions/workflows/dotnet.yml)
[![Code Quality](https://github.com/afonsoft/taskboard-ai/actions/workflows/code-quality.yml/badge.svg)](https://github.com/afonsoft/taskboard-ai/actions/workflows/code-quality.yml)
[![CodeQL](https://github.com/afonsoft/taskboard-ai/actions/workflows/codeql.yml/badge.svg)](https://github.com/afonsoft/taskboard-ai/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **Idioma padrão:** Inglês (en-us). Veja a [README.md](README.md) para a versão em inglês.

Taskboard local-first e AI-native inspirado no `dashi-taskboard`, reescrito em **C# 14 / .NET 10**.

## Visão Geral

O `taskboard-ai` é um quadro de tarefas local-first para desenvolvedores e agentes de IA. Oferece sistema de tarefas com SQLite, API REST, Server-Sent Events (SSE), CLI `taskctl`, servidor MCP, integração de chat com IA e UI web Blazor WebAssembly — tudo implementado em .NET 10 com ABP N-Layer / DDD.

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
  Taskboard.Tests.Unit/             # 89 testes unitários
  Taskboard.Tests.Integration/      # 9 testes de integração
```

## Início Rápido

```bash
git clone https://github.com/afonsoft/taskboard-ai.git
cd taskboard-ai
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

- Build e testes em Release, verificação de formatação, gate de cobertura de linhas (atualmente 45%, subindo gradualmente até a meta de 80%) e verificação de pacotes vulneráveis.
- Análise SonarCloud quando o secret `SONAR_TOKEN` está configurado.
- Análise CodeQL para C# e GitHub Actions.
- Atualizações semanais de pacotes NuGet e GitHub Actions através do Dependabot.

## GitHub Kanban e Agentes de IA

Defina `GITHUB_TOKEN` antes de iniciar o servidor:

```bash
export GITHUB_TOKEN=seu-token-do-github
```

Abra `/github-board` para visualizar as issues do GitHub como um board Kanban. Arraste uma issue para **In Progress**, selecione um agente instalado e acompanhe a execução na aba **Logs**. As CLIs suportadas são Devin, Claude, Codex, OpenCode e OpenHands. Os logs dos agentes são transmitidos em tempo real pelo hub SignalR em `/agent-log-hub`.

## Destaques Recentes

- Orquestração de agentes com detecção de CLI e streaming de logs via SignalR.
- Persistência de `AgentLogMessage` em SQLite via EF Core.
- Adapter JSON-RPC ACP sobre stdin/stdout para comunicação com agentes.
- Harness `.devin/` para Devin CLI e `~/.gemini/skills/` para Google Antigravity.
- Refinamento das GitHub Actions com cache, concurrency, permissions, SonarCloud e CodeQL.

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
