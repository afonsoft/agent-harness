# Tecnologias

| Categoria | Tecnologia | Versão |
|---|---|---|
| Linguagem | C# | 14 |
| Runtime | .NET | 10.0 |
| Web Framework | ASP.NET Core | 10.0 |
| Framework DDD | ABP N-Layer | 9.x |
| ORM | Entity Framework Core | 10.0 |
| Banco de Dados | SQLite | embutido |
| Parser de CLI | Spectre.Console.Cli | 0.57.2 |
| MCP SDK | ModelContextProtocol | latest stable for .NET |
| Testes | xUnit + Shouldly + NSubstitute | latest stable |
| Frontend | Blazor WebAssembly | .NET 10 |
| Componentes de UI | Blazor.Bootstrap | 4.0.0 |
| Tempo real | ASP.NET Core SignalR | 10.0 |
| Cliente da API do GitHub | Octokit | 14.0.0 |
| Provedores de IA | OpenAI / Claude / Azure OpenAI (abstração) | — |
| Cache | HybridCache (L1 em memória + L2 Redis opcional via `Taskboard:Cache:Redis:ConnectionString`) | 10.10.0 |

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

## Bundle WASM (SPEC-20261003-perf-pass RF-001)

`RunAOTCompilation` foi **avaliado e rejeitado** em 2026-10-03
(.NET 10, `wasm-tools` 10.0.12):

| Métrica | Sem AOT | Com AOT |
|---|---|---|
| Tempo de `dotnet publish` | ~36 s | ~5 min 49 s |
| `_framework` em disco | 29 MB | 74 MB |
| Total comprimido na rede (`.br`) | ~5,0 MB | ~10,3 MB |

AOT dobra o download de cold-start e adiciona ~5 min a cada publish —
os caminhos quentes (chat, terminal, polling) são limitados por I/O e
renderização, não por CPU, então a troca não compensa hoje. Reavaliar
se um feature pesada de CPU no cliente (ex.: inferência local,
renderização de diffs grandes) for adicionada. Orçamento de bundle:
manter o wire do `_framework` ≤ ~6 MB comprimido; os maiores itens são
`dotnet.native.wasm` (~1 MB br) e `System.Private.CoreLib` (~0,6 MB br).
