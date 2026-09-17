# SPEC-20260911: API Docs & Skill Prompt Page

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | API Docs & Skill Prompt Page |
| Product / System | taskboard-ai |
| Module / Bounded Context | Frontend / API Docs |
| Change type | Feature |
| Repository | afonsoft/taskboard-ai |
| Suggested branch | `feature/devin-20260911-api-docs-and-prompts` |
| Technical owner | afonsoft |
| Status | `Done` (status corrigido — implementado e issues fechadas; reconciliado em 2026-09-17) |
| Date | 2026-09-11 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

The application currently has no interactive API documentation and no user-facing page that explains how to prompt or use the `manage-taskboard` skill. Contributors and admins have to read source files or `SKILL.md` directly.

### Objective

Add two surfaces:
1. A Swagger UI page (`/swagger`) that exposes all minimal API endpoints and schemas.
2. A prompt page (`/prompts/{source}/{name}`) that renders a skill's `SKILL.md` content, highlights its `References` and `Scripts` sections, and lets the user copy the full prompt.

### Capability map

| Module | Responsibility | Depends on |
|---|---|---|
| `swagger` | API documentation with Swagger UI | — |
| `skill-prompts` | Render skill `SKILL.md` with copy action | `SkillDetailDto` (or a new skill read endpoint) |

Build order: `swagger` → `skill-prompts`

### Expected outcome

- `/swagger` loads the Swagger UI with all API endpoints grouped and documented.
- `/prompts/taskboard/manage-taskboard` renders the `manage-taskboard` skill content with a clean, readable layout and a copy-to-clipboard button.
- Both pages follow the existing Tailwind token system and are accessible.

### Out of scope

- OpenAPI JSON generation for external clients (the endpoint is generated, but not advertised as a product).
- Markdown-to-HTML rendering engine (keep plain text / simple section extraction).
- Editing skills from the UI.

---

## 2. Agent Role

> Senior .NET/Blazor engineer with Swagger/OpenAPI and docs UI experience.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Do not add new NuGet packages; use `Swashbuckle.AspNetCore` which is already referenced.
- Do not expose secrets, tokens, or admin credentials in Swagger or prompt pages.
- Do not modify protected CI/GitHub workflows.

---

## 4. Product Context

### Functional context

- Admins and contributors need to discover API endpoints without reading source.
- Users need a quick way to see the `manage-taskboard` skill instructions and copy the prompt for use in an agent.

### Technical context

- .NET 10 minimal APIs, `Program.cs`.
- `Swashbuckle.AspNetCore` already in `Directory.Packages.props`.
- `Taskboard.Integrations.Skills.SkillDiscoveryService` already discovers `SKILL.md` files and returns `SkillDto`.
- `SkillDetailDto` from `SPEC-20260911-skills-ux-redesign` can be reused.
- Tailwind CSS tokens and `site.css` are available.

### Relevant stack

- .NET 10
- Swashbuckle.AspNetCore 7.2.0 (per `Directory.Packages.props`)
- ASP.NET Core Minimal APIs with endpoint routing
- Blazor Server
- Tailwind CSS

---

## 5. Task Definition

### Main task

Add an interactive API docs page and a skill prompt page.

### Subtasks

1. Configure Swashbuckle in `Program.cs` and expose `/swagger`.
2. Ensure `/swagger` and `/swagger/v1/swagger.json` are reachable (auth whitelist if public).
3. Add `GET /api/skills/{source}/{name}` detail endpoint if not already implemented by `SPEC-20260911-skills-ux-redesign`.
4. Create `Prompts.razor` page with route `/prompts/{source}/{name}`.
5. Create `SkillPromptCard.razor` component (title, source badge, description, tools, references, scripts, content, copy button).
6. Add `NavMenu` link for `Prompts` / `manage-taskboard`.
7. Build and smoke test on `https://task.afonsoft.dev/swagger` and `https://task.afonsoft.dev/prompts/taskboard/manage-taskboard`.

### Do not do

- Do not add markdown rendering libraries.
- Do not build a public API portal without auth.
- Do not edit skill files from the page.

---

## 6. Functional Requirements

### FR-001: Swagger UI Configuration

**Description:**
The server exposes an interactive Swagger UI at `/swagger`.

**Configuration:**

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Taskboard AI API",
        Version = "v1",
        Description = "API for taskboard-ai projects, tasks, agents and skills."
    });
});
```

```csharp
app.MapSwagger();
app.MapSwaggerUI("swagger", options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Taskboard AI v1");
    options.DocumentTitle = "Taskboard AI - API";
    options.EnableTryItOutByDefault = false;
});
```

**Auth behavior:**
- If the user is not authenticated, the Swagger UI itself may still load (whitelist `/swagger` and `/swagger/v1/swagger.json`).
- API calls from `Try It Out` will 302/401 unless the user is logged in, because the endpoints keep their auth requirements.
- Alternatively, the Swagger UI can be behind auth by not whitelisting it; in that case it redirects to `/login`. The SPEC defaults to **public UI, protected endpoints**.

### FR-002: OpenAPI Endpoint Grouping

**Description:**
Minimal API endpoints are grouped by tag in Swagger.

**Behavior:**
- Endpoints under `/api/projects` are tagged `Projects`.
- Endpoints under `/api/login`, `/api/logout` are tagged `Auth`.
- Endpoints under `/api/admin/*` are tagged `Admin`.
- Endpoints under `/api/skills` are tagged `Skills`.

This can be achieved with `WithTags(...)` or `WithGroupName(...)` on each `Map*` call. Where the existing code does not set tags, add them only for the major route groups listed above.

### FR-003: Skill Prompt Page

**Description:**
A dedicated page renders the `SKILL.md` content of a given skill.

**Route:**

```
/prompts/{source}/{name}
```

Examples:
- `/prompts/taskboard/manage-taskboard`
- `/prompts/claude/manage-taskboard` (if the same skill exists in another source)

**Data:**
- Calls `GET /api/skills/{source}/{name}`.
- Returns `SkillDetailDto`:

```csharp
public sealed record SkillDetailDto(
    string Name,
    string Description,
    string Source,
    string Path,
    IReadOnlyList<string> Tools,
    string? References,
    string? Scripts,
    string Content);
```

**Page sections:**
- Page title: `Prompt: {Name}`.
- Subtitle: `Source: {Source} · {Description}`.
- `Tools` list as small badges.
- `References` block, shown only if not null/empty.
- `Scripts` block, shown only if not null/empty.
- `Full content` block in a scrollable `<pre>` or formatted panel.
- `Copy prompt` button at the top right of the content block; copies the entire `Content` to the clipboard and shows a `Snackbar` confirmation.

**Empty / not found:**
- If the skill is not found, show `EmptyState` with "Skill not found" and a link to `/skills`.

### FR-004: Prompt Page Navigation

**Description:**
The prompt page is reachable from the main navigation.

**Behavior:**
- Add a `Prompts` or `manage-taskboard` link to `NavMenu.razor`.
- The link points to `/prompts/taskboard/manage-taskboard`.
- On mobile, the link is inside the hamburger/collapsed sidebar.

### FR-005: Responsive & Accessible

**Description:**
Both Swagger and the prompt page are accessible.

**Behavior:**
- Swagger UI is self-contained; ensure it is not blocked by CSP or auth.
- Prompt page is mobile-first: single column, content block wraps naturally, copy button is a clear touch target.
- Prompt page has visible focus and reduced-motion support.

---

## 7. Business Rules

- Swagger UI must not display API keys or admin tokens in examples.
- The prompt page only reads `SKILL.md` files under paths already discovered by `SkillDiscoveryService`.
- The `Copy prompt` button must not trigger a page reload.

---

## 8. Domain Modeling

No new domain entities. Swagger uses endpoint metadata; prompt page uses existing skill data.

---

## 9. Expected Architecture

```text
src/Taskboard.Server/
  Program.cs              # Swagger registration, endpoint tags, /api/skills/{source}/{name} endpoint

src/Taskboard.Blazor/
  Components/Pages/
    Prompts.razor         # route /prompts/{source}/{name}
  Components/Shared/
    SkillPromptCard.razor # renders SkillDetailDto sections and copy button
  Layout/
    NavMenu.razor         # add prompt link

src/Taskboard.Application.Contracts/Skills/
  SkillDetailDto.cs       # if not already added by the Skills UX spec

src/Taskboard.Integrations/Skills/
  SkillDiscoveryService.cs # if not already has GetDetailAsync
```

---

## 10. API Contracts

### Skill detail

```http
GET /api/skills/{source}/{name}
```

**Response `200 OK`:**

```json
{
  "name": "manage-taskboard",
  "description": "Gerencie o Dashi Taskboard...",
  "source": "taskboard",
  "path": "/home/ubuntu/repos/taskboard-ai/skills/manage-taskboard",
  "tools": ["Bash", "Read", "Edit", "Write"],
  "references": "## Contexto\n\n...",
  "scripts": null,
  "content": "## Contexto\n\n..."
}
```

**Response `404 Not Found`:**

```json
{
  "type": "https://taskboard.ai/errors",
  "title": "Not Found",
  "status": 404,
  "detail": "Skill not found."
}
```

---

## 11. Application Contracts

```csharp
public sealed record SkillDetailDto(
    string Name,
    string Description,
    string Source,
    string Path,
    IReadOnlyList<string> Tools,
    string? References,
    string? Scripts,
    string Content);
```

---

## 12. Persistence and Data

No persistence changes. Swagger is generated from endpoint metadata. Prompt page reads `SKILL.md` from the file system path returned by discovery.

---

## 13. Integrations

- Reuses `SkillDiscoveryService` and `FrontmatterReader` from `Taskboard.Integrations.Skills`.
- Reuses `ISnackbar` from MudBlazor for copy confirmation.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Swagger unauthenticated | user not logged in and not whitelisted | Redirect to `/login` (if protected) or UI loads with 401 on Try It Out. |
| `Try It Out` without cookie | auth-required endpoint | 401 or 302, as before. |
| Skill not found | `GET /api/skills/unknown/unknown` | `404 Not Found`. |
| Missing `References` or `Scripts` | `SKILL.md` does not have those headings | Section is hidden on the prompt page. |
| Skill file deleted after discovery | `Path` no longer exists | `404 Not Found` with a generic error. |
| Copy failure | clipboard API blocked | `Snackbar` error message. |

---

## 15. Few-Shot Examples

```csharp
// Swagger registration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Taskboard AI API",
        Version = "v1"
    });
});

app.MapSwagger();
app.MapSwaggerUI("swagger", options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Taskboard AI v1");
});
```

```razor
<!-- Prompt page concept -->
@page "/prompts/{source}/{name}"

<PageTitle>Prompt - @Skill?.Name</PageTitle>

@if (Skill is null)
{
    <EmptyState Message="Skill not found" />
}
else
{
    <div class="card">
        <div class="flex items-start justify-between">
            <div>
                <h1 class="text-2xl font-bold text-text">@Skill.Name</h1>
                <p class="text-sm text-text-muted">@Skill.Description</p>
            </div>
            <button type="button" class="btn-secondary" @onclick="CopyToClipboardAsync">Copy prompt</button>
        </div>
        <div class="mt-4 flex flex-wrap gap-2">
            @foreach (var tool in Skill.Tools)
            {
                <span class="badge">@tool</span>
            }
        </div>
        @if (!string.IsNullOrWhiteSpace(Skill.References))
        {
            <div class="mt-6">
                <h2 class="text-lg font-semibold text-text">References</h2>
                <pre class="code-block">@Skill.References</pre>
            </div>
        }
        @if (!string.IsNullOrWhiteSpace(Skill.Scripts))
        {
            <div class="mt-6">
                <h2 class="text-lg font-semibold text-text">Scripts</h2>
                <pre class="code-block">@Skill.Scripts</pre>
            </div>
        }
        <div class="mt-6">
            <h2 class="text-lg font-semibold text-text">Full content</h2>
            <pre class="code-block">@Skill.Content</pre>
        </div>
    </div>
}
```

---

## 16. Non-Functional Requirements

- Swagger page must load in < 2s on the live server.
- Prompt page must not fetch the same skill file more than once per render.
- Build compiles with `TreatWarningsAsErrors`.
- Both pages respect the active light/dark theme (where applicable; Swagger UI has its own theme).

---

## 17. Mandatory Guardrails

- Do not expose the admin token or GitHub token in Swagger example requests.
- Do not allow the prompt page to read arbitrary files outside the discovered skill directories.
- Do not commit `wwwroot/css/tailwind.css` if generated.
- Do not push to `main`/`develop`.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| Swagger loads | `https://task.afonsoft.dev/swagger` returns `200`. |
| Swagger JSON | `https://task.afonsoft.dev/swagger/v1/swagger.json` returns valid OpenAPI. |
| Skill detail | `GET /api/skills/taskboard/manage-taskboard` returns `200` with `Content`. |
| Prompt page | `/prompts/taskboard/manage-taskboard` renders the skill content. |
| Copy prompt | Button copies `Content` and shows `Snackbar`. |
| Not found | `GET /api/skills/unknown/unknown` returns `404`. |
| Build | `dotnet build` succeeds. |

---

## 19. Acceptance Criteria

- [ ] Swagger UI is available at `/swagger`.
- [ ] OpenAPI JSON is available at `/swagger/v1/swagger.json`.
- [ ] Major endpoint groups are tagged (`Projects`, `Auth`, `Admin`, `Skills`).
- [ ] `GET /api/skills/{source}/{name}` endpoint works (or is already implemented and reused).
- [ ] `/prompts/{source}/{name}` renders the skill content, `Tools`, `References`, and `Scripts`.
- [ ] `Copy prompt` button copies the full content and confirms with a `Snackbar`.
- [ ] `NavMenu` has a link to the `manage-taskboard` prompt.
- [ ] Build compiles with `TreatWarningsAsErrors`.
- [ ] Smoke tests on `https://task.afonsoft.dev/swagger` and `/prompts/taskboard/manage-taskboard` pass.

---

## 20. Implementation Plan

1. Add Swashbuckle registration to `Program.cs` (`AddEndpointsApiExplorer`, `AddSwaggerGen`, `MapSwagger`, `MapSwaggerUI`).
2. Add `/swagger` and `/swagger/v1/swagger.json` to the auth whitelist or require auth (default to public UI).
3. Add endpoint tags for major route groups.
4. Implement `GET /api/skills/{source}/{name}` if not already available from `SPEC-20260911-skills-ux-redesign`.
5. Add `TaskboardClient.GetSkillDetailAsync` method.
6. Create `Prompts.razor` and `SkillPromptCard.razor`.
7. Add `NavMenu` link for the prompt page.
8. Add `.code-block` CSS class to `site.css` if not present.
9. Build and run unit/integration tests.
10. Smoke test on `https://task.afonsoft.dev/swagger` and `https://task.afonsoft.dev/prompts/taskboard/manage-taskboard`.

---

## 21. Rollback Strategy

- Remove Swagger registration from `Program.cs`.
- Remove `Prompts.razor`, `SkillPromptCard.razor`.
- Revert `NavMenu.razor`.
- Keep `GET /api/skills/{source}/{name}` if it is already used elsewhere.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Swagger exposes too much API surface | Médio | Média | Keep auth on all state-changing endpoints; consider requiring auth for the UI if public is a concern. |
| Skill file path traversal | Alto | Baixa | Validate that the requested `source`+`name` matches a discovered skill. |
| Copy-to-clipboard not supported | Baixo | Baixa | Fallback to selecting text and a manual copy hint. |

---

## 23. Definition of Done

- [ ] SPEC approved.
- [ ] Swagger UI and OpenAPI JSON are available.
- [ ] Major endpoint groups are tagged.
- [ ] Skill prompt page is available and renders `manage-taskboard`.
- [ ] `Copy prompt` is functional.
- [ ] `NavMenu` links to the prompt page.
- [ ] Build and tests pass.
- [ ] Smoke tests on live URLs pass.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Should Swagger UI be public or require admin auth? (Proposed: public UI, protected endpoints.)
2. Should the prompt page be generic (`/prompts/{source}/{name}`) or only for `manage-taskboard`? (Proposed: generic route with a default `NavMenu` link to `manage-taskboard`.)
3. Should the `Copy prompt` button copy the full `Content` or a curated prompt? (Proposed: full `Content` from `SKILL.md`.)

## Human Approval Checklist

- [ ] Swagger route and auth approach approved.
- [ ] Prompt page route and content sections approved.
- [ ] Copy prompt behavior approved.
