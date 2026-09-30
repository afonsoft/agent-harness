# SPEC-20260930-sonar-generated-docs-exclusion

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | Exclude generated architecture docs from SonarQube analysis |
| Type | Infra |
| Stack | Docs/Infra |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260930-sonar-docs-exclusion` |
| Ticket | 1036 SonarQube issues on generated artifact |
| Status | Approved |

## 1. User Story

**As a** maintainer
**I want** `docs/architecture/architecture.html` (a generated archify artifact) excluded from SonarQube analysis
**So that** 1036 findings on machine-generated HTML/JS/CSS stop polluting the quality gate.

**Problem context:**
The file is generated output — hand-editing its inline JS/CSS is futile because regeneration reintroduces the patterns. 1036 of 1811 unresolved issues (57%) live in this single artifact.

## 2. Scope

**In scope:**
- Exclude `docs/**` (or at minimum `docs/architecture/**`) from SonarQube analysis.
- Preferred path (no repo change): set `sonar.exclusions`/`sonar.cpd.exclusions` at the SonarCloud project level via UI/API (requires admin token).
- Fallback path: add `docs/**` to `sonar.exclusions` in `.github/workflows/code-quality.yml` — **REQUIRES HUMAN APPROVAL** (protected path).

**Out of scope:** manually fixing the generated file; any non-docs exclusions.

## 3. Technical Context

- `docs/architecture/architecture.html` — generated interactive diagram (archify skill).
- `.github/workflows/code-quality.yml` lines ~97-106 — current `sonar.exclusions` flags.

**Rules covered by this SPEC (all locations in the artifact):**

| Rule | Issues |
| --- | --- |
| `javascript:S7761` | 848 |
| `javascript:S3358` | 48 |
| `Web:S6819` | 19 |
| `javascript:S3776` | 18 |
| `javascript:S2486` | 15 |
| `javascript:S7765` | 15 |
| `javascript:S6653` | 12 |
| `javascript:S8786` | 7 |
| `javascript:S4138` | 7 |
| `javascript:S7768` | 7 |
| `javascript:S4144` | 6 |
| `javascript:S6666` | 6 |
| `css:S4666` | 3 |
| `Web:S7927` | 3 |
| `javascript:S3735` | 3 |
| `javascript:S7778` | 3 |
| `javascript:S7773` | 2 |
| `javascript:S2004` | 2 |
| `javascript:S6353` | 1 |
| `javascript:S6661` | 1 |
| `Web:PageWithoutTitleCheck` | 1 |
| `Web:S5254` | 1 |
| `Web:S6827` | 1 |
| `Web:S6845` | 1 |
| `javascript:S6535` | 1 |
| `javascript:S7762` | 1 |
| `javascript:S7721` | 1 |
| `javascript:S9381` | 1 |
| `javascript:S1854` | 1 |
| `javascript:S7750` | 1 |

## 4. Requirements

### RF-001: Exclude generated docs from analysis
- **Description:** SonarQube must no longer report issues for `docs/architecture/**`.
- **Rules:** prefer server-side exclusion; workflow edit only with human approval.

## 6. Acceptance Criteria

- [ ] **Given** a new SonarCloud scan **when** it completes **then** zero issues are reported under `docs/`.
- [ ] **Given** the exclusion config **when** reviewed **then** only generated docs paths are excluded — no `src/` exclusions added.

## 7. Task Plan

- [ ] **T1:** Attempt SonarCloud project-level exclusion `docs/**` (needs admin token — ask user).
- [ ] **T2:** If not possible, prepare a PR adding `docs/**` to `sonar.exclusions` in code-quality.yml and request human approval.
- [ ] **T3:** Verify on next scan; mark 1036 issues as resolved-by-exclusion in the ToDo board.

## 8. Organization Guardrails

- Workflows protected — human approval required for the fallback path.
- Do NOT hand-edit `architecture.html` to "fix" findings.

## 9. Definition of Done

- [ ] Zero SonarQube issues under `docs/` on the next scan.

## Open Questions

- Which exclusion path does the user prefer (server-side vs workflow PR)?
