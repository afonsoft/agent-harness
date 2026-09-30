# SPEC-20260930-sonar-s2365-property-copies-collection

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S2365` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-property-copies-collection` |
| Ticket | SonarQube rule `csharpsquid:S2365` (5 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'CRITICAL': 5} |
| Estimated effort | ~25 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S2365` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 5 unresolved `csharpsquid:S2365` issue(s): "Refactor 'ConfigAgentModels' into a method, properties should not copy collections.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (5 issue(s), rule `csharpsquid:S2365` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs`
- `src/Taskboard.Domain.Shared/Agents/CliDatabaseMap.cs`
- `src/Taskboard.Integrations/CliDb/Extractors/AntigravityCliDbExtractor.cs`
- `src/Taskboard.Server/Services/PromptQueue.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDpllQ1TboaTa85QyoA` | `src/Taskboard.Blazor/Components/Pages/AiChat.razor` | 628 | CRITICAL | Refactor 'ConfigAgentModels' into a method, properties should not copy collections. |
| `AaDbpHiOkXWrHeTEYNuY` | `src/Taskboard.Server/Services/PromptQueue.cs` | 18 | CRITICAL | Refactor 'Pending' into a method, properties should not copy collections. |
| `AaDbpHgfkXWrHeTEYNuK` | `src/Taskboard.Domain.Shared/Agents/CliDatabaseMap.cs` | 96 | CRITICAL | Refactor 'All' into a method, properties should not copy collections. |
| `AaDbpHw6kXWrHeTEYN1I` | `src/Taskboard.Integrations/CliDb/Extractors/AntigravityCliDbExtractor.cs` | 43 | CRITICAL | Refactor 'Sources' into a method, properties should not copy collections. |
| `AaDbpHgpkXWrHeTEYNuQ` | `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` | 182 | CRITICAL | Refactor 'All' into a method, properties should not copy collections. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S2365` at all listed locations
- **Description:** Refactor the flagged collection-copying property into a method.
- **Rules:** Properties should be cheap; expose via method or precomputed field.
- **Input → Output:** code flagged by `csharpsquid:S2365` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S2365` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-property-copies-collection`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-property-copies-collection`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S2365` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S2365` occurrences resolved (5/5).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
