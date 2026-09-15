# SPEC-20260915-api-authorization-hardening

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `api-authorization-hardening` |
| Type | `Bugfix` |
| Stack | `.NET` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260915-api-authorization-hardening` |
| Ticket | `GAP-security-api-anonymous-surface` (gap-analysis-20260915) |
| Status | `Implemented` |

## 1. User Story

**As a** Taskboard AI operator
**I want** every `/api` endpoint to require authentication, with machine clients (`taskctl`, MCP server) authenticating via API key
**So that** the WASM migration's removal of the HTML redirect middleware does not leave the entire API — including stored credentials and mutation endpoints — anonymously reachable.

**Problem context:**

`SPEC-20260915-blazor-wasm-migration` removed the middleware that redirected unauthenticated non-API requests to `/login`. That middleware had an undocumented side effect: it implicitly protected every API endpoint that lacked explicit `RequireAuthorization`. Evidence (reproduced against the running deployment):

- `GET /api/tasks` → `200`, `GET /api/projects` → `200`, `GET /api/local/jira-connection` → `200` — data readable without a session.
- `POST /api/projects` → `201` — **anonymous writes**: projects, tasks, comments, attachments, Jira config, AI thread runs are all creatable/mutable without authentication.
- `GET /api/settings` → `200` — `SettingsDto` serializes `gitHubToken` when configured (`WhenWritingNull` only hides it while null).
- Only the new `github`/`agents` groups and a few endpoints carry `RequireAuthorization` (`Program.cs:1171,1236`); the `/api` root group (`Program.cs:299`) and `projects`/`tasks`/`attachments`/`local` groups do not.

Constraint discovered during gap-analysis: `Taskboard.Cli` and `Taskboard.Mcp` call the API with a bare `HttpClient` and no credentials — locking the API breaks both unless a non-cookie scheme exists (the same problem KnowledgeHub solved with `aft_*` API keys).

## 2. Scope

**In scope:**
- `RequireAuthorization` on the `/api` group (single place), with `AllowAnonymous` on the minimal allowlist.
- API key authentication scheme (header `X-Api-Key`) composed alongside cookie auth, following the KnowledgeHub `ApiKeyAuthenticationHandler` pattern.
- `taskctl` and `Taskboard.Mcp` HTTP clients send the API key header when configured.
- `[Authorize]` on every routed page except `/login` (`BoardView`, `AiChat`, `ProjectsBoard` currently lack it).
- Integration tests asserting 401 for anonymous API access and 200/202 with cookie or API key.

**Out of scope:**
- Per-endpoint role/permission granularity (single authenticated user model is unchanged).
- API key lifecycle UI (rotation screen, multiple keys, scopes) — single configured key is enough.
- Changing the cookie scheme, login page, or `AuthRedirectHandler`.
- Rate limiting / IP allowlists.

## 3. Technical Context

**Where the change happens:**
`Taskboard.Server` composition root (`Program.cs`): authentication registration, the `api` endpoint group, and the anonymous allowlist. `Taskboard.Cli` and `Taskboard.Mcp` `TaskboardApiClient` constructors gain the key header. Razor pages gain `[Authorize]` attributes (no logic change).

**Files to read before implementing:**
- `src/Taskboard.Server/Program.cs` (auth block ~lines 171-205; `api` group line 299; all `Map*` endpoints)
- `repos/LangGraph-UI/src/KnowledgeHub.Server/Auth/ApiKeyAuthenticationHandler.cs` and `AuthPolicies.cs` (reference pattern)
- `repos/LangGraph-UI/src/KnowledgeHub.Server/Program.cs` lines ~39-80 (scheme composition)
- `src/Taskboard.Cli/Services/TaskboardApiClient.cs`, `src/Taskboard.Mcp/Services/TaskboardApiClient.cs`
- `src/Taskboard.Blazor/Components/Pages/{BoardView→Components/,AiChat,ProjectsBoard,Login}.razor`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` (catalog pattern for the new key)
- `tests/Taskboard.Tests.Integration/TaskboardWebApplicationFactory.cs`, `WasmHostingTests.cs`, `ServerEndpointsTests.cs`

**Files to create or modify:**
```text
src/Taskboard.Server/Auth/ApiKeyAuthenticationHandler.cs   # NEW
src/Taskboard.Server/Auth/AuthPolicies.cs                  # NEW (scheme + policy names)
src/Taskboard.Server/Program.cs                            # scheme registration, api group auth, allowlist
src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs  # Taskboard:ApiKey catalog entry (write-sensitive, masked)
src/Taskboard.Cli/Services/TaskboardApiClient.cs           # X-Api-Key header when configured
src/Taskboard.Mcp/Services/TaskboardApiClient.cs           # X-Api-Key header when configured
src/Taskboard.Cli/...                                      # env/config read for the key (follow existing option plumbing)
src/Taskboard.Mcp/...                                      # idem
src/Taskboard.Blazor/Components/BoardView.razor            # [Authorize]
src/Taskboard.Blazor/Components/Pages/AiChat.razor         # [Authorize]
src/Taskboard.Blazor/Components/Pages/ProjectsBoard.razor  # [Authorize]
tests/Taskboard.Tests.Integration/ApiAuthorizationTests.cs # NEW
tests/Taskboard.Tests.Integration/WasmHostingTests.cs      # adjust anonymous expectations
tests/Taskboard.Tests.Integration/ServerEndpointsTests.cs  # adjust to authenticated client
docs/installation*.md, README.md                           # TASKBOARD_API_KEY env var
```

## 4. Requirements

### RF-001: `RequireAuthorization` on the `/api` group
- **Description:** `var api = app.MapGroup("/api").RequireAuthorization()` so every API endpoint is authenticated by default; sub-groups inherit it.
- **Rules:** no per-endpoint `RequireAuthorization` may be removed from `github`/`agents`/explicit endpoints (harmless duplication is acceptable); `app.MapGet("/api/events")` must also be protected.
- **Input → Output:** anonymous request to any non-allowlisted `/api/*` → `401` (never a redirect, never `200`).

### RF-002: Anonymous allowlist
- **Description:** `AllowAnonymous` applied only to: `POST /api/login`, `GET /api/auth/me`, `GET /api/meta`. Everything else under `/api` requires auth, including `logout`, `settings`, `skills`, `client-storage`, `local/*`, `device-workspaces`, `workflow-capabilities`, `events`.
- **Rules:** `health`, `swagger`, `/`, `/login`, `/_framework`, `/framework-assets`, and static assets remain anonymous (they are outside `/api` or already `AllowAnonymous`); `/agent-log-hub` stays `RequireAuthorization`.
- **Input → Output:** `POST /api/login` anonymous → works; `GET /api/settings` anonymous → `401`.

### RF-003: API key scheme for machine clients
- **Description:** `X-Api-Key` header authentication handler (`AuthenticationSchemeOptions`, constant-time comparison) composed with the cookie scheme via an `AnyScheme` authorization policy — same dual scheme as KnowledgeHub.
- **Rules:** key read from `Taskboard:ApiKey` configuration (env `TASKBOARD_API_KEY`); when unset, the scheme still applies but any presented key fails (no bypass); the key must be masked in `GET /api/configuration` responses and never logged; constant-time (`CryptographicOperations.FixedTimeEquals`) comparison.
- **Input → Output:** request with valid `X-Api-Key` → authenticated principal; wrong/missing key → falls through to cookie check → `401` if unauthenticated.

### RF-004: `taskctl` and MCP send the key
- **Description:** `TaskboardApiClient` in both `Taskboard.Cli` and `Taskboard.Mcp` reads `TASKBOARD_API_KEY` (env, following existing option plumbing) and attaches `X-Api-Key` to every request.
- **Rules:** when the env var is absent the header is not sent (local anonymous setups keep working only if the server also has no key — same behavior contract as today); document the variable in `docs/installation*.md` and `README.md`.
- **Input → Output:** `taskctl list` against a keyed server with `TASKBOARD_API_KEY` set → `200`s; without it → `401` surfaced as a clear CLI error.

### RF-005: `[Authorize]` on protected pages
- **Description:** add `@attribute [Authorize]` to `BoardView.razor` (`/`), `AiChat.razor`, `ProjectsBoard.razor`; `Login.razor` stays anonymous.
- **Input → Output:** anonymous navigation to `/` → `AuthorizeRouteView.NotAuthorized` → `RedirectToLogin` → `/login`.

### RF-006: Sensitive keys masked in configuration API
- **Description:** `Taskboard:ApiKey` joins the runtime configuration catalog as a write-sensitive entry whose `GET /api/configuration` representation is masked (e.g. `"********"` or last-4), following whatever masking convention the catalog already uses — if none exists, masked-by-default for keys whose name contains `Key`/`Token`/`Secret`.
- **Input → Output:** `GET /api/configuration` → `ApiKey` entry shows masked value, never the plaintext.

## 5. API Contract

**Header (machine clients):** `X-Api-Key: <key>` — checked before cookie; failure falls through to cookie scheme, then `401`.

**Anonymous allowlist (final):**

| Endpoint | Method | Reason |
| --- | --- | --- |
| `/api/login` | POST | credential entry point |
| `/api/auth/me` | GET | `TaskboardAuthStateProvider` bootstrap (returns 401 when unauthenticated) |
| `/api/meta` | GET | version/transport probe used by clients pre-auth |

**New configuration key:** `Taskboard:ApiKey` — env `TASKBOARD_API_KEY`, nullable (no key configured = scheme rejects any presented key), writeable via `PUT /api/configuration` (itself authorized), masked on read.

## 6. Acceptance Criteria

- [x] **Given** no credentials **when** `GET /api/tasks`, `GET /api/projects`, `GET /api/settings`, `GET /api/local/jira-connection`, `GET /api/skills`, `GET /api/events` **then** each returns `401` (not `200`, not a redirect).
- [x] **Given** no credentials **when** `POST /api/projects`, `POST /api/tasks`, `PUT /api/settings`, `POST /api/local/jira-connection/sync`, `POST /api/local/ai/threads/{id}/runs` **then** each returns `401` — no resource is created.
- [x] **Given** a configured `TASKBOARD_API_KEY` **when** a request carries `X-Api-Key: <key>` to `GET /api/tasks` **then** `200`; with a wrong key → `401`.
- [x] **Given** valid login **when** the browser holds the session cookie **then** all endpoints behave exactly as today (cookie path unchanged).
- [x] **Given** `taskctl` with `TASKBOARD_API_KEY` set **when** it calls the API **then** requests succeed; without the env var against a keyed server → clear 401 error, no crash.
- [x] **Given** anonymous browser **when** navigating to `/`, `/ai-chat`, `/projects` **then** routed to `/login`; `/login` itself renders.
- [x] **Given** `GET /api/configuration` with a key stored **then** the `ApiKey` entry is masked.
- [x] **Given** the MCP server with `TASKBOARD_API_KEY` **when** tools invoke API endpoints **then** `200`s; without → `401`.
- [x] `dotnet build` clean; `dotnet test` green including new `ApiAuthorizationTests` and updated `WasmHostingTests`/`ServerEndpointsTests` (existing tests that hit `/api/*` anonymously must use the factory's authenticated client or expect 401).

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read section-3 files; port `ApiKeyAuthenticationHandler`/`AuthPolicies` from KnowledgeHub; enumerate the full anonymous-needed set (login, auth/me, meta only).
- [x] **T2 — Tests (red):** `ApiAuthorizationTests` — anonymous 401 matrix across representative GET/POST/PUT endpoints; allowlist 200s; API key acceptance/rejection. Watch existing anonymous-API tests fail/adjust.
- [x] **T3 — Server auth:** `AuthPolicies`, handler, scheme registration, `/api` group `RequireAuthorization`, allowlist `AllowAnonymous`, catalog key + masking.
- [x] **T4 — Clients:** `X-Api-Key` plumbing in `Taskboard.Cli` + `Taskboard.Mcp` (option/env read → header → 401 error message).
- [x] **T5 — Pages:** `[Authorize]` on `BoardView`, `AiChat`, `ProjectsBoard`.
- [x] **T6 — Docs:** `TASKBOARD_API_KEY` in README + installation docs (en + pt-br).
- [ ] **T7 — Validation:** build, `dotnet test`; container redeploy smoke — anonymous curl matrix returns 401; browser login flow intact.
- [ ] **T8 — Done + PR:** `Status = Done` after merge.

**7.1 Validation strategy:** test-first — the anonymous-access matrix is the regression suite for this bug; every mutation endpoint needs a negative test, not just GETs.

## 8. Organization Guardrails

- **Branches:** `feature/devin-20260915-api-authorization-hardening`; never `main`/`master`/`develop`.
- **Security:** API key never logged (handler must not log header values); masked in configuration reads; constant-time comparison; no key material in tests (use a dummy value).
- **Compatibility:** deployments without `TASKBOARD_API_KEY` keep cookie-only auth; `taskctl`/MCP without the env var keep working only against unkeyed servers — documented behavior.
- **Scope:** no roles/permissions redesign; no OAuth/OIDC.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests.
- [ ] Anonymous access verified against the deployed container (curl matrix).
- [ ] `taskctl` + MCP verified against a keyed server.
- [x] `dotnet build` clean (TreatWarningsAsErrors), `dotnet test` green.
- [x] Docs updated; SPEC status advanced.

## Open Questions / Pending Ambiguity

- Resolved during gap-analysis interview: dual cookie+API-key scheme (mirrors KnowledgeHub), allowlist = login/auth/me/meta, `X-Api-Key` header name, `Taskboard:ApiKey` config key.
- `[A DEFINIR]` whether `GET /api/skills*` (skill browsing) should stay anonymous for unauthenticated UX previews — current decision: protected (consistent with the authenticated app model); revisit if a public use case appears.
