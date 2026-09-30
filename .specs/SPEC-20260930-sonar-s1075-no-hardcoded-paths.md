# SPEC-20260930-sonar-s1075-no-hardcoded-paths

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S1075` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-no-hardcoded-paths` |
| Ticket | SonarQube rule `csharpsquid:S1075` (8 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 8} |
| Estimated effort | ~160 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S1075` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 8 unresolved `csharpsquid:S1075` issue(s): "Refactor your code not to use hardcoded absolute paths or URIs.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (8 issue(s), rule `csharpsquid:S1075` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs`
- `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs`
- `src/Taskboard.Mcp/Program.cs`
- `src/Taskboard.Server/Program.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDukLvmkK9GZAB4jb_f` | `src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs` | 52 | MINOR | Refactor your code not to use hardcoded absolute paths or URIs. |
| `AaDbpHjukXWrHeTEYNvb` | `src/Taskboard.Server/Program.cs` | 776 | MINOR | Refactor your code not to use hardcoded absolute paths or URIs. |
| `AaDbpHgpkXWrHeTEYNuL` | `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` | 93 | MINOR | Refactor your code not to use hardcoded absolute paths or URIs. |
| `AaDbpHgpkXWrHeTEYNuM` | `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` | 101 | MINOR | Refactor your code not to use hardcoded absolute paths or URIs. |
| `AaDbpHgpkXWrHeTEYNuN` | `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` | 109 | MINOR | Refactor your code not to use hardcoded absolute paths or URIs. |
| `AaDbpHgpkXWrHeTEYNuO` | `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` | 117 | MINOR | Refactor your code not to use hardcoded absolute paths or URIs. |
| `AaDbpHgpkXWrHeTEYNuP` | `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` | 165 | MINOR | Refactor your code not to use hardcoded absolute paths or URIs. |
| `AaDbpH6KkXWrHeTEYN4H` | `src/Taskboard.Mcp/Program.cs` | 22 | MINOR | Refactor your code not to use hardcoded absolute paths or URIs. |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S1075` at all listed locations
- **Description:** Stop using hardcoded absolute paths/URIs.
- **Rules:** Move to configuration/options or compute relative to a known base.
- **Input → Output:** code flagged by `csharpsquid:S1075` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S1075` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-no-hardcoded-paths`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-no-hardcoded-paths`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S1075` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S1075` occurrences resolved (8/8).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
