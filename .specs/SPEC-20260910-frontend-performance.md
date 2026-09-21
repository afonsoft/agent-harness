# SPEC-20260910: Melhorias de Performance e UX do Frontend

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Frontend Performance & UX Improvements |
| Product / System | agent-harness |
| Module / Bounded Context | Presentation (Blazor / Static Fallback) |
| Change type | Enhancement |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-frontend-ux-performance` |
| Technical owner | afonsoft |
| Status | Done (framework superseded: MudBlazor/Tailwind → Bootstrap 5.3 + Blazor.Bootstrap per SPEC-20260913-bootstrap-modernization) |
| Date | 2026-09-10 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O frontend Blazor e o fallback estático já estão funcionais (`SPEC-008` implementado), mas a análise de gap revelou oportunidades claras de performance e experiência do usuário:

- `BoardView.razor` carrega todas as tarefas em memória e enumera `_tasks` várias vezes por render.
- Não há virtualização para boards com grande volume de cards.
- Estados de loading e erro são textos simples, sem skeleton ou boundaries.
- CSS customizado misturado com estilos MudBlazor sem tema centralizado.
- Recursos estáticos (`wwwroot`) não usam compressão explícita.
- Alguns componentes renderizam mais vezes do que o necessário porque não controlam re-render.

### Objective

Melhorar a performance percebida, a estabilidade em listas grandes e a acessibilidade/responsividade do frontend Blazor, mantendo os contratos HTTP e a arquitetura ABP N-Layer existentes.

### Expected outcome

- Renderização suave em listas grandes com virtualização.
- Menos ciclos de render por mudança de estado.
- Estados de carregamento/erro consistentes com MudBlazor.
- Layout responsivo e acessível.
- Assets estáticos servidos com compressão e cache.

### Out of scope

- Reescrita da UI para outra tecnologia (React, MAUI, etc.).
- Mudanças no domínio, API ou contratos.
- Sincronização em tempo real além do SSE já implementado.

---

## 2. Agent Role

> Frontend/Blazor engineer focado em performance, acessibilidade e responsividade.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não modificar rotas `/api` nem contratos HTTP.
- Preservar autenticação e antiforgery.
- Não adicionar dependências sem justificativa no PR.

---

## 4. Product Context

### Functional context

A UI exibe board de tarefas local (`BoardView`), board Kanban do GitHub (`KanbanBoard`), chat de IA, login e menus. A maioria dos dados vem da API REST e o real-time vem de SSE/SignalR.

### Technical context

- `Taskboard.Blazor` (Razor Class Library) com `MudBlazor`.
- `Taskboard.Server` serve Blazor via `MapRazorComponents` e fallback estático.
- `wwwroot/css/site.css` com estilos customizados.
- MudBlazor já é dependência central.

### Relevant files

- `src/Taskboard.Blazor/Components/BoardView.razor`
- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor`
- `src/Taskboard.Blazor/Components/TaskCard.razor`
- `src/Taskboard.Blazor/Layout/MainLayout.razor`
- `src/Taskboard.Blazor/App.razor`
- `src/Taskboard.Blazor/Routes.razor`
- `src/Taskboard.Server/wwwroot/css/site.css`
- `src/Taskboard.Server/Program.cs`

---

## 5. Task Definition

### Main task

Otimizar o frontend Blazor e o fallback estático para melhorar performance, acessibilidade e responsividade.

### Subtasks

1. Virtualizar listas de tarefas e issues.
2. Reduzir re-renderizações desnecessárias.
3. Padronizar estados de carregamento, erro e vazio.
4. Centralizar tema MudBlazor e responsividade.
5. Ativar compressão e cache de assets estáticos.

### Do not do

- Não reescrever componentes para outra biblioteca.
- Não alterar regras de domínio.
- Não expor credenciais no client.

---

## 6. Functional Requirements

### FR-001: Virtualização de listas

`BoardView` e `KanbanBoard` devem usar `<Virtualize>` quando renderizarem listas que podem passar de 30 itens. A altura/largura de linha e a chave (`@key`) devem ser fornecidas para evitar reconciliação custosa.

**Métrica de sucesso:** renderização fluida com 100+ cards por coluna sem drop de frames perceptível.

### FR-002: Redução de re-renderizações

- `GetTasks(string)` deve ser substituído por uma coleção pré-materializada `IReadOnlyList<TaskDto>` por status, calculada uma vez no `OnInitializedAsync` ou em um `Memoized` local.
- `TaskCard` deve herdar de `ComponentBase` e, se necessário, implementar `ShouldRender` para ignorar atualizações em que `Task` não mudou (usar `SetParametersAsync` + `Equals` se `TaskDto` for imutável).
- `KanbanBoard` deve evitar recriar `_columns` do zero em `OnParametersSetAsync` quando `RepositoryFullName` não mudou.

### FR-003: Estados de carregamento, erro e vazio

- Substituir textos `<p>Loading project...</p>` e `<MudText>Carregando issues...</MudText>` por componentes MudBlazor (`MudProgressLinear`, `MudSkeleton`, `MudOverlay`).
- Adicionar `ErrorBoundary` em `Routes.razor` ou `MainLayout.razor` para capturar exceções de componente.
- Exibir estado vazio com `MudText` e ícone quando não houver tarefas/issues.

### FR-004: Tema e responsividade

- Definir um `MudTheme` compartilhado em `MainLayout` ou `Program.cs` com paleta primária, secundária e modo escuro opcional.
- Usar `MudLayout`, `MudAppBar`, `MudDrawer` para responsividade móvel (colapsar `NavMenu`).
- Manter o drag-and-drop Kanban funcional, garantindo áreas de drop acessíveis.

### FR-005: Acessibilidade

- Adicionar `aria-label` em botões de ação, drag handles e links do `NavMenu`.
- Garantir contraste mínimo de 4.5:1 no CSS (`kanban-card`, `task-card`).
- `NavLink` e `MudButton` devem ser navegáveis por teclado.

### FR-006: Otimização de recursos estáticos

- No `Taskboard.Server`, adicionar `AddResponseCompression` e `UseResponseCompression` antes de `UseStaticFiles`.
- Usar `MapStaticAssets` (já presente) com cache-headers adequados para `css`, `js` e imagens.
- Servir `MudBlazor.min.css` e `MudBlazor.min.js` com compressão Brotli/Gzip.

---

## 7. Business Rules

- Não alterar contratos HTTP nem autenticação.
- Preservar drag-and-drop do Kanban.
- Manter fallback para `index.html` em rotas não-API.

---

## 8. Domain Modeling

N/A. UI consome DTOs existentes.

---

## 9. Expected Architecture

```text
src/Taskboard.Blazor/
  Components/
    BoardView.razor          # usa <Virtualize>, loading/erro/vazio
    GitHub/KanbanBoard.razor # virtualização, memoização de colunas
    TaskCard.razor           # controle de ShouldRender
    Shared/
      TaskboardTheme.razor   # provider de MudTheme (ou em Program.cs)
      Loading.razor          # componente de loading reutilizável
      EmptyState.razor       # componente de estado vazio
  Layout/
    MainLayout.razor         # MudLayout, MudDrawer responsivo
  Services/
    TaskboardClient.cs       # reutiliza HttpClient, tokens de cancelamento
src/Taskboard.Server/
  Program.cs               # response compression, cache headers
```

---

## 10. API Contracts

N/A. Este SPEC não altera contratos HTTP.

---

## 11. Application Contracts

N/A. Os DTOs existentes (`ProjectDto`, `TaskDto`, `IssueDto`) continuam os mesmos.

---

## 12. Persistence and Data

N/A.

---

## 13. Integrations

- MudBlazor (já instalado).
- Taskboard HTTP API (sem mudanças).

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Board vazio | projeto sem tarefas | exibir `EmptyState` |
| Carregamento lento | rede lenta | skeleton/linear progress visível |
| Erro de API | `GitHubService` indisponível | `MudAlert` e `ErrorBoundary` |
| Lista muito grande | 500+ issues | `<Virtualize>` mantém performance |
| Mudança de tema | usuário alterna dark/light | aplica sem recarregar página |

---

## 15. Few-Shot Examples

```csharp
// MudTheme centralizado
public static class TaskboardTheme
{
    public static MudTheme Default => new()
    {
        PaletteLight = new PaletteLight { Primary = "#1b6ec2", AppbarBackground = "#1b1b1b" },
        PaletteDark = new PaletteDark { Primary = "#58a6ff", AppbarBackground = "#0d1117" }
    };
}
```

```razor
@* BoardView com virtualização *@
<div class="column" style="height: 80vh; overflow-y: auto;">
    <Virtualize Items="_tasksByStatus[status]" Context="task">
        <TaskCard Task="task" />
    </Virtualize>
</div>
```

---

## 16. Non-Functional Requirements

### Performance

- P95 de renderização < 16ms para interações comuns.
- Time-to-first-contentful-paint < 2s em local.

### Accessibility

- WCAG 2.1 nível AA.

### Responsiveness

- Funcional em telas de 360px a 4K.

---

## 17. Mandatory Guardrails

- Não expor credenciais no client.
- Não quebrar antiforgery.
- Não adicionar dependências sem justificativa.
- Não alterar domínio ou API.

---

## 18. Expected Tests

### Unit tests

| Component / Service | Scenarios |
|---|---|
| `BoardView` | loading, vazio, erro, virtualização |
| `KanbanBoard` | memoização de colunas, drop |
| `TaskCard` | não re-renderiza quando `Task` não muda |

### Integration tests

| Flow | Validation |
|---|---|
| GET `/` | retorna HTML com Blazor e assets comprimidos |
| GET `/css/site.css` | header `Content-Encoding` gzip/brotli |
| Drag-and-drop Kanban | eventos mantêm sincronização |

---

## 19. Acceptance Criteria

- [x] `<Virtualize>` aplicado em `BoardView` e `KanbanBoard`.
- [x] `TaskCard` evita re-render quando parâmetro inalterado.
- [x] Loading/erro/vazio usam componentes compartilhados (`Components/Shared/Loading.razor`, `EmptyState.razor`) — MudBlazor removido, substituído por Blazor.Bootstrap.
- [x] Tema centralizado via Bootstrap 5.3 (`data-bs-theme`/`--bs-*`) com sidebar offcanvas responsiva — substitui `MudTheme`.
- [x] `ErrorBoundary` protege rotas.
- [x] Response compression ativa para assets estáticos.
- [x] `dotnet build` e `dotnet test` passam sem warnings.

---

## 20. Implementation Plan

1. Virtualizar `BoardView` e `KanbanBoard`.
2. Refatorar `TaskCard` e `KanbanBoard` para reduzir re-render.
3. Criar componentes `Loading` e `EmptyState`.
4. Centralizar `MudTheme` e responsividade em `MainLayout`.
5. Adicionar `ErrorBoundary`.
6. Configurar response compression e cache headers no `Taskboard.Server`.
7. Escrever/adaptar testes de componente.
8. Validar build e testes.

---

## 21. Rollback Strategy

- Reverter mudanças nos arquivos `*.razor` e `site.css`.
- Restaurar `Program.cs` sem response compression.
- Testar `dotnet test` para garantir que nenhum teste quebrou.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Virtualize quebra drag-and-drop | Médio | Média | testar interação nativa após virtualização |
| MudBlazor theme conflita com CSS customizado | Médio | Média | migrar estilos gradualmente, não deletar tudo |
| Compressão adiciona latência em dev | Baixo | Baixa | ativar apenas em `Production` |

---

## 23. Definition of Done

- [ ] SPEC revisado.
- [ ] Implementação segue o SPEC.
- [ ] Testes criados ou atualizados.
- [ ] Build sem warnings.
- [ ] Documentação (README/docs) atualizada se houver mudança no setup.
- [ ] PR descreve mudanças e evidências de performance.

---

## 24. Key Reminder

> The SPEC is the contract. Do not expand scope beyond frontend performance and UX.

---

## Pending Questions

1. O projeto deseja oferecer tema escuro persistente (salvo em localStorage/preference)?
2. Qual a lista máxima esperada de tarefas por projeto para definir o `OverscanCount` do `Virtualize`?
3. O drag-and-drop Kanban usa HTML5 nativo; queremos migrar para uma biblioteca (ex: `SortableJS`) ou manter?

---

## Human Approval Checklist

- [ ] Escopo claro (apenas Blazor/estáticos, sem mudar API).
- [ ] Requisitos testáveis.
- [ ] Riscos e mitigações aceitáveis.
