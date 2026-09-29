# SPEC-20260929-quality-hygiene

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `quality-hygiene` |
| Type | `Chore / Tests + Docs + Process` |
| Stack | `.NET 10 / xUnit / bUnit` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260929-quality-hygiene` |
| Ticket | [#372](https://github.com/afonsoft/agent-harness/issues/372) (Epic [#366](https://github.com/afonsoft/agent-harness/issues/366)) |
| Status | `Done` |
| Related | PRs #350, #360–#364 (achados Devin Review/CodeQL/Sonar em testes e docs) |

## 1. User Story

**As a** mantenedor do Harness
**I want** corrigir os alertas residuais de CodeQL/Sonar e fortalecer os guards/testes que os bots apontaram como frágeis, além de completar as referências de SPECs/docs
**So that** os quality gates voltem a zero issues abertas e os testes realmente detectem regressões.

**Problem context:**

Achados residuais dos bots (Devin Review, CodeQL/github-advanced-security, SonarCloud) em testes e artefatos dos PRs recentes:

1. **CodeQL** — `tests/Taskboard.Tests.Unit/Harness/CollectionValueComparerTests.cs:57`: cast `IReadOnlyList<string>` → `List<string>` questionável (alerta aberto, PR #362).
2. **SonarCloud** — `ListStringValueComparer.cs:18`: `S8970` null-forgiving desnecessário (PR #362, issue OPEN).
3. 🔍 **Teste de Tags não isola a coleção** — `CollectionValueComparerTests` usa `UpdateContent`, que também altera `UpdatedAt`/`Version` → UPDATE ocorreria mesmo sem comparer; o teste não prova RF-003 da SPEC anterior (PR #362).
4. 🔍 **Guards não verificam exports JS** — remover `pasteClipboard`/`setTerminalFocus` do objeto exportado em `terminal.js` mantém os guards verdes mas rompe o interop (PR #363).
5. 🔍 **Guard de CSS não verifica media query** — mover a regra da keybar para fora da media query mantém o teste verde mas exibe a barra no desktop (PR #363).
6. 🔍 **Estado de foco não guardado** — RF-001(d) da SPEC anterior exigia proteger o vínculo botão↔estado em `Terminal.razor`; guards só cobrem toolbar/interop (PR #363).
7. 🔍 **Ordem do NavMenu sem teste** — PR #350 mudou a ordem sem verificação automatizada (regra CLAUDE.md).
8. 🔍 **Cobertura do fluxo New indireta** — `ThreadRailTests` só testa callback; será coberto por `SPEC-20260929-ai-code-ux-fixes` (registrar aqui como dependência, não duplicar).
9. 🔍 **SPECs Done sem referência ao PR** — `SPEC-20260928-nav-menu-order` (#350) e `SPEC-20260928-taskboard-env-fallback-removal` (#364) marcam Done vinculando só o ticket; regra de lifecycle exige referência ao PR. Link "Epic #356" no header da SPEC de fallback aponta para #359.
10. 🔍 **`docs/installation.md`** — `--migrate` só cobre `~/.taskboard/data`; documentar procedimento manual para `taskboard.sqlite` em diretório custom (`HARNESS_DATA_DIR`) e quando o novo home já existe (PR #364).
11. 🔍 **Relatório de gap-analysis** — estados de retomada já corrigidos pelo usuário (verificar consistência final; nenhuma ação se já estiver coerente).

## 2. Scope

**In scope:**

- Corrigir o cast no teste (CodeQL) e o `!` no comparer (S8970).
- Reescrever o teste de `Tags` para mutação isolada da coleção.
- Fortalecer `TerminalFocusKeybarGuardTests`: verificar exports JS (`export {` / objeto de retorno), escopo da media query no CSS, e vínculo estado↔toolbar no razor.
- Guard/teste de ordem do `NavMenu`.
- Corrigir headers das SPECs (referência ao PR de entrega; link do epic).
- Nota em `docs/installation.md` para migração manual em diretórios custom.

**Out of scope:**

- Mudanças de produção nos componentes (cobertos pelas SPECs irmãs).
- Wiring de `check-spec-status.sh` em CI — decisão pendente do usuário (workflows protegidos).
- Cobertura do fluxo New — SPEC-20260929-ai-code-ux-fixes.

## 3. Technical Context

**Where the change happens:**

- `tests/Taskboard.Tests.Unit/Harness/CollectionValueComparerTests.cs`
- `src/Taskboard.EntityFrameworkCore/ValueConverters/ListStringValueComparer.cs`
- `tests/Taskboard.Tests.Unit/Blazor/TerminalFocusKeybarGuardTests.cs`
- `src/Taskboard.Client/wwwroot/js/terminal.js`, `src/Taskboard.Client/wwwroot/css/site.css`, `src/Taskboard.Blazor/Components/Pages/Terminal.razor` (leitura)
- `src/Taskboard.Blazor/Layout/NavMenu.razor` (leitura) + novo teste
- `.specs/SPEC-20260928-nav-menu-order.md`, `.specs/SPEC-20260928-taskboard-env-fallback-removal.md`
- `docs/installation.md` (+pt-br se existir)

## 4. Requirements

### RF-001: Alertas CodeQL/Sonar zerados

- **Description:** Substituir o cast `List<string>` no teste por coleção comparável (ex.: `ToList()` sobre o resultado ou `IReadOnlyList`); corrigir o cast em `CollectionValueComparerTests.cs` (o `null!` de `ListStringValueComparer.cs:18` é semântico — mantido com comentário explicativo).

### RF-002: Teste de Tags isolado

- **Description:** Provar detecção de mudança alterando apenas `Tags` (sem `UpdateContent`): verificar `ChangeTracker.HasChanges()`/estado `Modified` exclusivamente pela coleção.

### RF-003: Guards de export JS

- **Description:** Guard verifica que `setTerminalFocus`, `pasteClipboard` (e demais membros do interop usados pelo razor) constam no objeto exportado de `terminal.js` — não apenas nomes na fonte.

### RF-004: Guard de media query

- **Description:** Guard verifica que a regra de visibilidade da keybar (`display`/classe) está dentro do bloco da media query de touch — parser simples de bloco ou regex de contexto.

### RF-005: Guard do estado de foco

- **Description:** Guard verifica o vínculo entre o botão de foco e o estado em `Terminal.razor` (handler chamando `setTerminalFocus` + atributo `data-terminal-focus`).

### RF-006: Teste de ordem do NavMenu

- **Description:** Teste (bUnit ou guard) fixando a ordem AI Code < Board e Terminal < AI Code conforme a SPEC de menu.

### RF-007: SPECs e docs

- **Description:** Adicionar referência ao PR de entrega nos metadados das SPECs Done afetadas; corrigir link do epic (#359→#356); nota de migração manual em `installation.md` para `taskboard.sqlite` em diretório custom / home existente.

## 5. API Contract

Sem mudança.

## 6. Acceptance Criteria

- [ ] **Given** os testes modificados **when** executados **then** verdes e os alertas CodeQL/Sonar correspondentes fecham na próxima análise.
- [ ] **Given** remoção simulada de um export JS ou da media query **when** o guard roda **then** falha com mensagem clara (verificação por mutação manual do autor).
- [ ] **Given** mutação isolada de `Tags` **when** `SaveChanges` roda **then** a mudança é detectada somente pela coleção.
- [ ] **Given** headers das SPECs e `installation.md` **when** revisados **then** referências corretas e procedimento documentado.
- [ ] **Given** build/test **then** verde.

## 7. Task Plan (agent execution)

- [ ] **T1 — CodeQL/Sonar:** cast + `!` + teste Tags isolado.
- [ ] **T2 — Guards:** exports JS, media query, estado de foco.
- [ ] **T3 — NavMenu:** teste de ordem.
- [ ] **T4 — Docs/SPECs:** referências e nota de migração.
- [ ] **T5 — Done + PR.**

## 8. Organization Guardrails

- Somente testes/docs/headers — exceção: o `!` em `ListStringValueComparer` (mudança trivial em src).
- `.github/workflows/**` intocado.

## 9. Definition of Done

- [ ] Alertas abertos de CodeQL/Sonar tratados.
- [ ] Guards realmente pegam as remoções simuladas.
- [ ] Build/test verde; `Status = Done`.
