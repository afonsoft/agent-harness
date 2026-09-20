# SPEC-20260919-ade-living-specs

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ade-living-specs` |
| Type | `Feature` (ADE Control Plane & Living Specifications) |
| Stack | `.NET 10 / Blazor WASM / Markdig / SQLite / C# 14` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-ade-living-specs` |
| Ticket | [#168 — E13](https://github.com/afonsoft/taskboard-ai/issues/168) |
| Status | `Done` |

---

## 1. User Story

**As a** arquiteto de software ou desenvolvedor
**I want** gerenciar especificações vivas (*Living Specifications*) diretamente na UI do ADE (`/specs`) com rastreamento automatizado de requisitos, critérios de aceitação BDD e detecção de desvio de código (*spec drift*)
**So that** as especificações sejam a fonte única da verdade para humanos e agentes, garantindo que nenhum código seja implementado sem spec e que nenhuma spec fique desatualizada em relação ao código real.

### Problem Context

No repositório atual:
1. **Especificações como arquivos estáticos:** Existem mais de 70 arquivos em `.specs/`, mas eles são tratados apenas como arquivos markdown soltos. Não há entidade de domínio no sistema que os represente.
2. **Falta de rastreabilidade de BDD e Requisitos:** Não há como o sistema saber via API ou UI se o requisito `RF-003` da `SPEC-015` tem testes correspondentes passando.
3. **Spec Drift recorrente:** Conforme evidenciado no histórico do repositório (`gap-analysis-20260919.md`), ocorre frequentemente *drift* onde o código evolui (ex: troca de biblioteca CLI, remoção de entidades) mas as SPECs permanecem em `Approved` ou descrevendo arquiteturas antigas.

---

## 2. Scope

### In scope

- **Parser Estruturado de SPECs SDD (`ISpecDocumentParser`):**
  - Leitura de arquivos `.specs/SPEC-*.md` e extração de metadados (seção 0), user stories (seção 1), requisitos funcionais (seção 4), critérios de aceitação BDD (seção 6) e tarefas (seção 7).
- **Entidade de Domínio e Catálogo de Specs no ADE:**
  - Modelo `LivingSpecification` com ciclo de vida: `Draft`, `Approved`, `InImplementation`, `Done`, `Deprecated`.
  - Endpoint REST e página Blazor `/specs` com listagem, busca, filtros por status e visualização rica em markdown.
- **Motor de Detecção de Spec Drift (`ISpecDriftDetector`):**
  - Comparação automática entre a spec e o código:
    - Validação de arquivos citados em "Files to create or modify" (existem no disco?).
    - Validação de status: specs com `Approved` cujos testes e código já existem no `main` são sinalizadas para transição para `Done`.
    - Diffs de código recente que tocam áreas cobertas por specs sem atualizar a respectiva spec.
- **Ação Rápida "Run Spec":**
  - Botão na UI para disparar um `AgentRun` no pipeline ADE diretamente a partir de uma spec aprovada.

### Out of scope

- Editor visual WYSIWYG complexo (a edição pode ser feita em markdown nativo ou via agente).

---

## 3. Technical Context

### Where the change happens

- **Contracts:** `Taskboard.Application.Contracts/Specs/`, `LivingSpecDto.cs`, `SpecDriftReportDto.cs`.
- **Integrations:** `Taskboard.Integrations/Specs/MarkdigSpecParser.cs`, `SpecDriftDetector.cs`.
- **Server:** Endpoints `/api/specs`, `/api/specs/{id}`, `/api/specs/drift`.
- **Blazor WASM:** Página `SpecsPage.razor`, componente `SpecDetailDrawer.razor`, `BddScenarioBadge.razor`.

### Files to read before implementing

- `.specs/SPEC-000-overview.md`
- `.specs/SPEC-20260914-stale-spec-status.md`
- `.specs/SPEC-20260919-stale-spec-status.md`
- `.claude/memory/gap-analysis-20260919.md`

### Files to create or modify

```text
src/Taskboard.Domain.Shared/Specs/SpecStatus.cs                       [new]
src/Taskboard.Domain/Entities/Specs/LivingSpecification.cs           [new]
src/Taskboard.Application.Contracts/Specs/ISpecAppService.cs         [new]
src/Taskboard.Application.Contracts/Specs/Dtos/LivingSpecDto.cs      [new]
src/Taskboard.Application/Specs/SpecAppService.cs                    [new]
src/Taskboard.Integrations/Specs/MarkdigSpecParser.cs                [new]
src/Taskboard.Integrations/Specs/SpecDriftDetector.cs                [new]
src/Taskboard.Blazor/Pages/Specs/SpecsPage.razor                     [new]
src/Taskboard.Blazor/Components/Specs/SpecViewerModal.razor          [new]
tests/Taskboard.Tests.Unit/Specs/MarkdigSpecParserTests.cs           [new]
```

---

## 4. Requirements

### RF-001: Indexação e Leitura de SPECs
- **Description:** O serviço deve escanear a pasta `.specs/` ao iniciar e sempre que solicitado, parseando todas as specs em conformidade com o template SDD (seções 0 a 9).
- **Rules:** Seções ausentes ou não parseáveis são reportadas como `SpecLintWarning` sem interromper o serviço.
- **Input → Output:** Arquivo `.specs/SPEC-20260919-ade-cockpit-hitl.md` → Objeto `LivingSpecDto` com 5 requisitos e 5 critérios de aceitação BDD.

### RF-002: Detecção Automatizada de Stale Status
- **Description:** O motor de drift deve verificar se os arquivos listados em uma spec com status `Draft` ou `Approved` já existem no repositório e possuem testes verdes passando.
- **Rules:** Se todos os arquivos existirem e a suíte estiver verde, a spec é sinalizada com aviso de `StatusStale: Sugestão para marcar como Done`.

### RF-003: Interface Blazor de Catálogo de Specs
- **Description:** Na rota `/specs`, exibir cards e tabela com todas as especificações do projeto, indicando título, data, status (`Draft` amarelo, `Approved` azul, `Done` verde), stack e contagem de requisitos.
- **Input → Output:** Filtro por `Status = Draft` → Exibe apenas specs aguardando aprovação.

### RF-004: Disparo Direto de Execução (Run with Agent)
- **Description:** Para qualquer spec com `Status = Approved`, exibir botão "Executar com Agente".
- **Rules:** Ao clicar, abre o modal de seleção de pipeline e cria um `AgentRun` vinculado ao arquivo da spec.

---

## 5. API Contract

```http
GET /api/specs
→ 200 OK
[
  {
    "id": "SPEC-20260919-ade-cockpit-hitl",
    "title": "ade-cockpit-hitl",
    "type": "Feature",
    "status": "Draft",
    "date": "2026-09-19",
    "requirementsCount": 5,
    "acceptanceCriteriaCount": 5,
    "isDriftDetected": false
  }
]

GET /api/specs/drift-report
→ 200 OK
{
  "totalSpecs": 76,
  "staleSpecsCount": 2,
  "driftItems": [
    {
      "specId": "SPEC-20260918-action-button-separation",
      "currentStatus": "Approved",
      "suggestedStatus": "Done",
      "reason": "Todos os arquivos e testes existem e estão passando na branch main"
    }
  ]
}
```

---

## 6. Acceptance Criteria

- [x] **Given** a pasta `.specs/` com arquivos no formato padrão, **when** `GetAllSpecsAsync` é chamado, **then** todas as especificações válidas são retornadas como DTOs estruturados.
- [x] **Given** uma spec com status `Draft`, **when** o usuário aprova via UI ou API, **then** o arquivo markdown em disco é atualizado com `Status: Approved`.
- [x] **Given** uma spec com arquivos que já foram implementados e testados, **when** o detector de drift roda, **then** emite recomendação para transição para `Done`.

---

## 7. Task Plan

- [x] **T1 — Markdown SDD Parser:** Implementar parser usando Markdig para extrair metadados, tabelas e requisitos.
- [x] **T2 — App Service & Endpoints:** Criar `SpecAppService` e rotas REST para listagem, leitura e atualização de specs.
- [x] **T3 — Drift Engine:** Implementar checagem de existência de arquivos e estado de commits para apontar desvios.
- [x] **T4 — Blazor UI:** Criar tela `/specs` no Blazor WASM com layout limpo e badges de BDD.
- [x] **T5 — Unit Tests:** Testar parser com specs reais do repositório garantindo 0 falhas de parsing.

---

## 8. Organization Guardrails

- Não sobrescrever seções livres do markdown ao atualizar o status na tabela de metadados.
- Preservar integridade de codificação UTF-8 em todos os arquivos de especificação.

---

## 9. Definition of Done

- [x] Parser de SPECs cobrindo todo o acervo de `.specs/`.
- [x] Tela `/specs` disponível na navegação principal do Harness.
- [x] Relatório de drift funcionando e integrado ao painel.
