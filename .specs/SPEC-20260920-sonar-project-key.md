# SPEC-20260920-sonar-project-key

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `sonar-project-key` |
| Type | `Chore` (CI quality config pós-rename) |
| Stack | `GitHub Actions / SonarCloud` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | — |
| Ticket | [#253](https://github.com/afonsoft/agent-harness/issues/253) — GAP-automation-sonar-project-key (gap-analysis-20260920) |
| Status | `Done` |

---

## 1. User Story

**As a** mantenedor do repo
**I want** decidir e executar o alinhamento do projeto SonarCloud com o nome novo
**So that** a análise de qualidade não fica atrelada a um identificador órfão do nome antigo (`afonsoft_taskboard-ai`).

### Problem Context

O repo foi renomeado `afonsoft/taskboard-ai` → `afonsoft/agent-harness` (2026-09-20), mas `.github/workflows/code-quality.yml` ainda usa `SONAR_PROJECT_KEY: afonsoft_taskboard-ai`. Funciona (a key é opaca), mas diverge do nome do produto/repo e foi deixado pendente por ser arquivo protegido + decisão com trade-off.

**Evidência:**

- AS-IS: `.github/workflows/code-quality.yml:27` `SONAR_PROJECT_KEY: afonsoft_taskboard-ai`.
- TO-BE: repo `afonsoft/agent-harness`; branding rules atualizadas.
- ⚠️ **Escalation gate duplo:** workflow protegido (Hard Rule 2) + mutação externa no SonarCloud.

### Opções

| Opção | Prós | Contras |
|---|---|---|
| **A — Manter key antiga** | Zero trabalho, histórico Sonar preservado | Nome divergente permanente; confusão futura |
| **B — Recriar projeto como `afonsoft_agent-harness`** | Nome consistente | Perde histórico/baseline do Sonar; exige criar projeto no SonarCloud + atualizar key |
| **C — Renomear no SonarCloud** | Consistência se a plataforma permitir manter dados | Nem toda operação de rename preserva histórico — verificar |

---

## 2. Scope

### In scope

- Decisão registrada (A/B/C) no PR.
- Se B ou C: atualizar `SONAR_PROJECT_KEY` no workflow + verificar que o check `SonarCloud Analysis` passa; documentar em `AGENTS.md`/docs se relevante.
- Se A: fechar este spec como `Deprecated`/`Won't do` com justificativa — nenhum código muda.

### Out of scope

- Outras mudanças no SonarCloud (quality gates, exclusões).
- Secrets (`SONAR_TOKEN` já existe e não muda).

---

## 3. Acceptance Criteria (BDD)

- **AC1:** decisão A/B/C explicitada no PR/spec.
- **AC2 (se B/C):** `code-quality.yml` com key nova, check SonarCloud verde no PR.
- **AC3 (se A):** spec fechado sem mudança de código + nota em `AGENTS.md` ou decisão registrada.

---

## 4. Tasks

- [x] **T1:** decisão do usuário → executar opção escolhida + verificar CI.

---

## 5. Verification

- `grep SONAR_PROJECT_KEY .github/workflows/code-quality.yml` → valor decidido.
- Job `SonarCloud Analysis` verde no PR seguinte.

---

## 6. Risks & Open Questions

1. Opção B perde o baseline de issues do Sonar (quality gate pode re-computar "new code" do zero — primeiro scan pode acusar issues antigas como novas).
2. Verificar se SonarCloud permite renomear a key preservando histórico (opção C) antes de assumir B.

---

## Decisão registrada (2026-09-21)

**Opção B já aplicada** — o rename commit `cd8fd6f` (PR #246) migrou `SONAR_PROJECT_KEY` para `afonsoft_agent-harness` em `code-quality.yml:27`. Nenhuma edição adicional de workflow é necessária.

**Estado real da análise:** o job `SonarCloud Analysis` está **dormant** — `SONAR_TOKEN` não está configurado nos secrets do repo e o step `Check SONAR_TOKEN` sai com "skipping SonarCloud analysis" (verificado em run 35557772067, 2026-09-21). O "pass" do check é vacuoso: nenhuma análise roda.

**Pendência operacional (fora do repo):** para ativar a análise é preciso (1) confirmar/criar o projeto `afonsoft_agent-harness` no SonarCloud org `afonsoft` e (2) adicionar o secret `SONAR_TOKEN`. Ambos exigem acesso admin ao SonarCloud — ação manual do mantenedor. A key no workflow já está correta para quando o token for configurado.
