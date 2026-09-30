# SPEC-20260930-sonar-s101-pascal-case-naming

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `csharpsquid:S101` |
| Type | Refactor |
| Stack | .NET |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-pascal-case-naming` |
| Ticket | SonarQube rule `csharpsquid:S101` (7 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MINOR': 7} |
| Estimated effort | ~35 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `csharpsquid:S101` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 7 unresolved `csharpsquid:S101` issue(s): "Rename interface 'ILLMProvider' to match pascal case naming rules, consider using 'ILlmProvider'.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (7 issue(s), rule `csharpsquid:S101` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs`
- `src/Taskboard.Application/AiChat/MockLLMProvider.cs`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpHqZkXWrHeTEYNyw` | `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` | 3 | MINOR | Rename interface 'ILLMProvider' to match pascal case naming rules, consider using 'ILlmPro |
| `AaDbpHqZkXWrHeTEYNyx` | `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` | 18 | MINOR | Rename record 'LLMMessage' to match pascal case naming rules, consider using 'LlmMessage'. |
| `AaDbpHqZkXWrHeTEYNyz` | `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` | 23 | MINOR | Rename record 'LLMOptions' to match pascal case naming rules, consider using 'LlmOptions'. |
| `AaDbpHqZkXWrHeTEYNyy` | `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` | 29 | MINOR | Rename record 'LLMResponse' to match pascal case naming rules, consider using 'LlmResponse |
| `AaDbpHqZkXWrHeTEYNy0` | `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` | 34 | MINOR | Rename record 'LLMUsage' to match pascal case naming rules, consider using 'LlmUsage'. |
| `AaDbpHqZkXWrHeTEYNy1` | `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` | 39 | MINOR | Rename record 'LLMStreamChunk' to match pascal case naming rules, consider using 'LlmStrea |
| `AaDbpHtwkXWrHeTEYNz1` | `src/Taskboard.Application/AiChat/MockLLMProvider.cs` | 5 | MINOR | Rename class 'MockLLMProvider' to match pascal case naming rules, consider using 'MockLlmP |

## 4. Requirements

### RF-001: Resolve `csharpsquid:S101` at all listed locations
- **Description:** Rename the flagged type/member to satisfy PascalCase naming.
- **Rules:** e.g. ILLMProvider -> ILlmProvider; update all references.
- **Input → Output:** code flagged by `csharpsquid:S101` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `csharpsquid:S101` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-pascal-case-naming`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-pascal-case-naming`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `csharpsquid:S101` occurrences listed above.

## 9. Definition of Done

- [ ] All `csharpsquid:S101` occurrences resolved (7/7).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
