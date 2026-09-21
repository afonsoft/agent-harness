# SPEC-20260911: Admin Change Password

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Admin Change Password |
| Product / System | agent-harness |
| Module / Bounded Context | Security |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feature/devin-20260911-admin-change-password` |
| Technical owner | afonsoft |
| Status | `Done` (status corrigido — implementado e issues fechadas; reconciliado em 2026-09-17) |
| Date | 2026-09-11 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

The administrator password is currently defined only through the `TASKBOARD_ADMIN_PASSWORD` environment variable. After the first login there is no in-app way to change it; any change requires editing the environment file and restarting the service.

### Objective

Add a self-service change-password form in the Blazor UI and a backend endpoint so the admin can update their own password without touching the server filesystem.

### Expected outcome

- A "Change password" card on the `/settings` page.
- A `PUT /api/admin/password` endpoint that validates the current password and stores a new hash.
- Password hashes persisted in the data directory (`~/.taskboard/data/admin.json`).
- The environment variable remains the initial seed but is no longer the only source of truth.

### Out of scope

- Password recovery / reset via email.
- Multi-user or role management.
- Password strength policy beyond the UI hints (can be added later).

---

## 2. Agent Role

> Senior .NET/Blazor engineer implementing a self-contained auth improvement.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Do not log or return plaintext passwords.
- Do not commit the generated `admin.json` or any local admin hash.
- Do not introduce external dependencies without approval.

---

## 4. Product Context

### Functional context

The admin user logs in with the default credentials configured in `~/.taskboard/env`. After the first successful login, they should be able to change the password through the settings page.

### Technical context

- Cookie-based authentication already exists.
- `AdminUser` is a singleton created from configuration at startup.
- `Settings.razor` is already refactored with Tailwind tokens.

### Relevant stack

- .NET 10
- ASP.NET Core Minimal APIs
- Blazor Server
- `Microsoft.AspNetCore.Identity.PasswordHasher<T>`
- Tailwind CSS tokens from `site.css`

---

## 5. Task Definition

### Main task

Add an in-app change-password flow for the admin user.

### Subtasks

1. Refactor `AdminUser` to load/store a password hash from `admin.json`.
2. Add `PUT /api/admin/password` endpoint.
3. Add `TaskboardClient.ChangePasswordAsync`.
4. Add a Tailwind-styled change-password form to `Settings.razor`.
5. Update tests and smoke test on `https://task.afonsoft.dev`.

### Do not do

- Do not build a full user-management system.
- Do not allow changing the username in this spec.

---

## 6. Functional Requirements

### FR-001: Persistent Admin Hash

**Description:**
`AdminUser` loads the admin credentials from a file in the data directory. If the file does not exist, it falls back to the environment variables and writes the resulting hash to the file.

**File:** `~/.taskboard/data/admin.json`

```json
{
  "username": "admin",
  "passwordHash": "..."
}
```

**Behavior:**
- On startup, if `admin.json` exists, load `username` and `passwordHash`.
- If it does not exist, read `TASKBOARD_ADMIN_USERNAME` / `TASKBOARD_ADMIN_PASSWORD` (or configuration `Admin:Username` / `Admin:Password`) and write the hash to `admin.json`.
- The `admin.json` file must be readable/writable only by the owner (`0600`).

### FR-002: Change Password Endpoint

**Description:**
A JSON endpoint that requires an authenticated admin and updates the stored hash.

**Endpoint:**

```http
PUT /api/admin/password
Content-Type: application/json
```

**Request body:**

```json
{
  "currentPassword": "123qwe",
  "newPassword": "new-secure-password"
}
```

**Responses:**
- `204 NoContent` on success.
- `400 BadRequest` if `currentPassword` or `newPassword` is missing or empty.
- `401 Unauthorized` if the current password does not match.

### FR-003: Change Password UI

**Description:**
A card on `/settings` with fields for current password, new password and confirm new password.

**Fields:**
- Current password (type `password`)
- New password (type `password`)
- Confirm new password (type `password`)

**Behavior:**
- Client-side check that new password and confirm match.
- Calls `TaskboardClient.ChangePasswordAsync` on submit.
- Shows a MudBlazor `Snackbar` for success or error.
- After success, clears the form.

---

## 7. Business Rules

- Only the currently authenticated admin can change the password.
- The current password must be verified before setting the new one.
- The new password is stored as a `PasswordHasher<AdminUser>` hash, never in plaintext.
- The username cannot be changed through this feature.

---

## 8. Domain Modeling

No new domain entities. This is an infrastructure/security change.

---

## 9. Expected Architecture

```text
src/Taskboard.Server/
  Services/
    AdminUser.cs              # Load/save admin.json and verify password
  Program.cs                  # /api/admin/password endpoint

src/Taskboard.Blazor/
  Services/
    TaskboardClient.cs        # ChangePasswordAsync
  Components/Pages/
    Settings.razor            # Change password card

tests/Taskboard.Tests.Integration/
  AdminPasswordTests.cs       # Endpoint tests
```

---

## 10. API Contracts

### Change password

```http
PUT /api/admin/password
Content-Type: application/json

{
  "currentPassword": "123qwe",
  "newPassword": "new-password"
}
```

Success:

```http
HTTP/2 204 NoContent
```

Current password wrong:

```http
HTTP/2 401 Unauthorized
Content-Type: application/problem+json

{
  "type": "https://taskboard.ai/errors",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Current password is incorrect."
}
```

Validation error:

```http
HTTP/2 400 BadRequest
Content-Type: application/problem+json

{
  "type": "https://taskboard.ai/errors",
  "title": "Bad Request",
  "status": 400,
  "detail": "Current and new passwords are required."
}
```

---

## 11. Application Contracts

```csharp
public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
```

```csharp
public sealed class AdminUser
{
    public string Username { get; }
    public string PasswordHash { get; }

    public bool Validate(string? password);
    public void ChangePassword(string newPassword);
    public static AdminUser CreateFromConfiguration(IConfiguration configuration, string dataDirectory);
    public void Save(string dataDirectory);
}
```

```csharp
public class TaskboardClient
{
    public async Task ChangePasswordAsync(ChangePasswordRequest request);
}
```

---

## 12. Persistence and Data

- `~/.taskboard/data/admin.json` stores username and hash.
- File permissions: `0600` on Unix.
- Backwards compatibility: if `admin.json` does not exist, the env variable is used as seed and the file is created.

---

## 13. Integrations

None.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Current password wrong | `currentPassword` does not match hash | `401 Unauthorized` |
| New password empty | `newPassword` is empty or whitespace | `400 BadRequest` |
| Confirm mismatch | Client new/confirm mismatch | Client blocks submit, no request sent |
| Missing file on startup | No `admin.json` | Seed from env and create `admin.json` |
| Corrupt `admin.json` | Invalid JSON | Use env seed and recreate file with warning |
| Concurrent change | Two requests in parallel | Last write wins; acceptable for single admin |

---

## 15. Few-Shot Examples

```bash
# Successful change via curl
curl -s -X PUT https://task.afonsoft.dev/api/admin/password \
  -H "Content-Type: application/json" \
  -b .AspNetCore.Cookies=... \
  -d '{"currentPassword":"123qwe","newPassword":"new-password"}'
# -> 204 NoContent
```

```bash
# Wrong current password
curl -s -X PUT https://task.afonsoft.dev/api/admin/password \
  -H "Content-Type: application/json" \
  -b .AspNetCore.Cookies=... \
  -d '{"currentPassword":"wrong","newPassword":"new-password"}'
# -> 401 Unauthorized
```

---

## 16. Non-Functional Requirements

- The new password must be hashed with `PasswordHasher<AdminUser>`.
- The `admin.json` file must not be committed to the repository.
- The form must be accessible (labels, focus states) and follow the Tailwind token system.

---

## 17. Mandatory Guardrails

- Never log or return `currentPassword` or `newPassword`.
- Never write plaintext to disk.
- Always set `0600` permissions on the credentials file.
- Keep `Secure`, `HttpOnly`, `SameSite=Strict` cookie settings unchanged.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| Change with correct password | `204 NoContent` and `admin.json` hash updated |
| Change with wrong current | `401 Unauthorized` and hash unchanged |
| Empty new password | `400 BadRequest` |
| Unauthenticated request | `302` redirect to `/login` or `401` |
| Load from `admin.json` on startup | Login with new password works after restart |

---

## 19. Acceptance Criteria

- [ ] Admin user can change password from `/settings`.
- [ ] New password persists after service restart.
- [ ] The environment variable is only a seed; `admin.json` becomes the source of truth.
- [ ] Build compiles with `TreatWarningsAsErrors`.
- [ ] Integration tests pass.
- [ ] Smoke test on `https://task.afonsoft.dev` succeeds.

---

## 20. Implementation Plan

1. Refactor `AdminUser` to load/save `admin.json` and expose `ChangePassword`.
2. Add `PUT /api/admin/password` in `Program.cs` (or a separate route group).
3. Add `ChangePasswordRequest` DTO.
4. Add `TaskboardClient.ChangePasswordAsync`.
5. Add the change-password card to `Settings.razor`.
6. Write integration tests for success, failure and persistence.
7. Build, test and smoke test on the live URL.

---

## 21. Rollback Strategy

- Remove the endpoint and UI card.
- Revert `AdminUser` to env-only behavior.
- Delete `admin.json` to fall back to the environment variable.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| `admin.json` corruption makes login impossible | Alto | Baixa | Recreate from env on startup if file is invalid |
| New password not saved due to permission error | Alto | Baixa | Validate file write and surface `Snackbar` error |
| CSRF via JSON endpoint | Médio | Média | Cookie is `SameSite=Strict`; require auth on endpoint |

---

## 23. Definition of Done

- [ ] SPEC approved.
- [ ] `AdminUser` persists and loads from `admin.json`.
- [ ] `/api/admin/password` endpoint working.
- [ ] `Settings.razor` has a working change-password form.
- [ ] Tests pass and build is green.
- [ ] Smoke test on `https://task.afonsoft.dev` passes.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Should the username also be editable from settings? (Proposed: no, out of scope.)
2. Should the form show a password-strength meter? (Proposed: no, simple confirm match.)
3. Should `admin.json` support a backup file? (Proposed: no, recreate from env on error.)

## Human Approval Checklist

- [ ] File `admin.json` approach approved.
- [ ] `PUT /api/admin/password` contract approved.
- [ ] UI location (`/settings`) approved.
- [ ] Rollback and recovery approach approved.
