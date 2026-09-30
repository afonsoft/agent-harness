# SPEC-20260930-sonar-s7688-shell-double-bracket

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | SonarQube fix: `shelldre:S7688` |
| Type | Refactor |
| Stack | Infra |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-shell-double-bracket` |
| Ticket | SonarQube rule `shelldre:S7688` (34 issue(s)) |
| Status | Approved |
| Sonar type | CODE_SMELL |
| Severities | {'MAJOR': 34} |
| Estimated effort | ~68 min |

## 1. User Story

**As a** maintainer of the Harness codebase
**I want** all `shelldre:S7688` findings resolved
**So that** the SonarQube quality gate improves and the flagged defects/risks are removed.

**Problem context:**
SonarCloud project `afonsoft_agent-harness` reports 34 unresolved `shelldre:S7688` issue(s): "Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more feature-rich.". Source: `.sonar_devin_auto_fix/sonarqube_issues.json`.

## 2. Scope

**In scope:**
- Fix every occurrence listed in section 3 (34 issue(s), rule `shelldre:S7688` only).

**Out of scope:**
- Any other SonarQube rule, refactor, or improvement.
- `docs/architecture/architecture.html` occurrences (handled by SPEC-20260930-sonar-generated-docs-exclusion).


## 3. Technical Context

**Files to read before implementing:**
- `CLAUDE.md` (harness rules), `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- `install.sh`

**Issue locations:**

| Issue key | File | Line | Severity | Message |
| --- | --- | --- | --- | --- |
| `AaDbpH-TkXWrHeTEYOIm` | `install.sh` | 12 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI-` | `install.sh` | 406 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI_` | `install.sh` | 410 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOJA` | `install.sh` | 425 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOJB` | `install.sh` | 425 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOJC` | `install.sh` | 428 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOJD` | `install.sh` | 435 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOJE` | `install.sh` | 444 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOJF` | `install.sh` | 450 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOJG` | `install.sh` | 463 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI8` | `install.sh` | 340 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIn` | `install.sh` | 49 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIo` | `install.sh` | 53 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIp` | `install.sh` | 70 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIq` | `install.sh` | 80 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIr` | `install.sh` | 89 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIs` | `install.sh` | 109 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIt` | `install.sh` | 121 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIu` | `install.sh` | 127 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIv` | `install.sh` | 133 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIw` | `install.sh` | 160 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIx` | `install.sh` | 173 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIy` | `install.sh` | 173 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOIz` | `install.sh` | 180 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI0` | `install.sh` | 190 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI1` | `install.sh` | 196 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI2` | `install.sh` | 200 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI3` | `install.sh` | 204 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI4` | `install.sh` | 209 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI5` | `install.sh` | 214 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI6` | `install.sh` | 235 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI7` | `install.sh` | 239 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOI9` | `install.sh` | 349 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |
| `AaDbpH-TkXWrHeTEYOJH` | `install.sh` | 478 | MAJOR | Use '[[' instead of '[' for conditional tests. The '[[' construct is safer and more featur |

## 4. Requirements

### RF-001: Resolve `shelldre:S7688` at all listed locations
- **Description:** Replace [ with [[ in the flagged shell conditional tests.
- **Rules:** Use bash [[ ]] constructs.
- **Input → Output:** code flagged by `shelldre:S7688` → code that no longer triggers the rule, behavior preserved.

**Business rules / invariants:**
- No functional behavior change beyond what the fix requires.
- No coverage exclusions (no `NOSONAR`, no `ExcludeFromCodeCoverage`).

## 5. API Contract (if applicable)

N/A — internal code quality fix; no public contract change expected.

## 6. Acceptance Criteria

- [ ] **Given** the flagged code **when** the fix is applied **then** the rule `shelldre:S7688` no longer triggers on any listed location.
- [ ] **Given** the existing test suite **when** `dotnet build` + `dotnet test` run **then** everything stays green.
- [ ] **Given** changed lines **when** coverage is measured **then** modified lines are covered by tests.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read each file+line in section 3 and understand local patterns.
- [ ] **T2 — Tests:** add/adjust tests covering each changed line (xUnit; `Dado_Quando_Entao` naming).
- [ ] **T3 — Implementation:** apply the per-rule fix at each location.
- [ ] **T4 — Validation:** `dotnet build` (warnings as errors) + `dotnet test`; run `dotnet format` if touched.
- [ ] **T5 — Done + PR:** set Status = Done, open PR to `develop` on `feature/devin-20260930-sonar-shell-double-bracket`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use `feature/devin-20260930-sonar-shell-double-bracket`.
- **Workflows:** `.github/workflows/**` is protected — do not modify without human approval.
- **Security:** never log/commit secrets; no coverage exclusions.
- **Scope:** fix only `shelldre:S7688` occurrences listed above.

## 9. Definition of Done

- [ ] All `shelldre:S7688` occurrences resolved (34/34).
- [ ] Tests pass; modified lines covered.
- [ ] `dotnet build` clean (TreatWarningsAsErrors).
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None.
