# SPEC-20260930-sonar-s7637-pin-gha-commit-sha

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `githubactions:S7637` |
| Type | Security |
| Stack | Infra |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-pin-gha-commit-sha` |
| Ticket | SonarQube rule `githubactions:S7637` (1 issue(s)) |
| Status | Done — entregue via [PR #415](https://github.com/afonsoft/agent-harness/pull/415) (merged 2026-10-01) |
| Sonar type | VULNERABILITY |
| Severities | {'MAJOR': 1} |
| Estimated effort | ~30 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `githubactions:S7637` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 1 unresolved `githubactions:S7637` issue(s): "Use full commit SHA hash for this dependency.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (1 issue(s), rule `githubactions:S7637` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).
- `.github/workflows/**` changes require explicit human approval before implementation.

## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `.github/workflows/dotnet.yml`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpH7ZkXWrHeTEYN4a` | `.github/workflows/dotnet.yml` | 118 | MAJOR | Use full commit SHA hash for this dependency. |

## 4. Requirements

### RF-001: Resolve `githubactions:S7637` at all listed locations
- **Description:** Pin the flagged GitHub Actions dependency to a full commit SHA.
- **Rules:** REQUIRES HUMAN APPROVAL: file lives in .github/workflows/ which is protected. Propose the SHA pin in a dedicated PR for human review.
- **Input → Output:** code flagged by `githubactions:S7637` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `githubactions:S7637` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-pin-gha-commit-sha`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-pin-gha-commit-sha`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `githubactions:S7637` occurrences listed above.

## 9. Definition of Done

- [ ] All `githubactions:S7637` occurrences resolved (1/1).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- `.github/workflows/dotnet.yml` is a protected path — requires human approval before edit.
