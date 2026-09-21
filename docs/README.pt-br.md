# Documentação do Sistema

## Visão Geral

O **Harness** (`agent-harness`) é um clone .NET 10 do taskboard local-first `dashi-taskboard`. É construído com ABP N-Layer / Domain-Driven Design e expõe interfaces HTTP REST, SSE, CLI (`taskctl`) e servidor MCP.

## Arquitetura

| Camada | Responsabilidade | Projetos |
|---|---|---|
| Domain | Regras de negócio, agregados, entidades, value objects | `Taskboard.Domain` |
| Application.Contracts | DTOs, interfaces, commands/queries | `Taskboard.Application.Contracts` |
| Application | Casos de uso, handlers MediatR | `Taskboard.Application` |
| Infrastructure | EF Core, SQLite, repositórios | `Taskboard.EntityFrameworkCore` |
| Presentation | Minimal APIs, SSE, arquivos estáticos | `Taskboard.Server` |
| Tools | CLI, MCP server | `Taskboard.Cli`, `Taskboard.Mcp` |
| Módulos | IA, workflow, cloud, integrações | `Taskboard.*` |

## Estrutura de Diretórios

```text
src/               Projetos .NET 10
tests/             Testes xUnit/Shouldly/NSubstitute
.specs/            Especificações SDD
docs/              Documentação do sistema
.claude/           Harness Claude Code / Devin CLI
.devin/            Configuração Devin CLI
.agent/            Skills Google Antigravity
```

## Início Rápido

Veja [`README.md`](../README.md) (en-us padrão) ou [`README.pt-br.md`](../README.pt-br.md).

## Referências

- [technologies.pt-br.md](./technologies.pt-br.md)
- [packages.pt-br.md](./packages.pt-br.md)
- [plugins.pt-br.md](./plugins.pt-br.md)
- [features.pt-br.md](./features.pt-br.md)
- [api.pt-br.md](./api.pt-br.md)
