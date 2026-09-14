# SPEC-20260914-blazor-web-assets

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `blazor-web-assets` |
| Type | `Bugfix` |
| Stack | `.NET 10 / Blazor Server` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260914-menu-links-gap-analysis` |
| Ticket | `GAP-implementation-blazor-web-js` |
| Status | `Done` (merged via PR #59) |

## 1. User Story

**As a** Taskboard user
**I want** the sidebar menu links to navigate between pages
**So that** I can use Board, AI Chat, GitHub Board, Settings, Skills and Prompts.

**Problem context:**
`/_framework/blazor.web.js` returned **404** on every deployment. The .NET SDK only downloads the `Microsoft.AspNetCore.App.Internal.Assets` pack (which contributes `blazor.web.js` as a static web asset) when the **web host project itself** contains `.razor` Content items — see `Microsoft.NET.Sdk.Web.ProjectSystem.targets` (`RequiresAspNetWebAssets` auto-set condition). Because all components live in the `Taskboard.Blazor` RCL and `Taskboard.Server` has none, the pack was never resolved: `project.assets.json` had no `internal.assets` library and `staticwebassets.build.endpoints.json` had zero `_framework` routes. Without `blazor.web.js` there is no enhanced navigation and no `InteractiveServer` circuit; worse, every `NavLink` carries `data-bs-dismiss="offcanvas"`, and Bootstrap's document-level `click.dismiss` handler calls `event.preventDefault()` on `<a>`/`<area>` elements — killing plain-HTML navigation too. Net effect: **all sidebar links dead**.

## 2. Scope

**In scope:**
- Set `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` in `src/Taskboard.Server/Taskboard.Server.csproj`.
- Add a regression test asserting `GET /_framework/blazor.web.js` returns 200.

**Out of scope:**
- Removing `data-bs-dismiss` from `NavMenu` (required for offcanvas dismissal on `< lg` breakpoints; harmless once `blazor.web.js` loads because Blazor's click listener registers first — script order in `App.razor` — and its enhanced-nav handler `Ti` does not check `defaultPrevented`).
- Changing render-mode gating (`PageRenderMode` in `App.razor`).

## 3. Technical Context

**Where the change happens:** build-time SDK framework-reference resolution (`Taskboard.Server.csproj`) + integration test project.

**Files to read before implementing:**
- `src/Taskboard.Server/Taskboard.Server.csproj`
- `src/Taskboard.Blazor/App.razor` · `src/Taskboard.Blazor/Layout/NavMenu.razor`
- `tests/Taskboard.Tests.Integration/ServerEndpointsTests.cs`

**Files to create or modify:**
```text
src/Taskboard.Server/Taskboard.Server.csproj        (modified — done)
tests/Taskboard.Tests.Integration/ServerEndpointsTests.cs (add regression test)
```

## 4. Requirements

### RF-001: Serve framework web assets
- **Description:** The published/built server must expose `_framework/blazor.web.js` (and `blazor.server.js`) as static web assets.
- **Rules:** `staticwebassets.build.endpoints.json` must contain `_framework/blazor.web.js` routes.
- **Input → Output:** `GET /_framework/blazor.web.js` → `200` with JS body.

### RF-002: Menu navigation works without and with interactivity
- **Description:** Sidebar `NavLink` clicks must navigate in static SSR (via enhanced navigation) and after the `InteractiveServer` circuit starts.
- **Input → Output:** click on each of the 6 menu links → target page renders.

## 6. Acceptance Criteria

- [ ] **Given** a built `Taskboard.Server` **when** `GET /_framework/blazor.web.js` **then** HTTP 200 and `Content-Type: text/javascript`.
- [ ] **Given** an authenticated session **when** clicking each `NavMenu` link **then** the browser navigates to `/`, `/ai-chat`, `/github-board`, `/settings`, `/skills`, `/prompts/taskboard/manage-taskboard`.
- [ ] **Given** `dotnet test Taskboard.sln` **when** it runs **then** the regression test passes.
- [ ] `dotnet build` passes with `TreatWarningsAsErrors`.

## 7. Task Plan

- [x] **T1 — Discovery:** traced 404 → missing `_framework` endpoints → `RequiresAspNetWebAssets` unset.
- [x] **T2 — Implementation:** `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` in `Taskboard.Server.csproj`.
- [x] **T3 — Verification:** rebuilt; manifest now lists `_framework/blazor.web.js`; live `GET` returned 200 (200,645 bytes).
- [ ] **T4 — Regression test:** add `ServerEndpointsTests` case for `/_framework/blazor.web.js` → 200.
- [ ] **T5 — Done + PR.**

## 9. Definition of Done

- [ ] Fix committed on feature branch.
- [ ] Regression test green.
- [ ] `dotnet build` + `dotnet test` green.
