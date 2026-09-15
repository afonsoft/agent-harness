# Technologies

| Category | Technology | Version |
|---|---|---|
| Language | C# | 14 |
| Runtime | .NET | 10.0 |
| Web Framework | ASP.NET Core | 10.0 |
| DDD Framework | ABP N-Layer | 9.x |
| ORM | Entity Framework Core | 10.0 |
| Database | SQLite | bundled |
| CLI Parser | System.CommandLine | latest stable |
| MCP SDK | ModelContextProtocol | latest stable for .NET |
| Tests | xUnit + Shouldly + NSubstitute | latest stable |
| Frontend | Blazor WebAssembly | .NET 10 |
| UI Components | Blazor.Bootstrap | 4.0.0 |
| Real-time | ASP.NET Core SignalR | 10.0 |
| GitHub API Client | Octokit | 14.0.0 |
| AI Providers | OpenAI / Claude / Azure OpenAI (abstracted) | — |

## Tooling

- `dotnet` CLI 10.0+
- `dotnet-ef` (migrations)
- `shellcheck` (scripts)
- `npm` / `node` (optional, for React frontend build)

## Build Configuration

- `LangVersion` set to `14.0`
- `Nullable` disabled for EAF-style projects
- `TreatWarningsAsErrors` enabled
- `common.props` centralizes NuGet versions (to be created under `src/`)
