# SPEC-20260920-coverage-ratchet-bump

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `coverage-ratchet-bump` |
| Type | `Chore` (CI coverage gate) |
| Stack | `GitHub Actions / coverlet / reportgenerator` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | — |
| Ticket | [#252](https://github.com/afonsoft/agent-harness/issues/252) — GAP-automation-coverage-ratchet-stale (gap-analysis-20260920) |
| Status | `Approved` |

---

## 1. User Story

**As a** mantenedor do repo
**I want** o `COVERAGE_THRESHOLD` refletindo a cobertura real medida
**So that** o ratchet de qualidade aperta conforme a política (sobe a cada sprint até ≥80%, meta 90%) e não permite regressão até a realidade atual.

### Problem Context

A política documentada (`AGENTS.md`/`CLAUDE.md` Hard Rule 6) diz que o gate é ratchet — nunca desce e sobe a cada sprint. Baseline medida: **66.26% em 2026-09-19** com `COVERAGE_THRESHOLD: 65` em `.github/workflows/dotnet.yml`. Medição local em 2026-09-20 (unit + integration, cobertura.xml mergeado): **73.70% (26966/36587 linhas)** — o gate está ~9 pontos abaixo da realidade, permitindo regressão silenciosa.

**Evidência:**

- TO-BE: `AGENTS.md` Hard Rule 6 — "gate ratchet… sobe a cada sprint até ≥80% (meta 90%)".
- AS-IS: `.github/workflows/dotnet.yml:44` `COVERAGE_THRESHOLD: 65`; medição local reproduzível: `dotnet test --collect:"XPlat Code Coverage"` nos dois projetos + merge cobertura → 73.70%.
- ⚠️ **Escalation gate:** o arquivo está em `.github/workflows/` — Hard Rule 2 exige aprovação humana para editá-lo.

---

## 2. Scope

### In scope

- Subir `COVERAGE_THRESHOLD` em `dotnet.yml` para o valor medido arredondado para baixo com folga (ex.: `73` — nunca acima da medida, o ratchet é floor não target).
- Documentar a nova baseline em `AGENTS.md`/`CLAUDE.md` ("Baseline medido: 73.70% em 2026-09-20").
- PR explicitamente marcado como workflow-touching (soft rule: avisar; hard rule: aprovação humana prévia já obtida via gate desta auditoria).

### Out of scope

- Subir direto para 80/90 — o ratchet sobe com a realidade medida, não com a meta.
- Mudar a metodologia de medição (mantém-se line coverage dos 2 test projects, igual ao CI).
- Qualquer outra edição em workflows.

---

## 3. Acceptance Criteria (BDD)

- **AC1:** `COVERAGE_THRESHOLD` no workflow ≥ 70 e ≤ 73 (nunca acima da cobertura medida).
- **AC2:** `AGENTS.md`/`CLAUDE.md` registram a baseline medida com data.
- **AC3:** CI `Build, Test & Coverage` passa com a nova threshold.
- **AC4:** Nenhum outro workflow alterado; aprovação humana registrada no PR.

---

## 4. Tasks

- [ ] **T1:** bump `COVERAGE_THRESHOLD` + baseline docs + PR com aviso de workflow-touching.

---

## 5. Verification

- `grep 'COVERAGE_THRESHOLD' .github/workflows/dotnet.yml` → novo valor.
- CI run no PR verde no job Build/Test/Coverage.

---

## 6. Risks & Open Questions

1. Flaky coverage: integração pode oscilar ±0.5pp — usar floor conservador (73 ou 72.5).
2. Se a medição em CI diferir da local (ordem de testes/flakes), escolher o menor dos dois valores.
