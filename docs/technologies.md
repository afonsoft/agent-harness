# Technologies

| Category | Technology | Version |
|---|---|---|
| Language | C# | 14 |
| Runtime | .NET | 10.0 |
| Web Framework | ASP.NET Core | 10.0 |
| DDD Framework | ABP N-Layer | 9.x |
| ORM | Entity Framework Core | 10.0 |
| Database | SQLite | bundled |
| CLI Parser | Spectre.Console.Cli | 0.55.0 |
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

## WASM bundle (SPEC-20261003-perf-pass RF-001)

`RunAOTCompilation` was **evaluated and rejected** on 2026-10-03
(.NET 10, `wasm-tools` 10.0.12):

| Metric | No AOT | AOT |
|---|---|---|
| `dotnet publish` wall time | ~36 s | ~5 min 49 s |
| `_framework` on disk | 29 MB | 74 MB |
| Compressed wire total (`.br`) | ~5.0 MB | ~10.3 MB |

AOT doubles the cold-load download and adds ~5 min to every publish —
the hot paths (chat UI, terminal, polling) are I/O- and render-bound,
not CPU-bound, so the trade is not worth it today. Re-evaluate if a
CPU-heavy client feature (e.g. local inference, large diff rendering)
ships. Bundle budget: keep `_framework` wire ≤ ~6 MB compressed; the
largest items are `dotnet.native.wasm` (~1 MB br) and `System.Private.CoreLib`
(~0.6 MB br).
