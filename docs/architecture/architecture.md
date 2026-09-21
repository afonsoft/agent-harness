# System Architecture - agent-harness

This document provides a textual representation of the system architecture defined in `architecture.html`.

## 🏛️ Architectural Overview
`agent-harness` is built as a **Modular Monolith** using the **ABP N-Layer / Clean Architecture** pattern. It is designed to be local-first, prioritizing SQLite for data storage while allowing optional Cloud and Jira synchronization.

### 🛠️ Component Map

#### 1. Presentation Layer (The Entry Points)
These components are the primary interfaces for users and agents:
- **Users:** The external actors (Humans or AI Agents).
- **taskctl CLI:** A .NET console application using `Spectre.Console.Cli` for automation and agent interaction.
- **MCP Server:** A Model Context Protocol server that exposes the taskboard's capabilities as tools to LLMs.
- **Blazor / MAUI UI:** The visual interface for managing the board, tasks, and settings.

#### 2. API Gateway (`Taskboard.Server`)
Acts as the central orchestrator for all incoming requests:
- **Technology:** ASP.NET Core Minimal APIs.
- **Responsibilities:**
  - Routing requests to the appropriate application handlers.
  - Managing **SSE (Server-Sent Events)** for real-time updates via `/api/events`.
  - Handling CORS and Instance-Token authentication.
  - Serving the Blazor WebAssembly frontend (`Taskboard.Blazor`) as static files.

#### 3. Application Layer (`Taskboard.Application`)
Contains the business use cases and orchestrates the flow between the API and the Domain:
- **Technology:** MediatR (Commands/Queries).
- **Responsibilities:** Validating input, managing transactions, and triggering domain events.

#### 4. Domain Layer (`Taskboard.Domain`)
The heart of the system containing all business rules and invariants:
- **Core Concepts:**
  - The board's source of truth is GitHub (issues + labels); there are no local `Project`/`Task` aggregates.
  - **Entities:** `AiChatThread` (Runs, Events), `AgentRun`, `AgentLog`, `IssueHistoryEvent`, `ConfigurationOverride`, `UserPreference`, `AgentPreference`.
  - **Invariants:** Optimistic concurrency (`Version`) where applicable.

#### 5. Persistence Layer (`Taskboard.EntityFrameworkCore`)
Handles the durable storage of the system:
- **Technology:** EF Core 10 + SQLite.
- **Responsibilities:** Mapping domain entities to tables, managing migrations, and implementing the Repository pattern.

#### 6. Vertical Modules (Extension Slices)
Specialized modules that extend the core functionality:
- **AI Chat Module:** Manages LLM conversations, runs, and thread-specific event streams.
- **Cloud Companion:** Manages local-to-cloud proxying (Cloudflare D1/R2) and session synchronization.
- **GitHub Actions Monitor:** `/workflow` is a read-only monitor of GitHub Actions workflows and runs (via Octokit `Actions.*`) — the legacy local workflow engine/workspaces were removed.
- **Integrations:** Handles external synchronization, specifically with the Jira REST API.
- **Agent Orchestration:** Connects `Application.Contracts/Agents` to
  `Integrations/Agents`, the `Server` SignalR hub, and the Blazor GitHub
  components for CLI agent discovery, execution, cancellation, and logs.

The Agents module follows the dependency path
`Application.Contracts → Integrations → Server hub → Blazor`. `IAgentLogBroadcaster`
lives in `Application.Contracts` and is implemented by `Taskboard.Server`, so
`Taskboard.Integrations` does not depend on the Server layer.

### 🔄 Primary Data Flows

**1. The Request Path (Read/Write)**
`User` $\rightarrow$ `CLI/UI/MCP` $\rightarrow$ `Server (API)` $\rightarrow$ `Application (MediatR)` $\rightarrow$ `Domain` $\rightarrow$ `EF Core` $\rightarrow$ `SQLite`

**2. The Real-time Event Path**
`Domain Event` $\rightarrow$ `Application` $\rightarrow$ `SSE EventHub` $\rightarrow$ `User (Browser/Client)`

---
*Generated from `docs/architecture/architecture.json`*
