# SonarQube Auto-Fix — Review Notes

Generated: 2026-09-30 · Project: `afonsoft_agent-harness` (SonarCloud, public)

## Summary of changes

- Downloaded **1811** unresolved issues → `.sonar_devin_auto_fix/sonarqube_issues.json`
- ToDo board: `.sonar_devin_auto_fix/SONAR_FIX_TODO_BOARD.md`
- Generated **70 Approved SPECs** in `.specs/SPEC-20260930-sonar-*.md` (one per SonarQube rule + 1 exclusion SPEC)

| Type | Issues | SPECs |
|---|---|---|
| VULNERABILITY | 24 | 6 |
| BUG | 46 | 5 |
| CODE_SMELL | 1741 | 57 |
| Generated-docs exclusion | 1036 | 1 |

| Severity | Count |
|---|---|
| BLOCKER | 5 |
| CRITICAL | 105 |
| MAJOR | 1359 |
| MINOR | 341 |
| INFO | 1 |

Estimated effort (SonarQube debt): **~10921 min ≈ 182h**

## How to review

1. Each `SPEC-20260930-sonar-*.md` lists every issue key + file:line it fixes (section 3) and its acceptance criteria (section 6).
2. Implementation order follows the ToDo board: VULNERABILITY → BUG → CODE_SMELL (Blocker→Info).
3. Fixes are executed per-SPEC via `/execute-specs` (red-green-refactor, xUnit tests per changed line).

## Points of attention

- **`docs/architecture/architecture.html`** holds 1036 issues (57% of total) — generated artifact; resolved by exclusion SPEC, NOT by hand-editing.
- **`.github/workflows/dotnet.yml`** (`githubactions:S7637`) — protected path, requires human approval before edit.
- **`Dockerfile`** (`docker:S6471` root user) — verify runtime still works after adding non-root user.
- **`SqliteCliDatabaseReader.cs`** (`S2077`) — SQL parameterization; verify queries still return identical results.
- **`Program.cs` `S4015`** — Spectre.Console command members hide inherited members; renaming changes CLI surface — check command names stay stable.

## Tests

```bash
dotnet build   # TreatWarningsAsErrors
dotnet test    # xUnit suite
dotnet test /p:CollectCoverage=true /p:CoverageFormat=cobertura
```

## Post-review verification

- CI runs `dotnet-sonarscanner` on merge — issues resolved-by-fix should drop on the next scan.
- Remaining `docs/` findings disappear only after the exclusion in the dedicated SPEC is applied.
