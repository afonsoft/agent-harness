# Packages

## NuGet (planned)

| Package | Purpose |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | SQLite EF Core provider |
| `Microsoft.EntityFrameworkCore.Tools` | Migrations CLI |
| `MediatR` | CQRS commands/queries |
| `Microsoft.Extensions.Hosting` | Background services and hosted agent orchestration |
| `Microsoft.AspNetCore.OpenApi` / `Swashbuckle.AspNetCore` | OpenAPI documentation |
| `Microsoft.AspNetCore.SignalR.Client` | SignalR client for real-time agent logs |
| `Spectre.Console.Cli` | CLI parsing |
| `ModelContextProtocol` | MCP server |
| `Octokit` | GitHub API integration |
| `Blazor.Bootstrap` | Blazor UI components (Bootstrap 5) |
| `xunit` | Unit tests |
| `Shouldly` | Fluent assertions |
| `NSubstitute` | Mocking |
| `Microsoft.AspNetCore.Mvc.Testing` | Integration tests |
| `Bogus` | Test data generation (optional) |

## NPM (optional, fase 1 frontend)

| Package | Purpose |
|---|---|
| `react` | UI library |
| `vite` | Build tool |
| `typescript` | Type checking |
| `@xyflow/react` | Workflow graph editor |
| `dhtmlx-gantt` | Gantt view |

> Package versions are not pinned here; they will be set in `Directory.Packages.props` or `package.json` during implementation.
