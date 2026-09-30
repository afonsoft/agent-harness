# SPEC-20260930-sonar-s927-param-name-mismatch

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S927` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-param-name-mismatch` |
| Ticket | SonarQube rule `csharpsquid:S927` (5 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'CRITICAL': 5} |
| Estimated effort | ~50 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S927` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 5 unresolved `csharpsquid:S927` issue(s): "Rename parameter 'p' to 'updateParams' to match the interface declaration.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (5 issue(s), rule `csharpsquid:S927` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs`
- `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs`
- `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs`
- `src/Taskboard.Server/Services/AcpClientToolHandler.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpH26kXWrHeTEYN2-` | `src/Taskboard.Integrations/Agents/AcpV1Dialect.cs` | 75 | CRITICAL | Rename parameter 'p' to 'updateParams' to match the interface declaration. |
| `AaDbpH3NkXWrHeTEYN3J` | `src/Taskboard.Integrations/Agents/AcpV2Dialect.cs` | 97 | CRITICAL | Rename parameter 'p' to 'updateParams' to match the interface declaration. |
| `AaDbpHiYkXWrHeTEYNub` | `src/Taskboard.Server/Services/AcpClientToolHandler.cs` | 64 | CRITICAL | Rename parameter 'p' to 'params' to match the interface declaration. |
| `AaDbpHzmkXWrHeTEYN2F` | `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` | 86 | CRITICAL | Rename parameter 'sourceName' to 'source' to match the interface declaration. |
| `AaDbpHzmkXWrHeTEYN2E` | `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` | 61 | CRITICAL | Rename parameter 'sourceName' to 'source' to match the interface declaration. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S927` at all listed locations
- **Description:** Rename the parameter to match the base/interface declaration.
- **Rules:** Align names; update named-argument call sites if any.
- **Input → Output:** code flagged by `csharpsquid:S927` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S927` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-param-name-mismatch`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-param-name-mismatch`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S927` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S927` occurrences resolved (5/5).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
