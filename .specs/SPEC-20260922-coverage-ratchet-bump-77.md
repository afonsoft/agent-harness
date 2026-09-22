# SPEC-20260922-coverage-ratchet-bump-77

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `coverage-ratchet-bump-77` |
| Type | `Chore` (CI coverage gate) |
| Stack | `GitHub Actions / coverlet / reportgenerator` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | — |
| Ticket | [#308](https://github.com/afonsoft/agent-harness/issues/308) — GAP-automation-coverage-ratchet-bump (epic #306); ciclo anterior: #252 |
| Status | `Done — delivered in PR #312` |

---

## 1. User Story

**As a** mantenedor do repo
**I want** o `COVERAGE_THRESHOLD` refletindo a cobertura real medida
**So that** o ratchet de qualidade continua apertando conforme a política (nunca desce, sobe a cada sprint até ≥80%, meta 90%).

### Problem Context

Segundo ciclo do ratchet (primeiro: SPEC-20260920-coverage-ratchet-bump, issue #252, threshold 65→73). Medição local em 2026-09-22 (unit + integration, cobertura mergeada via `reportgenerator` com `-assemblyfilters:"-*.Tests.*"`, idêntico ao CI): **77.85%** — o gate está ~5 pontos abaixo da realidade, permitindo regressão silenciosa.

**Evidência:**

- TO-BE: `AGENTS.md`/`CLAUDE.md` Hard Rule 6 — "gate ratchet — `COVERAGE_THRESHOLD` nunca desce e sobe a cada sprint até ≥80% (meta 90%)".
- AS-IS: `.github/workflows/dotnet.yml:44` `COVERAGE_THRESHOLD: 73`; medição: `dotnet test Taskboard.sln -c Release --collect:"XPlat Code Coverage"` + `reportgenerator` → `line-rate="0.7785"`.
- ⚠️ **Escalation gate:** o arquivo está em `.github/workflows/` — Hard Rule 2 exige aprovação humana para editá-lo.

---

## 2. Scope

### In scope

- Subir `COVERAGE_THRESHOLD` em `dotnet.yml` para `77` (floor conservador abaixo de 77.85%, absorvendo oscilação dos flakes de integração).
- Atualizar a baseline documentada em `AGENTS.md`/`CLAUDE.md` ("Baseline medido: 77.85% em 2026-09-22").
- PR marcado como workflow-touching; aprovação humana prévia via gate desta auditoria.

### Out of scope

- Subir direto para 80/90 — o ratchet sobe com a realidade medida.
- Mudar metodologia de medição ou assembly filters.
- Qualquer outra edição em workflows.

---

## 3. Acceptance Criteria (BDD)

- **AC1:** `COVERAGE_THRESHOLD` ≤ 77 (nunca acima da cobertura medida).
- **AC2:** `AGENTS.md`/`CLAUDE.md` registram baseline 77.85% com data 2026-09-22.
- **AC3:** CI `Build, Test & Coverage` verde com a nova threshold.
- **AC4:** Nenhum outro workflow alterado; aprovação humana registrada no PR.

---

## 4. Tasks

- [ ] **T1:** bump `COVERAGE_THRESHOLD` → 77 + baseline docs + PR com aviso de workflow-touching.

---

## 5. Verification

- `grep 'COVERAGE_THRESHOLD' .github/workflows/dotnet.yml` → `77`.
- CI run do PR verde no job Build/Test/Coverage.

---

## 6. Risks & Open Questions

1. Flaky coverage: integração oscila — 77 (vs 77.85 medido) já embute folga de ~0.85pp.
2. Depende de `SPEC-20260922-flaky-integration-tests` para estabilidade do número? Não — o gate mede cobertura, não sucesso de teste; mas CI verde no PR exige os testes passarem (o flake pode falhar o job antes do gate). Ordem sugerida: flaky-tests primeiro.
