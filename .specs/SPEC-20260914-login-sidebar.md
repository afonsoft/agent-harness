# SPEC-20260914-login-sidebar

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `login-sidebar` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor Server` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/{AgentLLM}-20260914-login-sidebar` |
| Ticket | `GAP-ux-login-sidebar` |
| Status | `Draft` |

## 1. User Story

**As a** Taskboard user
**I want** the login page to not show an app sidebar full of links that redirect back to login
**So that** the unauthenticated experience is clean and nothing looks broken.

**Problem context:**
`Routes.razor` applies `MainLayout` as `DefaultLayout` to **every** page, including `Login.razor`. For unauthenticated requests the auth-gate middleware (`Program.cs` ~L995-1023) redirects any non-whitelisted path to `/login`, so clicking a sidebar link from the login page loops back to `/login` — the menu renders but does nothing. This predates and is independent of the `blazor.web.js` fix (SPEC-20260914-blazor-web-assets).

## 2. Scope

**In scope:**
- Do not render `NavMenu`/sidebar on `/login` (minimal centered shell), **or** use a dedicated layout for `Login.razor` via `@layout`.
- Keep the topbar logout/settings affordances for authenticated pages only.

**Out of scope:**
- Changes to the auth-gate middleware whitelist.
- Login form behavior (`/api/login` POST).

## 3. Technical Context

**Files to read before implementing:**
- `src/Taskboard.Blazor/Routes.razor`
- `src/Taskboard.Blazor/Layout/MainLayout.razor`
- `src/Taskboard.Blazor/Components/Pages/Login.razor`
- `src/Taskboard.Server/Program.cs` (auth-gate middleware, ~L995)

**Files to create or modify:**
```text
src/Taskboard.Blazor/Components/Pages/Login.razor        (@layout MinimalLayout or conditional)
src/Taskboard.Blazor/Layout/MinimalLayout.razor        (new, optional)
```

## 4. Requirements

### RF-001: No dead nav on /login
- **Description:** The login screen must not present navigation elements that cannot complete.
- **Rules:** either no sidebar, or sidebar links hidden when `HttpContext.User.Identity.IsAuthenticated == false`.
- **Input → Output:** `GET /login` unauthenticated → HTML without `.app-sidebar` `NavLink`s.

## 6. Acceptance Criteria

- [ ] **Given** an unauthenticated request to `/login` **when** the page renders **then** no sidebar menu links are present in the HTML.
- [ ] **Given** an authenticated session **when** visiting `/`, `/settings`, `/skills` etc. **then** the sidebar renders normally.
- [ ] **Given** the login page **when** it renders **then** it remains visually centered and Bootstrap-styled.
- [ ] `dotnet build` + `dotnet test` green.

## 7. Task Plan

- [ ] **T1 — Discovery:** confirm `MainLayout` usage and choose `@layout` override vs. conditional render.
- [ ] **T2 — Implementation:** apply chosen approach.
- [ ] **T3 — Verification:** curl `/login` unauthenticated; assert no `nav-link` markup; verify authenticated pages still render sidebar.
- [ ] **T4 — Done + PR.**

## 9. Definition of Done

- [ ] `/login` HTML contains no sidebar `NavLink`s when unauthenticated.
- [ ] Authenticated pages unchanged.
- [ ] Build/tests green.
