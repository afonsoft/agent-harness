# SPEC-20260915-blazor-wasm-migration

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `blazor-wasm-migration` |
| Type | `Refactor / Infra` |
| Stack | `.NET 10 / Blazor WebAssembly / ASP.NET Core Minimal APIs / SignalR` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260915-blazor-wasm-migration` |
| Ticket | N/A |
| Status | `Done (merged via PR #78)` |

## 1. User Story

**As a** Taskboard operator,
**I want** the UI migrated from Blazor Server to Blazor WebAssembly hosted by `Taskboard.Server`, following the `LangGraph-UI` (KnowledgeHub) Client/Server pattern — with a branded loading screen, the existing sidebar menu preserved, and proxy-hardened boot assets,
**So that** the app scales without per-circuit server state, survives corporate proxies that block `_framework` binaries, and the SignalR hub bug (server-side self-connection without cookie → circuit crash) disappears by construction.

**Problem context:**

- Today `Taskboard.Blazor` is an RCL rendered via `MapRazorComponents<App>().AddInteractiveServerRenderMode()` inside `Taskboard.Server` — every UI event requires the SignalR circuit; circuit death shows "An unexpected error occurred".
- `TaskLogTab` connects server→itself via SignalR without the auth cookie and crashes the circuit (confirmed in production logs — `InvalidDataException` on negotiate).
- Corporate proxies block `_framework` assets by extension (`.dat`/`.wasm`) — KnowledgeHub solved this with `/framework-assets/{stem}/{ext}` + `?enc=b64` + client-side `boot.js` chain (SPEC-20260915-wasm-boot-proxy-hardening, proven in production).
- Most components already consume `TaskboardClient` (HTTP); only `IGitHubService` and `IAgentOrchestrationService` are injected directly and lack REST endpoints.
- Auth already returns `401`/`403` (not redirects) for `/api/*`; the HTML redirect middleware only affects non-API paths.

## 2. Scope

**In scope:**

- New `src/Taskboard.Client` Blazor WASM project (SDK `Microsoft.NET.Sdk.BlazorWebAssembly`): `Program.cs`, `wwwroot/index.html` (loading spinner + `#blazor-error-ui`), `wwwroot/js/boot.js`, `_Imports.razor`, `App.razor`.
- `Taskboard.Blazor` converted to a pure RCL consumable by WASM (drop `FrameworkReference Microsoft.AspNetCore.App` → `Microsoft.AspNetCore.Components(.Web)` package refs; remove server-only services).
- New REST endpoints: `GET /api/auth/me`, `GET/POST /api/github/*`, `GET/POST /api/agents/*` covering everything the UI injects directly today.
- WASM services: `AuthenticationStateProvider` + `AuthorizeRouteView` + `RedirectToLogin`, `AuthRedirectHandler` (401 → `/login`), typed `GitHubApiClient` and `AgentsApiClient`, `TaskboardClient` rewired to `builder.HostEnvironment.BaseAddress` without `CircuitAuthContext`.
- Server hosting: `MapStaticAssets` + `MapFrameworkAssetsApi()` (ported verbatim semantics from KnowledgeHub: `{fileName}` legacy route, `{stem}/{ext}` extensionless route, `?enc=b64`, per-ext `Content-Type`, immutable cache, traversal rejection) + `MapFallbackToFile("index.html")`.
- Remove the HTML login-redirect middleware (SPA fallback is anonymous; APIs already 401/403; client-side routing guards pages).
- SignalR `/agent-log-hub` consumed by the WASM browser client (cookie flows natively — fixes the previously diagnosed crash).
- Loading screen + menu: port the KnowledgeHub boot (spinner SVG in `#app`, `autostart="false"` + `boot.js`) while keeping the Taskboard `MainLayout`/`NavMenu` visual identity.
- `install.sh`, `Dockerfile`, `README`/`docs` updates for the new build/publish shape.

**Out of scope:**

- Any UI redesign — pages keep current markup/components (Blazor.Bootstrap stays; no BootstrapBlazor port).
- MCP server (`Taskboard.Mcp`), CLI (`taskctl`), Domain/Application/EF Core — untouched.
- Service worker / PWA / offline mode.
- `_content/*` asset proxying (fonts) — same limitation as KnowledgeHub; follow-up if a proxy proves to block them.
- Per-user multi-tenancy; auth remains the single admin cookie session.

## 3. Technical Context

**Where the change happens:**

- New WASM host project + RCL refactor + new API groups in `Taskboard.Server`.
- `Taskboard.Server` keeps every existing endpoint and the hub; it additionally serves the published WASM static output (hosted-mode publish: server `ProjectReference` to the client emits `wwwroot/_framework`).

**Files to read before implementing:**

- `CLAUDE.md` · `.claude/rules/global-rules.md`
- `src/Taskboard.Server/Program.cs` (auth config lines ~146-176, redirect middleware ~1040-1067, `MapHub`, `MapFallbackToFile`)
- `src/Taskboard.Blazor/**` (all components; injections inventory below)
- `src/Taskboard.Blazor/Services/TaskboardClient.cs`, `CircuitAuthContext.cs`, `Components/AuthCookieCapture.razor`
- LangGraph-UI reference implementation:
  - `repos/LangGraph-UI/src/KnowledgeHub.Client/Program.cs`, `App.razor`, `wwwroot/index.html`, `wwwroot/js/boot.js`, `Services/{AuthApiClient,AuthRedirectHandler,KhAuthenticationStateProvider}.cs`, `RedirectToLogin.razor`
  - `repos/LangGraph-UI/src/KnowledgeHub.Server/Program.cs` + `Api/FrameworkAssetsEndpoints.cs`
  - `repos/LangGraph-UI/.specs/SPEC-20260915-wasm-boot-proxy-hardening.md`
- `install.sh`, `Dockerfile`, `Directory.Packages.props`

**Component → dependency inventory (what must be rewired):**

| Component | Today | WASM replacement |
| --- | --- | --- |
| `BoardView`, `KanbanBoard`, `NewTaskDialog` | `IGitHubService` | `GitHubApiClient` → `/api/github/*` |
| `KanbanBoard`, `AgentSelectionModal`, `TaskLogTab` | `IAgentOrchestrationService` | `AgentsApiClient` → `/api/agents/*` |
| `TaskLogTab` | `HubConnection` (server-side, no cookie) | same `HubConnection` but browser-side — cookie flows, bug eliminated; keep try/catch hardening |
| `MainLayout` | `IHttpContextAccessor` | `AuthenticationStateProvider` / `AuthorizeView` |
| `AuthCookieCapture`, `CircuitAuthContext` | prerender cookie capture | **deleted** — browser sends cookie |
| All other pages | `TaskboardClient` | unchanged calls; new registration |
| `NavMenu`, `Prompts` | `IJSRuntime` | unchanged |

**Files to create or modify:**

```text
src/Taskboard.Client/                                   # NEW PROJECT
  Taskboard.Client.csproj         (Sdk.BlazorWebAssembly; refs Blazor RCL + Application.Contracts)
  Program.cs                      (WebAssemblyHostBuilder, HttpClient+AuthRedirectHandler, providers)
  App.razor                       (CascadingAuthenticationState + AuthorizeRouteView + RedirectToLogin)
  RedirectToLogin.razor
  _Imports.razor
  wwwroot/index.html              (#app loading spinner, blazor-error-ui, autostart=false)
  wwwroot/js/boot.js              (3-layer proxy-resilient loadBootResource — ported)
  wwwroot/css/app.css             (existing taskboard styles + loading-progress css)
  Services/AuthApiClient.cs       (login/logout/me)
  Services/TaskboardAuthenticationStateProvider.cs
  Services/AuthRedirectHandler.cs
  Services/GitHubApiClient.cs
  Services/AgentsApiClient.cs
src/Taskboard.Blazor/
  Taskboard.Blazor.csproj         (drop FrameworkReference → package refs; IsPackable stays false)
  Services/TaskboardClient.cs     (remove CircuitAuthContext dependency)
  Components/AuthCookieCapture.razor  # DELETE
  Services/CircuitAuthContext.cs      # DELETE
  Components/Pages/Login.razor    (form post → AuthApiClient fetch)
  Components/GitHub/TaskLogTab.razor  (hub URL + graceful failure)
  Components/GitHub/{KanbanBoard,NewTaskDialog,AgentSelectionModal}.razor (service→client swap)
  Components/BoardView.razor      (IGitHubService→GitHubApiClient)
  Layout/MainLayout.razor         (IHttpContextAccessor→auth state)
src/Taskboard.Server/
  Program.cs                      (remove redirect middleware; MapStaticAssets; MapFrameworkAssetsApi;
                                   MapFallbackToFile("index.html"); new endpoint groups)
  Api/GitHubEndpoints.cs          # NEW — /api/github/*
  Api/AgentsEndpoints.cs          # NEW — /api/agents/*
  Api/AuthEndpoints.cs            # NEW or extend — GET /api/auth/me
  Api/FrameworkAssetsEndpoints.cs # NEW — ported mirror
  Taskboard.Server.csproj         (ProjectReference Taskboard.Client; hosted WASM publish)
Taskboard.sln, Directory.Packages.props, install.sh, Dockerfile, docs/
tests/…                           (new endpoint tests; existing UI-agnostic tests untouched)
```

## 4. Requirements

### RF-001: WASM host project

- **Description:** `Taskboard.Client` (`Sdk.BlazorWebAssembly`, `net10.0`) with `WebAssemblyHostBuilder`, root `App` at `#app`, `HeadOutlet` at `head::after`; registers `AddAuthorizationCore`, `AuthenticationStateProvider`, `AuthRedirectHandler` + `HttpClient(BaseAddress = HostEnvironment.BaseAddress)`, `TaskboardClient`, `AuthApiClient`, `GitHubApiClient`, `AgentsApiClient`, Blazor.Bootstrap services.
- **Input → Output:** `dotnet run --project src/Taskboard.Server` serves the WASM app.

### RF-002: Loading screen and boot pipeline

- **Description:** `index.html` renders the KnowledgeHub-style loading spinner (`.loading-progress` SVG + `.loading-progress-text`) inside `#app` plus `#blazor-error-ui`; `blazor.webassembly.js` loads with `autostart="false"` and `js/boot.js` calls `Blazor.start`. Boot rejection replaces `#app` with a readable proxy-guidance message + reload link (pt-BR text, matching KnowledgeHub's `showBootError`).

### RF-003: Proxy-resilient asset loading

- **Description:** Port `boot.js` `loadBootResource` chain unchanged in behavior: `.js` and non-`_framework` paths → default; others split `stem`/`ext` → (1) `GET /framework-assets/{stem}/{ext}` with `integrity` pass-through → (2) `?enc=b64` with `crypto.subtle` SHA-256 verification (chunked `atob` fallback) → (3) `defaultUri`.
- **Description (server):** Port `FrameworkAssetsEndpoints` — `GET /framework-assets/{fileName}` (legacy) and `/{stem}/{ext}` (raw + `?enc=b64`); `ValidName`/`ValidExt` regexes; `..` rejection; `.br`/`.gz` sibling serving; per-ext `Content-Type` (`wasm`/`json`/`js`/`octet-stream`); `Cache-Control: public,max-age=31536000,immutable`; `Vary: Accept-Encoding`; anonymous.

### RF-004: Auth state in WASM

- **Description:** New `GET /api/auth/me` → `200 { username }` when the cookie session is valid, `401` otherwise. `TaskboardAuthenticationStateProvider` calls it once and caches; `App.razor` wraps the router in `CascadingAuthenticationState` + `AuthorizeRouteView` (`NotAuthorized` → `RedirectToLogin`, `Authorizing` → loading text). `AuthRedirectHandler` (`DelegatingHandler`) navigates to `/login` on `401`/`403` responses.
- **Rules:** `Login.razor` posts credentials via `AuthApiClient` (`POST /api/login` JSON or form — match existing endpoint contract) instead of browser form post; on success `NotifyAuthenticationStateChanged` and navigate `/`. Logout calls `POST /api/logout` then navigates `/login`.

### RF-005: Remove HTML redirect middleware

- **Description:** Delete the `app.Use` block that redirects unauthenticated non-API paths to `/login` (lines ~1040-1067). `index.html`/static assets/`/framework-assets` are anonymous; API endpoints keep `RequireAuthorization`; the hub keeps working because the browser negotiates with the cookie (authenticated → `next()` was the old behavior anyway — after removal the hub path simply isn't intercepted).

### RF-006: GitHub API surface

- **Description:** New `api.MapGroup("github")` (RequireAuthorization):
  - `GET /api/github/repos` → `IReadOnlyList<RepositoryDto>`
  - `GET /api/github/repos/{owner}/{repo}/issues` → `IReadOnlyList<IssueDto>` (labels filtered as today)
  - `PATCH /api/github/repos/{owner}/{repo}/issues/{number}/column` `{ from, to }`
  - `POST /api/github/repos/{owner}/{repo}/issues/{number}/labels` `{ labels[] }`
  - `POST /api/github/repos/{owner}/{repo}/issues` `{ title, body, labels[] }` (NewTaskDialog)
- **Input → Output:** thin endpoints delegating to `IGitHubService`; token missing → `503`/`ProblemDetails` consistent with existing error style.

### RF-007: Agents API surface

- **Description:** New `api.MapGroup("agents")` (RequireAuthorization):
  - `GET /api/agents` → `IReadOnlyList<AgentInfo>` (with Busy status)
  - `POST /api/agents/executions` `{ AgentExecutionRequest }` → `202` (enqueue)
  - `GET /api/agents/executions/{issueId}/logs` → `IReadOnlyList<AgentLogMessage>`
  - `DELETE /api/agents/executions/{issueId}` → cancel
- **Input → Output:** delegate to `IAgentOrchestrationService`.

### RF-008: RCL compatibility

- **Description:** `Taskboard.Blazor` drops `FrameworkReference Microsoft.AspNetCore.App`; adds `Microsoft.AspNetCore.Components`, `Microsoft.AspNetCore.Components.Web`, `Microsoft.AspNetCore.Components.Authorization` (needed for `[Authorize]`), `Microsoft.AspNetCore.Components.WebAssembly` is **not** referenced by the RCL. Server-only code paths (`IHttpContextAccessor`, `PersistentComponentState`, `CircuitAuthContext`) removed. Blazor.Bootstrap + SignalR.Client package refs stay.
- **Rules:** no `System.Web`/`HttpContext` usage remains in the RCL; `_Imports.razor` pruned (`Microsoft.AspNetCore.Http`, `RenderMode` static using removed).

### RF-009: Menu and layout preservation

- **Description:** `MainLayout`/`NavMenu` keep the current taskboard look (sidebar, links, login state area) — ported verbatim except replacing `IHttpContextAccessor` user display with `AuthorizeView`/`AuthenticationStateProvider`. The collapsible menu behavior is unchanged. `Login`/`MinimalLayout` remain for unauthenticated routes.

### RF-010: Server hosting shape

- **Description:** `Taskboard.Server` gains `ProjectReference` → `Taskboard.Client`; `MapStaticAssets()` serves fingerprinted assets + `index.html`; `MapFallbackToFile("index.html")` last; `MapRazorComponents`/`AddInteractiveServerRenderMode` and Blazor-Server hub removed; `MapHub<AgentLogHub>` retained for the WASM `TaskLogTab`.
- **Rules:** `swagger`, `/api`, `/health`, `/agent-log-hub`, `/framework-assets` all resolve before the SPA fallback.

### RF-011: Packaging

- **Description:** `dotnet publish -c Release` on `Taskboard.Server` emits the hosted WASM bundle under `wwwroot`; `install.sh` build path unchanged (`Taskboard.sln` includes the new project); `Dockerfile` builds the Server project only — no nginx layer needed (same-process hosting, like KnowledgeHub). Update `README`/docs ports and architecture notes.

**Business rules / invariants:**

- Zero API contract changes for `taskctl`/MCP consumers.
- No server-side per-user UI state remains (all state in browser + SQLite via API).
- `/_framework/` asset integrity preserved end-to-end (SRI native on layer 1, `crypto.subtle` on layer 2).

## 5. API Contract

**New endpoints:**

```text
GET    /api/auth/me                       → 200 { "userName": "admin" } | 401
GET    /api/github/repos                  → 200 RepositoryDto[]
GET    /api/github/repos/{o}/{r}/issues   → 200 IssueDto[] | 503 (no token)
PATCH  /api/github/repos/{o}/{r}/issues/{n}/column  { "from": "backlog", "to": "done" } → 204
POST   /api/github/repos/{o}/{r}/issues/{n}/labels  { "labels": ["in-progress"] } → 204
POST   /api/github/repos/{o}/{r}/issues   { "title": "…", "body": "…", "labels": [] } → 201 IssueDto
GET    /api/agents                        → 200 AgentInfo[]
POST   /api/agents/executions             AgentExecutionRequest → 202
GET    /api/agents/executions/{issueId}/logs → 200 AgentLogMessage[]
DELETE /api/agents/executions/{issueId}   → 204
GET    /framework-assets/{stem}/{ext}[?enc=b64]  → 200 bytes | 200 text/plain | 404
```

**Auth:** cookie session for browser clients; `401`/`403` JSON for `/api/*` (existing behavior, unchanged).

## 6. Acceptance Criteria

- [x] **Given** a fresh publish **when** the browser hits `/` unauthenticated **then** the loading spinner shows, WASM boots, and the client routes to `/login` (index.html served anonymously).
- [ ] **Given** valid credentials **when** login posts **then** cookie is set, auth state propagates, and the board renders.
- [ ] **Given** the board **when** a task card is clicked **then** the detail modal opens, `TaskLogTab` negotiates `/agent-log-hub` with the browser cookie (200 negotiate, WebSocket established) — no circuit error bar.
- [x] **Given** DevTools blocking `/_framework/*.dat` **when** the app boots **then** assets flow via `/framework-assets/{stem}/{ext}` (and `?enc=b64` when sniffing is simulated) — byte-identical integrity.
- [ ] **Given** an expired session **when** any API returns 401 **then** `AuthRedirectHandler` navigates to `/login` — no unhandled error UI.
- [x] **Given** `dotnet build -c Release` and `dotnet test` **then** everything compiles clean (warnings-as-errors) and the suite passes, including new `FrameworkAssetsTests` + GitHub/Agents endpoint tests.
- [ ] **Given** the Docker image **when** rebuilt and run **then** `http://localhost:47823` serves the WASM app end-to-end (board, settings, skills, chat, workflow).
- [ ] **Given** `taskctl` and the MCP server **when** exercised against the new build **then** behavior is unchanged.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| No `GITHUB_TOKEN` | open board | repo combobox disabled / free-text warning — same as today via `503`/empty list handling |
| Deep link `/settings` unauthenticated | direct URL | index.html → WASM router → RedirectToLogin |
| Proxy strips query string | `?enc=b64` dropped | raw bytes returned → client digest check fails → layer 3 |
| `Taskboard.Blazor` still references `HttpContext` | build | compile error — must be zero |
| Slow WASM boot | low-end device | spinner persists; failure shows readable message, not blank page |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read section-3 files; diff Taskboard vs KnowledgeHub boot/auth wiring; enumerate every endpoint `TaskboardClient` calls.
- [x] **T2 — Server API:** `GET /api/auth/me`, `GitHubEndpoints`, `AgentsEndpoints`, `FrameworkAssetsEndpoints` + tests (red → green).
- [x] **T3 — RCL decoupling:** csproj package swap; delete `AuthCookieCapture`/`CircuitAuthContext`; rewire `TaskboardClient` registration; swap direct service injections for the new clients; fix `MainLayout`/`Login`/`TaskLogTab`.
- [x] **T4 — WASM host:** `Taskboard.Client` project (Program.cs, App.razor, RedirectToLogin, auth provider, `AuthApiClient`, `AuthRedirectHandler`, `index.html` loading UI, `boot.js` port).
- [x] **T5 — Server hosting:** project ref, `MapStaticAssets`, `MapFrameworkAssetsApi()`, `MapFallbackToFile`, remove redirect middleware + `AddInteractiveServerRenderMode`/`MapRazorComponents`.
- [x] **T6 — Packaging:** sln, `Directory.Packages.props`, `install.sh`, `Dockerfile`, docs.
- [ ] **T7 — Validation:** build, `dotnet format`, `dotnet test`; manual smoke of every page; forced-failure boot test (DevTools request blocking) exercising boot.js layers 2–3.
- [ ] **T8 — Done + PR:** `Status = Done`, PR on `feature/devin-20260915-blazor-wasm-migration`.

**7.1 Validation strategy:** Refactor — the full existing suite must pass unchanged; new endpoints get integration tests (`WebApplicationFactory`); boot/proxy behavior verified by the ported `FrameworkAssetsTests` pattern + manual DevTools smoke. No behavioral change without a covering test.

## 8. Organization Guardrails

- **Branches:** `feature/devin-20260915-blazor-wasm-migration`; never `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched (CI already builds the sln — new project flows in automatically).
- **Security:** cookie `SameSite=Strict` + `HttpOnly` preserved; `/framework-assets` anonymous but read-only/regex-validated/rooted under `_framework`; no tokens in client bundle; API `401` not redirects.
- **Scope:** no UI redesign, no BootstrapBlazor swap, no PWA/service worker.
- **Architecture:** no business logic moves into the client; the RCL holds presentation only; all mutations go through `/api`.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] All acceptance criteria (section 6) verified — including forced-failure boot and the task-click/log-tab scenario.
- [ ] `dotnet build` clean, `dotnet test` green, `dotnet format --verify-no-changes` clean.
- [ ] Docker + `install.sh` produce a working deployment.
- [ ] Guardrails respected; no secrets; SPEC statuses referenced remain accurate.

## Open Questions / Pending Ambiguity

- Resolved: structure (new `Taskboard.Client` + `Taskboard.Blazor` RCL), auth (`AuthenticationStateProvider` + `/api/auth/me`, 401-redirect handler, HTML redirect middleware removed), API surface (`/api/github/*` + `/api/agents/*`), hub (browser-side SignalR with native cookie — also fixes the diagnosed circuit crash).
- `[A DEFINIR]` whether `AiChat`'s thread-event consumption needs a dedicated streaming endpoint in WASM — implementation must check how `TaskboardClient` consumes `/api/local/ai/threads/{id}/events` today (SSE via `HttpCompletionOption.ResponseHeadersRead` works in WASM).
