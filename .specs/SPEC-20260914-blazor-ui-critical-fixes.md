# SPEC-20260914-blazor-ui-critical-fixes

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `blazor-ui-critical-fixes` |
| Type | `Bugfix` (inclui pequeno incremento de frontend: árvore de arquivos de skill) |
| Stack | `.NET 10 / Blazor Server` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `fix/blazor-ui-critical-fixes` (aprovado pelo usuário; `main`/`develop` proibidas) |
| Ticket | `user-report-2026-09-14` |
| Status | `Approved` |

## 1. User Story

**As a** Taskboard user
**I want** the Blazor UI to navigate via the sidebar, load GitHub boards, persist settings, and show the full file tree of each installed skill
**So that** the .NET 10 UI is actually usable end-to-end instead of crashing or dead-ending on four critical paths.

**Problem context:**
Four independent bugs were reported on 2026-09-14, all root-caused with evidence:

1. **Board / GitHub Board** — `Erro ao carregar issues: Repository name must be in the format 'owner/name'. Value: '_selectedRepo'`. `KanbanBoard RepositoryFullName="_selectedRepo"` (sem `@`) passa a string literal `_selectedRepo` em vez do valor do campo. Blazor trata atributo de parâmetro `string` sem `@` como literal. O mesmo bug existe em `GitHubBoard.razor` com `_selectedRepositoryFullName`.
2. **Sidebar navigation não navega** — todo `NavLink` em `NavMenu.razor` carrega `data-bs-dismiss="offcanvas" data-bs-target="#appSidebar"`. O data-API do Bootstrap chama `event.preventDefault()` em elementos `<a>` com `data-bs-dismiss`, cancelando a navegação em qualquer viewport (o `offcanvas-lg` é estático em desktop, mas o handler dispara mesmo assim).
3. **"An unexpected error occurred" ao salvar Settings (e ao trocar senha)** — `TaskboardClient` faz self-call HTTP ao próprio servidor **sem propagar o cookie de autenticação**. `PUT /api/settings` e `PUT /api/admin/password` são os únicos endpoints com `.RequireAuthorization()` → retornam **401** (reproduzido via curl) → `EnsureSuccessStatusCode()` lança `HttpRequestException` não tratada em `SaveAsync` → `ErrorBoundary` do `MainLayout` exibe a mensagem genérica. Em `ChangePasswordAsync` o 401 é capturado mas reporta a mensagem errada ("Current password is incorrect").
4. **"An unexpected error occurred" na tela de Skills** — `@key="skill.Name"` em `Skills.razor` produz keys duplicadas: o ambiente real retorna 518 skills com **124 nomes duplicados** (mesma skill instalada em `~/.claude`, `~/.cursor`, `~/.config/devin`, `~/.opencode`). O primeiro render do circuito (loading) seguido do render da lista dispara `InvalidOperationException` do diff de keys → `ErrorBoundary`. O prerender SSR escapa porque só executa um render.
5. **Detalhe de skill não mostra a árvore de arquivos** — `SkillDetailDto` não expõe os demais arquivos do diretório da skill (`references/`, `scripts/`, etc.); `SkillDiscoveryService.GetDetailAsync` lê apenas `SKILL.md`.

## 2. Scope

**In scope:**
- Corrigir binding de `RepositoryFullName` em `BoardView.razor` e `GitHubBoard.razor`.
- Corrigir navegação da sidebar (`NavMenu.razor`), preservando o fechamento do drawer offcanvas em mobile (`< lg`).
- Fazer self-calls autenticadas do `TaskboardClient` propagarem a identidade do usuário (cookie forwarding) e tratar falhas em `SaveAsync`/`ChangePasswordAsync` com feedback adequado.
- Corrigir a key duplicada no grid de skills.
- Estender `GET /api/skills/{source}/{name}` com a lista de arquivos da skill e adicionar endpoint para ler o conteúdo de um arquivo, com proteção contra path traversal.
- Exibir a árvore de arquivos no `SkillDetailDialog`, com visualização de conteúdo ao clicar.

**Out of scope:**
- Refatorar outros usos do `TaskboardClient` além do necessário para o forwarding de auth.
- Mudar o modelo de autenticação (cookie de admin continua sendo o mecanismo).
- Edição de arquivos de skill pela UI (somente leitura).
- bUnit ou testes de componente Blazor (novo pacote — não aprovado; validação de UI é manual).
- Alterações em `.github/workflows/**`.

## 3. Technical Context

**Where the change happens:** `src/Taskboard.Blazor` (componentes + `TaskboardClient` + captura de cookie no root interativo), `src/Taskboard.Application.Contracts/Skills` (DTOs), `src/Taskboard.Integrations/Skills` (`SkillDiscoveryService`), `src/Taskboard.Server/Program.cs` (endpoint novo), `tests/Taskboard.Tests.Integration` (testes de endpoint).

**Files to read before implementing:**
- `AGENTS.md` · `.specs/SPEC-008-frontend.md` · `.specs/SPEC-20260911-skills-ux-redesign.md` · `.specs/SPEC-20260913-bootstrap-modernization.md`
- `src/Taskboard.Blazor/Layout/NavMenu.razor` · `src/Taskboard.Blazor/Layout/MainLayout.razor` · `src/Taskboard.Blazor/App.razor` · `src/Taskboard.Blazor/Routes.razor`
- `src/Taskboard.Blazor/Components/BoardView.razor` · `src/Taskboard.Blazor/Components/Pages/GitHubBoard.razor` · `src/Taskboard.Blazor/Components/Pages/Settings.razor` · `src/Taskboard.Blazor/Components/Pages/Skills.razor` · `src/Taskboard.Blazor/Components/Shared/SkillDetailDialog.razor`
- `src/Taskboard.Blazor/Services/TaskboardClient.cs`
- `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` · `src/Taskboard.Application.Contracts/Skills/SkillDetailDto.cs`
- `src/Taskboard.Server/Program.cs` (seções `AddHttpClient<TaskboardClient>` e endpoints `settings`/`skills`)

**Files to create or modify:**
```text
src/Taskboard.Blazor/Components/BoardView.razor               # @ no binding de RepositoryFullName
src/Taskboard.Blazor/Components/Pages/GitHubBoard.razor       # idem para _selectedRepositoryFullName
src/Taskboard.Blazor/Layout/NavMenu.razor                     # remover data-bs-dismiss; fechar offcanvas via JS quando aberto
src/Taskboard.Blazor/Components/Pages/Skills.razor            # @key único por skill (Path)
src/Taskboard.Blazor/Components/Pages/Settings.razor          # try/catch + mensagens corretas
src/Taskboard.Blazor/Components/Shared/SkillDetailDialog.razor# seção "Files" + viewer
src/Taskboard.Blazor/Services/TaskboardClient.cs              # attach Cookie header por request
src/Taskboard.Blazor/Services/CircuitAuthContext.cs           # NOVO: scoped, guarda cookie do circuito
src/Taskboard.Blazor/Components/AuthCookieCapture.razor       # NOVO: persiste/restaura cookie via PersistentComponentState
src/Taskboard.Server/wwwroot/js/taskboard.js                  # NOVO (ou site.js existente): closeSidebar helper
src/Taskboard.Blazor/App.razor                                # referenciar o script js se criado
src/Taskboard.Server/Program.cs                               # registrar AuthCookieCapture/Routes; novo endpoint de arquivo
src/Taskboard.Application.Contracts/Skills/SkillDetailDto.cs  # + Files
src/Taskboard.Application.Contracts/Skills/SkillFileDto.cs    # NOVO
src/Taskboard.Application.Contracts/Skills/ISkillDiscoveryService.cs # + GetFileAsync/ResolveFile
src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs    # listagem de arquivos + leitura segura
tests/Taskboard.Tests.Integration/ServerEndpointsTests.cs     # testes dos endpoints de skills/files
```

## 4. Requirements

> Number every requirement (RF-001, RF-002...). Must be objective and verifiable.

### RF-001: Binding correto do repositório GitHub
- **Description:** `KanbanBoard` deve receber o valor do campo, não o literal. Em `BoardView.razor` usar `RepositoryFullName="@_selectedRepo"`; em `GitHubBoard.razor` usar `RepositoryFullName="@_selectedRepositoryFullName"`.
- **Rules:** nenhum parâmetro `string` de componente pode receber nome de campo sem `@`.
- **Input → Output:** seleção `repo:owner/name` → `KanbanBoard` carrega issues de `owner/name` sem erro de formato.

### RF-002: Sidebar navegável
- **Description:** `NavLink` deve navegar em todos os viewports. Remover `data-bs-dismiss`/`data-bs-target` dos links. Para manter a UX mobile, o offcanvas deve ser fechado programaticamente (`bootstrap.Offcanvas.getInstance('#appSidebar')?.hide()`) após o clique, somente quando estiver aberto como overlay.
- **Rules:** nenhum `data-bs-*` de dismiss em elementos `<a>` de navegação; helper JS deve ser no-op em desktop.
- **Input → Output:** clique em item da sidebar → navega para a rota; em `< lg` o drawer também fecha.

### RF-003: Self-call autenticada do Blazor ao próprio API
- **Description:** `TaskboardClient` deve propagar o cookie de autenticação do usuário nas chamadas HTTP que ele mesmo faz ao servidor. Mecanismo aprovado: **cookie forwarding** —
  1. scoped `CircuitAuthContext` guarda `string? Cookie`;
  2. componente no root interativo (`AuthCookieCapture` ou lógica em `Routes.razor`) persiste o cookie durante o prerender via `PersistentComponentState` e o restaura no circuito interativo;
  3. `TaskboardClient` anexa o header `Cookie` em cada request quando o contexto está preenchido (configurável no construtor da instância transient, sem estado em `HttpMessageHandler` compartilhado — o pool de handlers é compartilhado entre circuitos e não pode reter cookie por usuário).
- **Rules:** nunca logar o cookie; não armazenar cookie em singleton/handler compartilhado; quando não houver cookie (ex.: render sem auth), o request segue sem header.
- **Input → Output:** `PUT /api/settings` a partir da UI → `204` em vez de `401`.

### RF-004: Erros tratados em Settings
- **Description:** `SaveAsync` e `ChangePasswordAsync` devem capturar `HttpRequestException`/`Exception` e exibir feedback (toast/inline) em vez de estourar o `ErrorBoundary`. `ChangePasswordAsync` deve distinguir 401 (não autorizado/sessão expirada) de falha de validação.
- **Input → Output:** falha de save → toast de erro visível; sucesso → toast "Settings saved".

### RF-005: Key única no grid de skills
- **Description:** substituir `@key="skill.Name"` por `@key="skill.Path"` (diretório é único por skill) em `Skills.razor`.
- **Input → Output:** lista com skills de mesmo nome em sources diferentes → renderiza sem exceção.

### RF-006: Árvore de arquivos da skill no contrato
- **Description:** `SkillDetailDto` ganha `IReadOnlyList<SkillFileDto> Files` com `RelativePath` (sempre `/`-separado, relativo ao diretório da skill), `SizeBytes` e `IsText`. `ISkillDiscoveryService`/`SkillDiscoveryService` enumeram recursivamente os arquivos do diretório da skill (incluindo `SKILL.md`), ordenados por path.
- **Rules:** ignorar diretórios ocultos (`.git`, `.git` internos) e symlinks que resolvam fora do diretório da skill; cap de arquivos por skill (ex.: 500) para não inflar o payload.
- **Input → Output:** `GET /api/skills/taskboard/manage-taskboard` → `skill.files[]` contendo `SKILL.md` e demais arquivos do diretório.

### RF-007: Endpoint de conteúdo de arquivo de skill
- **Description:** novo `GET /api/skills/{source}/{name}/files/{**path}` retorna `200` com `{ path, content }` para arquivos de texto, `404` se skill/arquivo inexistente, `400` para path traversal (`..`, path absoluto), `415` para extensão não-whitelist, `413` para arquivo acima do limite (256 KB).
- **Rules:** resolver `Path.GetFullPath` dentro do diretório da skill e exigir prefixo do diretório; whitelist de extensões de texto: `.md .txt .json .yaml .yml .xml .csv .cs .fs .razor .cshtml .html .css .js .ts .py .sh .ps1 .toml .ini .cfg .sln .csproj .props .targets .gitignore .editorconfig .mjs .jsx .tsx`; sem auth adicional (consistente com os demais GETs `/api/skills`, já protegidos apenas pelo gate de rota do middleware).
- **Input → Output:** `GET .../files/references/x.md` → `{ "path": "references/x.md", "content": "..." }`.

### RF-008: UI da árvore no SkillDetailDialog
- **Description:** `SkillDetailDialog` renderiza seção "Files" com a árvore de `Skill.Files` (paths agrupados por diretório, indentados). Clique em arquivo de texto (`IsText`) carrega conteúdo sob demanda via `TaskboardClient.GetSkillFileContentAsync(source, name, path)` e exibe em `.code-block` dentro do diálogo; arquivo não-texto exibe hint "binary file".
- **Input → Output:** abrir detalhe de `manage-taskboard` → árvore com `SKILL.md` + demais arquivos; clicar em `references/...md` → conteúdo visível.

**Business rules / invariants:**
- Nenhum comportamento de endpoint existente muda (contratos são aditivos: `files` é campo novo; endpoints existentes intactos).
- Nenhum cookie/token pode aparecer em logs.
- Path de arquivo servido nunca escapa do diretório da skill.

## 5. API Contract (if applicable)

**Endpoint:** `GET /api/skills/{source}/{name}` (existente — resposta ampliada)
**Auth:** nenhuma (gate do middleware `/api/`)

**Response (success) — novo campo `files`:**
```json
{
  "skill": {
    "name": "manage-taskboard",
    "description": "...",
    "source": "taskboard",
    "path": "/app/skills/manage-taskboard",
    "tools": [],
    "references": null,
    "scripts": null,
    "content": "---\nname: manage-taskboard\n...",
    "files": [
      { "relativePath": "SKILL.md", "sizeBytes": 4210, "isText": true },
      { "relativePath": "references/commands.md", "sizeBytes": 1820, "isText": true }
    ]
  }
}
```

**Endpoint:** `GET /api/skills/{source}/{name}/files/{**path}` (novo)
**Auth:** nenhuma (gate do middleware `/api/`)

**Response (success):**
```json
{ "path": "references/commands.md", "content": "# Commands\n..." }
```

**Expected errors:** `400` traversal/invalid path, `404` skill ou arquivo inexistente, `413` arquivo > 256 KB, `415` extensão não-texto — sempre `{ "error": { "code": "...", "message": "..." } }`, sem PII.

## 6. Acceptance Criteria

- [ ] **Given** um repositório selecionado no Board **when** a página renderiza `KanbanBoard` **then** `RepositoryFullName` contém `owner/name` real e issues carregam sem "Repository name must be in the format".
- [ ] **Given** o usuário autenticado em qualquer página **when** clica num item da sidebar **then** o app navega para a rota; e em viewport `< lg` o offcanvas fecha após o clique.
- [ ] **Given** o usuário autenticado em Settings **when** clica "Save changes" **then** `PUT /api/settings` retorna sucesso e toast "Settings saved" aparece — nunca a mensagem do `ErrorBoundary`.
- [ ] **Given** senha atual válida **when** "Change password" **then** `PUT /api/admin/password` retorna sucesso e toast "Password changed"; dado 401 → mensagem de sessão/autorização, não "Current password is incorrect".
- [ ] **Given** 518 skills com 124 nomes duplicados **when** a página `/skills` monta no circuito interativo **then** o grid renderiza todos os cards sem exceção de key duplicada.
- [ ] **Given** `GET /api/skills/taskboard/manage-taskboard` **when** a skill tem arquivos além de `SKILL.md` **then** `files[]` lista todos com `relativePath`, `sizeBytes`, `isText`.
- [ ] **Given** `GET /api/skills/{s}/{n}/files/references/commands.md` válido **when** chamado **then** `200` com `content` do arquivo.
- [ ] **Given** path `../../etc/passwd` ou extensão `.png` ou arquivo > 256 KB **when** chamado o endpoint de arquivo **then** `400`/`415`/`413` respectivamente, sem conteúdo de arquivo.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Cookie ausente (sem auth) | request sem cookie | `TaskboardClient` envia sem header `Cookie`; endpoint protegido retorna 401 tratado com toast |
| Skill sem frontmatter válido | dir sem `SKILL.md` | não listada (comportamento atual preservado) |
| Symlink para fora do dir | `files` enum | entrada ignorada |
| `path` com `..` ou absoluto | `files/..%2F..%2Fetc` | `400` |
| Arquivo binário na árvore | `.png` na lista | mostrado na árvore, clique exibe "binary file" (não chama endpoint) |
| Skill sem arquivos extras | só `SKILL.md` | `files[]` contém só `SKILL.md`; árvore mostra 1 item |
| Offcanvas nunca aberto (desktop) | clique em nav | `getInstance` retorna null → no-op, navegação normal |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** reler os arquivos da seção 3 e confirmar que não há outros componentes com `Param="_campo"` sem `@` nem `data-bs-dismiss` em links (`grep -R '="_' src/Taskboard.Blazor` e `grep -R 'data-bs-dismiss' src/Taskboard.Blazor`).
- [ ] **T2 — Board binding:** aplicar `@` nos dois bindings (RF-001).
- [ ] **T3 — Sidebar:** remover dismiss attrs; adicionar helper JS `taskboard.closeSidebar()` e `@onclick` de fechamento no `NavMenu` (RF-002).
- [ ] **T4 — Auth forwarding:** `CircuitAuthContext` scoped + captura via `PersistentComponentState` + attach do header em `TaskboardClient` (RF-003); try/catch + toasts em Settings (RF-004).
- [ ] **T5 — Skills key:** `@key="skill.Path"` (RF-005).
- [ ] **T6 — Skills files API:** `SkillFileDto`, `Files` no `SkillDetailDto`, enumeração em `SkillDiscoveryService`, endpoint `files/{**path}` com guardas (RF-006/RF-007).
- [ ] **T7 — Skills tree UI:** seção Files + viewer no `SkillDetailDialog` + método no `TaskboardClient` (RF-008).
- [ ] **T8 — Tests:** testes de integração para `files` no detail, endpoint de arquivo (200/400/404/413/415); se viável, teste de unidade para `ExtractSections`/resolução de path.
- [ ] **T9 — Validation:** `dotnet build` (warnings as errors), `dotnet test`, checklist manual dos ACs de UI.
- [ ] **T10 — Done + PR:** DoD completo → `Status = Done` → PR na branch `fix/blazor-ui-critical-fixes`.

**7.1 Validation strategy**

Bugfix (.NET): teste de reprodução/regressão para os endpoints alterados/novos; `dotnet build` sem warnings; `dotnet test` verde; gate de cobertura existente (45%) não pode regredir. UI Blazor validada manualmente contra a seção 6 (sem bUnit neste ciclo).

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** nunca commitar em `main`, `master` ou `develop`. Branch aprovada: `fix/blazor-ui-critical-fixes`.
- **Workflows:** não modificar `.github/workflows/` (protegido).
- **Security:** não logar cookie/token; secrets via env/config; nada de `.env` em commit; endpoint de arquivo com traversal guard obrigatório.
- **Scope:** não inventar requisitos além dos RF-001..RF-008; parar e perguntar em ambiguidade.
- **Architecture:** regra de negócio fora de componentes; resolução de arquivos permanece em `Taskboard.Integrations`.
- **Specs:** mudança de contrato (`files`, endpoint novo) já refletida aqui; manter `docs/` coerente se rotas mudarem (não mudam).

## 9. Definition of Done

- [ ] Todos os requisitos (seção 4) implementados.
- [ ] Todos os ACs (seção 6) cobertos por testes ou evidência manual documentada.
- [ ] Edge cases tratados.
- [ ] `dotnet build` limpo (TreatWarningsAsErrors), `dotnet test` verde, cobertura sem regressão.
- [ ] Guardrails da seção 8 respeitados.
- [ ] Logs sem cookies/tokens; erros no formato genérico `{ error: { code, message } }`.

**Next action after DoD is complete:** `Status = Done` e abrir PR em `fix/blazor-ui-critical-fixes`.

## Open Questions / Pending Ambiguity

- Nenhuma — aprovado pelo usuário: SPEC único, cookie forwarding (b), árvore + visualização de conteúdo, validação por testes de integração + checklist manual.
