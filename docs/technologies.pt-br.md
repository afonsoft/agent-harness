# Tecnologias

| Categoria | Tecnologia | Versão |
|---|---|---|
| Linguagem | C# | 14 |
| Runtime | .NET | 10.0 |
| Web Framework | ASP.NET Core | 10.0 |
| Framework DDD | ABP N-Layer | 9.x |
| ORM | Entity Framework Core | 10.0 |
| Banco de Dados | SQLite | embutido |
| Parser de CLI | System.CommandLine | latest stable |
| MCP SDK | ModelContextProtocol | latest stable for .NET |
| Testes | xUnit + Shouldly + NSubstitute | latest stable |
| Frontend | Blazor WebAssembly | .NET 10 |
| Componentes de UI | Blazor.Bootstrap | 4.0.0 |
| Tempo real | ASP.NET Core SignalR | 10.0 |
| Cliente da API do GitHub | Octokit | 14.0.0 |
| Provedores de IA | OpenAI / Claude / Azure OpenAI (abstração) | — |

## Ferramentas

- `dotnet` CLI 10.0+
- `dotnet-ef` (migrations)
- `shellcheck` (scripts)
- `npm` / `node` (opcional, para build do frontend React)

## Configuração de Build

- `LangVersion` configurado para `14.0`
- `Nullable` desabilitado para projetos no estilo EAF
- `TreatWarningsAsErrors` habilitado
- `common.props` centraliza versões NuGet (a ser criado em `src/`)
