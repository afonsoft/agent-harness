# Gap Analysis — 2026-09-22 (specs não implementadas)

- Repository: `/home/ubuntu/repos/agent-harness` | Branch: `main` | Commit: `cf1032b` (post-merge PR #317 finops spec)
- Phase reached: `done` (gate aprovado → SPEC Approved → Issues criadas)
- Mode: `spec-focused` (pedido: "analise todas as specs que não foram implementadas")

## 1. Inventário de status (parse dual-format: `| Status | X` + `- **Status**: X`)

- ~100 SPECs; terminais: Done/Implemented/Completed/Deprecated/Superseded.
- Não-terminais: 4.

## 2. Candidatos e Veredictos

| Key | Veredito | Evidência |
|---|---|---|
| `SPEC-20260922-flaky-integration-tests` (Approved) | **DUPLICADO** | PR #311 MERGED 13:53Z — `PostMcpRemove`/`SyncManual` corrigidos |
| `SPEC-20260922-coverage-ratchet-bump-77` (Approved) | **DUPLICADO** | PR #312 MERGED; `dotnet.yml:44` `COVERAGE_THRESHOLD: 77` |
| `SPEC-20260922-harness-home-rename` (Approved) | **DUPLICADO** | PR #313 MERGED; `HARNESS_*` binding `Program.cs:91` |
| `SPEC-20260922-finops-dashboard-detail` (Approved) | **não-gap** | Criada hoje; Issue #316 OPEN; aguardando execução — não flaggar |
| `GAP-automation-spec-status-enforcement` | **CONFIRMADO** | Convenção `global-rules.md:28` falhou 3ª vez (09-14, 09-19, 09-22); sem enforcement mecânico |

## 3. Draft SPEC gerada

- `.specs/SPEC-20260922-spec-status-enforcement.md` — detector `check-spec-status.sh` + `--fix` + bump dos 3 stale + wiring na convenção. Sem tocar `.github/workflows/**`.

## 4. Resultado

- Gate: **aprovado** (2026-09-22).
- Issues: Epic #318 + slice #319 (spec-status-enforcement). SPEC Ticket = #319.
- Pendente: execução da SPEC-20260922-spec-status-enforcement (via orchestrator/execute-specs) e da SPEC-20260922-finops-dashboard-detail (#316).
