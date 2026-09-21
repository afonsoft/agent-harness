# SPEC-20260920-docs-sync-ade-platform

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `docs-sync-ade-platform` |
| Type | `Docs` (sincronização pós-onda ADE E6–E16 + rename) |
| Stack | `Markdown / Mermaid / draw.io` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | — |
| Ticket | [#251](https://github.com/afonsoft/agent-harness/issues/251) — GAP-documentation-architecture-drift + readme-feature-drift + knowledge-dir (gap-analysis-20260920) |
| Status | `Approved` |

---

## 1. User Story

**As a** engenheiro/agente que abre o repositório
**I want** README e arquitetura refletindo a plataforma atual
**So that** entendo em 5 minutos o que o produto faz hoje — não o que ele fazia antes da onda ADE.

### Problem Context

A onda E6–E16 (worktrees, context/memory, security gateway, verification loop, CLI metrics, multi-agent DAG, living specs, FinOps, cockpit HITL, repo selector, maintenance jobs) está entregue e documentada em `docs/features.md` + `docs/api.md`, mas:

1. **`README.md`** descreve o produto pré-ADE (taskboard + agent CLIs) — não menciona cockpit, `/specs`, seletor global de repo, pipelines multi-agente, FinOps, gateway de segurança, verification loop nem memory.
2. **`docs/architecture/architecture.md`** (e os gerados `architecture.html`/`.json`) parou na arquitetura pré-ADE: zero menção a `PipelineEngine`, `CockpitHub`, worktrees, `PermissionGateway`, `VerificationLoop`, `MemoryService`, `FinOps`, `SpecDriftScanService`, `SelectedRepositoryService`.
3. **`CLAUDE.md`** tabela "Caminhos por Plataforma" prescreve `.claude/knowledge/` para todas as plataformas — o diretório não existe no repo.

**Evidência:**

- AS-IS: `grep -niE 'cockpit|pipeline|worktree|selectedrepo' docs/architecture/architecture.md` → 0 matches; `grep -niE 'cockpit|repo.selector' README.md` → 0 matches; `ls .claude/knowledge` → inexistente.
- TO-BE: SPECs E6–E16 `Done` em `.specs/`; descrição do repo "local-first AI agent workbench".

---

## 2. Scope

### In scope

- **`README.md` + `README.pt-br.md`:** atualizar Overview com as capacidades atuais (cockpit HITL, pipelines DAG, repo selector, `/specs` living specs, FinOps, security gateway, verification loop, context/memory, maintenance jobs); seção de páginas/features atualizada; manter bilingual paridade.
- **`docs/architecture/architecture.md`:** novo mapa de componentes cobrindo os módulos de harness (`Taskboard.Application/Harness`, `Taskboard.Server/Hubs/CockpitHub`, `Taskboard.Integrations` worktree/security/vscode/terminal, jobs de background); fluxos: pipeline run (start → stages → gates → PR), repo selector → consumers, terminal/vscode lifecycle; regenerar `architecture.html`/`.json` se forem derivados do `.md` (verificar pipeline de geração antes de editar manualmente).
- **`.claude/knowledge/`:** criar o diretório prescrito pelo CLAUDE.md (com `.gitkeep` + nota de convenção) ou ajustar a tabela se a decisão for outra — decidir e registrar.
- `CHANGELOG.md`: entrada `Unreleased` com a onda ADE + rename, se ainda não houver.

### Out of scope

- Reescrever `docs/features.md`/`docs/api.md` — já sincronizados (verificado nesta auditoria).
- ADRs novos por feature — apenas sincronizar o documento de arquitetura existente.

---

## 3. Acceptance Criteria (BDD)

- **AC1:** README (en + pt-br) menciona cockpit, seletor de repositório, `/specs`, pipelines/FinOps e gateway/verificação — conferível por grep.
- **AC2:** `architecture.md` contém seção de harness/pipeline com os componentes entregues (nomes reais do código); artefatos gerados consistentes com o `.md`.
- **AC3:** `.claude/knowledge/` existe (ou a tabela do CLAUDE.md foi corrigida) — decisão registrada no commit.
- **AC4:** `dotnet build`/`dotnet test` intactos (docs-only, mas verificar que nenhum arquivo referenciado quebrou).

---

## 4. Tasks

- [ ] **T1 — README sync** (en + pt-br): overview + lista de features + links.
- [ ] **T2 — architecture.md sync**: componentes ADE + diagramas mermaid atualizados; regenerar html/json se aplicável.
- [ ] **T3 — knowledge dir**: criar ou corrigir tabela do CLAUDE.md.
- [ ] **T4 — changelog + verificação**: entrada Unreleased, grep-check das ACs, SPEC → `Done`.

---

## 5. Verification

- `grep -niE 'cockpit|pipeline|worktree|finops|gateway' README.md docs/architecture/architecture.md` → matches.
- `grep -niE 'cockpit|pipeline' docs/architecture/architecture.html` → consistente se o html é derivado.
- `ls .claude/knowledge` → existe (ou CLAUDE.md corrigido).
- Docs bilingual: mesma estrutura en/pt-br.

---

## 6. Risks & Open Questions

1. `architecture.html`/`.json` podem ser artefatos gerados por outra ferramenta (archify/drawio) — verificar proveniência antes de editar na mão.
2. Decisão: `.claude/knowledge/` criar vazio vs remover da tabela — perguntar no review do spec se ambíguo.
