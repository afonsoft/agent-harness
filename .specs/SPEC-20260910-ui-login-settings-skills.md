# SPEC-20260910: Telas de Login, Configurações e Skills

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | UI Login, Settings and Skills |
| Product / System | taskboard-ai |
| Module / Bounded Context | Presentation |
| Change type | Feature |
| Repository | afonsoft/taskboard-ai |
| Suggested branch | `devin/spec-ui-login-settings-skills` |
| Technical owner | afonsoft |
| Status | Done (framework superseded: MudBlazor → Bootstrap 5.3 + Blazor.Bootstrap; dark/light toggle removido por pedido — tema light-only) |
| Date | 2026-09-10 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O `Login.razor` atual é um formulário HTML puro sem theme provider, sem estados visuais e sem suporte a dark/light. Não existe uma tela central de configurações para ajustar preferências, listar agentes/CLIs habilitados ou explorar skills instaladas. A gestão de preferências (tema, token, CLIs) está dispersa em env vars e `~/.config/taskctl/settings.json`.

### Objective

Criar três telas Blazor usando MudBlazor:

1. `Login.razor` redesenhado com tema dark/light, validação e anti-forgery.
2. `/settings` página autenticada com abas: Geral, Agentes e Variáveis.
3. `/skills` página autenticada (ou pública) que lista todas as skills disponíveis com nome e descrição.

As preferências do usuário (tema, CLIs habilitados, `GITHUB_TOKEN`) serão persistidas no SQLite via EF Core.

### Expected outcome

- Login visualmente consistente com o restante da aplicação.
- Usuário pode alternar entre dark e light, persistindo a escolha.
- Tela de configurações exibe agentes descobertos via `IAgentDiscoveryService` e permite habilitar/desabilitar cada um.
- Tela de skills lista skills do repositório e dos diretórios de agentes com descrição extraída do frontmatter do `SKILL.md`.

### Out of scope

- Gerenciamento de múltiplos usuários.
- Edição de `TASKBOARD_ADMIN_USERNAME` e `TASKBOARD_ADMIN_PASSWORD` pela UI (senha nunca exposta).
- Sincronização de preferências entre dispositivos.
- Mobile nativo.

---

## 2. Agent Role

> Frontend/Blazor engineer com foco em UX, MudBlazor e integração com ABP Minimal APIs.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não modificar contratos HTTP sem versionar.
- Não expor senhas, tokens ou connection strings no client.
- Não modificar `.github/workflows` sem aprovação humana.

---

## 4. Product Context

### Functional context

A UI oferece login, board, Kanban GitHub, chat de IA e a partir deste SPEC passa a oferecer configurações e descoberta de skills. O admin autenticado gerencia preferências e agentes; a tela de skills pode ser pública ou autenticada.

### Technical context

- Blazor Server com `MudBlazor`.
- Autenticação via cookies (`CookieAuthenticationDefaults.AuthenticationScheme`).
- `IAgentDiscoveryService` já descobre devin, claude, codex, opencode e openhands no PATH.
- `TaskboardDbContext` (SQLite/EF Core) para persistência das preferências.
- Skills são distribuídas em `skills/manage-taskboard` e copiadas para `~/.devin/skills`, `~/.claude/skills`, `~/.cursor/skills`, `~/.opencode/skills`, `~/.gemini/skills` e `~/.github/skills`.

### Relevant files

- `src/Taskboard.Blazor/Components/Pages/Login.razor`
- `src/Taskboard.Blazor/Layout/MainLayout.razor`
- `src/Taskboard.Blazor/Components/BoardView.razor`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Integrations/Agents/AgentDiscoveryService.cs`
- `src/Taskboard.Application.Contracts/Agents/IAgentDiscoveryService.cs`
- `src/Taskboard.EntityFrameworkCore/TaskboardDbContext.cs`
- `skills/manage-taskboard/SKILL.md`
- `install.sh`

---

## 5. Task Definition

### Main task

Criar telas de login, configurações e skills no `Taskboard.Blazor`, persistindo preferências em SQLite/EF Core.

### Subtasks

1. Redesenhar `Login.razor` com MudBlazor e toggle dark/light.
2. Criar a entidade `UserPreference` e `AgentPreference` no Domain/EF Core.
3. Criar endpoints REST para CRUD de preferências e listagem de skills.
4. Criar `SkillDiscoveryService` para varrer diretórios e extrair `name` e `description` de `SKILL.md`.
5. Criar `Settings.razor` com abas Geral, Agentes e Variáveis.
6. Criar `Skills.razor` para listar skills com descrição.
7. Atualizar `MainLayout.razor` com `MudTheme` e `MudThemeProvider` vinculado ao `UserPreference`.

### Do not do

- Não reescrever o backend de autenticação.
- Não persistir a senha admin no banco de preferências.
- Não instalar/gerenciar skills (apenas visualização).

---

## 6. Functional Requirements

### FR-001: Tela de Login redesenhada

- Substituir `Login.razor` para usar componentes MudBlazor (`MudTextField`, `MudButton`, `MudCard`, `MudToggleIconButton` para theme).
- Manter `method="post" action="/api/login"` com anti-forgery token.
- Exibir erro de credenciais com `MudAlert`.
- Suportar dark/light antes da autenticação, lendo preferência do `localStorage`/OS fallback.

### FR-002: Tema Dark/Light

- Definir `MudTheme` padrão em `MainLayout.razor` ou em `Program.cs`.
- Persistir `Theme` (string: `dark` | `light`) em `UserPreference` no SQLite.
- Aplicar tema imediatamente após login e permitir toggle na top bar.

### FR-003: Página `/settings`

Página autenticada com três abas:

**Aba Geral:**
- Toggle dark/light.
- `GITHUB_TOKEN` (input de password com opção de mostrar/ocultar).
- `TASKBOARD_ADMIN_USERNAME` (somente leitura).

**Aba Agentes:**
- Listar agentes retornados por `IAgentDiscoveryService.DiscoverAsync()`.
- Cada linha mostra nome, caminho, versão, status (`Available` / `Unavailable`) e toggle enable.
- Persistir `Enabled` por agente na tabela `AgentPreference`.

**Aba Variáveis:**
- Apenas `GITHUB_TOKEN` editável.
- `TASKBOARD_URL`, `TASKBOARD_DATA_DIR` e `TASKBOARD_PORT` não aparecem (removidos por decisão do usuário).

### FR-004: Persistência SQLite/EF Core

- Criar entidade `UserPreference` (Id, Theme, GitHubToken).
- Criar entidade `AgentPreference` (Id, AgentType, Enabled).
- Criar repositórios `IRepository<UserPreference>` e `IRepository<AgentPreference>`.
- Aplicar EF Core migration `AddUserAndAgentPreferences`.

### FR-005: Tela `/skills`

- Varre diretórios:
  - `skills/manage-taskboard` (repositório).
  - `~/.devin/skills`
  - `~/.claude/skills`
  - `~/.cursor/skills`
  - `~/.opencode/skills`
  - `~/.gemini/skills`
  - `~/.github/skills`
- Para cada skill, ler `SKILL.md` e extrair frontmatter `name` e `description`.
- Exibir tabela/cards: `Nome`, `Descrição`, `Origem` (qual IDE/CLI), `Caminho`.
- Permitir busca por nome ou descrição.

### FR-006: Responsividade e acessibilidade

- Layout funcional em mobile e desktop.
- Uso de `aria-label` em toggles e campos.
- Contrastes mínimos WCAG AA.

---

## 7. Business Rules

- Senhas e tokens nunca são renderizados em texto claro (a menos que o usuário clique em "revelar").
- Apenas usuários autenticados acessam `/settings`.
- `/skills` pode ser pública ou autenticada; a SPEC mantém pública por padrão (somente leitura de metadados).
- Preferências de agentes são por servidor (uma única instalação), não por usuário.
- `IAgentDiscoveryService` continua sendo a fonte de verdade para status de agentes.

---

## 8. Domain Modeling

### UserPreference

```csharp
public sealed class UserPreference : Entity<Guid>
{
    public string Theme { get; set; } = "dark";
    public string? GitHubToken { get; set; }
}
```

### AgentPreference

```csharp
public sealed class AgentPreference : Entity<Guid>
{
    public AgentType AgentType { get; set; }
    public bool Enabled { get; set; } = true;
}
```

### SkillDescriptor (read model)

```csharp
public sealed class SkillDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // repo, devin, claude, etc.
    public string Path { get; set; } = string.Empty;
}
```

---

## 9. Expected Architecture

```text
src/Taskboard.Blazor/
  Components/Pages/
    Login.razor             # redesenhado
    Settings.razor          # abas Geral, Agentes, Variáveis
    Skills.razor            # listagem de skills
  Shared/
    ThemeToggle.razor       # botão dark/light
    SkillCard.razor         # card de uma skill
  Layout/
    MainLayout.razor        # MudTheme + ThemeToggle
  Services/
    UserPreferenceClient.cs # consome /api/settings
    SkillClient.cs          # consome /api/skills
src/Taskboard.Application/
  Settings/
    GetUserPreferencesQuery.cs
    UpdateUserPreferencesCommand.cs
    GetAgentPreferencesQuery.cs
    UpdateAgentPreferenceCommand.cs
    ListAvailableSkillsQuery.cs
  Settings/
    UserPreferenceDto.cs
    AgentPreferenceDto.cs
    SkillDto.cs
src/Taskboard.Server/
  Program.cs                # endpoints /api/settings e /api/skills
  Services/
    SkillDiscoveryService.cs
src/Taskboard.Domain/
  UserPreference.cs
  AgentPreference.cs
src/Taskboard.EntityFrameworkCore/
  Configurations/
    UserPreferenceConfiguration.cs
    AgentPreferenceConfiguration.cs
```

---

## 10. API Contracts

### Settings

```http
GET    /api/settings/preferences
PUT    /api/settings/preferences
GET    /api/settings/agents
PUT    /api/settings/agents/{agentType}
```

### Skills

```http
GET    /api/skills
```

### Request/Response shapes

```json
// GET /api/settings/preferences
{
  "theme": "dark",
  "gitHubToken": "ghp_***"
}

// PUT /api/settings/preferences
{
  "theme": "light",
  "gitHubToken": "ghp_***"
}

// GET /api/settings/agents
[
  { "agentType": "Claude", "name": "claude", "executablePath": "/...", "version": "0.24.0", "enabled": true }
]

// GET /api/skills
[
  { "name": "manage-taskboard", "description": "...", "source": "claude", "path": "~/.claude/skills/manage-taskboard" }
]
```

---

## 11. Application Contracts

```csharp
public sealed record GetUserPreferencesQuery : IRequest<UserPreferenceDto>;
public sealed record UpdateUserPreferencesCommand(string Theme, string? GitHubToken) : IRequest;
public sealed record GetAgentPreferencesQuery : IRequest<IReadOnlyList<AgentPreferenceDto>>;
public sealed record UpdateAgentPreferenceCommand(AgentType AgentType, bool Enabled) : IRequest;
public sealed record ListAvailableSkillsQuery : IRequest<IReadOnlyList<SkillDto>>;
```

---

## 12. Persistence and Data

- Adicionar `UserPreferences` e `AgentPreferences` ao `TaskboardDbContext`.
- EF Core migration: `AddLoginSettingsSkills`.
- Seed opcional para `AgentPreference` com todos os agentes habilitados por padrão.

---

## 13. Integrations

- MudBlazor (já instalado).
- `IAgentDiscoveryService` (já existente).
- `MudBlazor.Icons.Material` para ícones.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Banco vazio | primeira execução | cria `UserPreference` padrão (dark) e `AgentPreference` para cada agente conhecido |
| Agent não encontrado | `claude` ausente no PATH | mostra `Unavailable` e desabilita toggle ou permite habilitar com aviso |
| `SKILL.md` sem frontmatter | skill mal formada | exibe nome da pasta e descrição "[A DEFINIR]" |
| Token vazio | `GITHUB_TOKEN` removido | salva `null` e desabilita integração GitHub |
| Tema inválido | string inesperada | fallback para `dark` |
| Não autenticado | acesso a `/settings` | 302 para `/login` |

---

## 15. Few-Shot Examples

```csharp
// Command
public sealed record UpdateUserPreferencesCommand(string Theme, string? GitHubToken) : IRequest;

// Endpoint
app.MapPut("/api/settings/preferences", async (
    [FromBody] UpdateUserPreferencesCommand command,
    [FromServices] IMediator mediator,
    CancellationToken ct) =>
{
    await mediator.Send(command, ct);
    return Results.NoContent();
});
```

```razor
@* Theme toggle *@
<MudToggleIconButton Toggled="@_isDark"
                     ToggledChanged="OnToggledChanged"
                     Icon="@Icons.Material.Filled.DarkMode"
                     ToggledIcon="@Icons.Material.Filled.LightMode" />
```

---

## 16. Non-Functional Requirements

### Performance

- P95 < 300ms para carga de `/settings` e `/skills`.
- Descoberta de skills < 2s para < 50 skills.

### Accessibility

- WCAG 2.1 AA.
- Todos os inputs com labels.
- Mensagens de erro associadas a campos.

### Responsiveness

- Funcional em 360px a 4K.
- Abas convertem para stepper em mobile.

---

## 17. Mandatory Guardrails

- Nunca logar `GITHUB_TOKEN` ou `Admin:Password`.
- Não retornar token em texto plano para o client (mascarar como `ghp_***`).
- Não colocar regras de negócio em endpoints/controllers.
- Não alterar autenticação sem spec dedicada.

---

## 18. Expected Tests

### Unit tests

| Class | Scenarios |
|---|---|
| `UserPreference` | tema padrão, validação |
| `AgentPreference` | enable/disable |
| `SkillDiscoveryService` | extração de frontmatter, skills ausentes |

### Integration tests

| Flow | Validation |
|---|---|
| PUT `/api/settings/preferences` | atualiza no SQLite |
| GET `/api/settings/agents` | retorna agentes com `Enabled` |
| GET `/api/skills` | retorna skills com `Name` e `Description` |
| `Login.razor` | renderiza com anti-forgery token |

---

## 19. Acceptance Criteria

- [x] `Login.razor` usa Bootstrap 5.3 (MudBlazor removido); dark/light toggle removido — tema light-only por decisão de 2026-09-13.
- [x] `/settings` acessível apenas após login.
- [x] Tema persistido em `UserPreference` no SQLite (sempre `"light"`).
- [x] Toggle de agentes persistido em `AgentPreference`.
- [x] `GITHUB_TOKEN` editável e mascarado (input password + toggle Show/Hide).
- [x] `/skills` lista skills com nome e descrição.
- [x] `dotnet build` e `dotnet test` passam.

---

## 20. Implementation Plan

1. Criar `UserPreference` e `AgentPreference` no Domain.
2. Configurar EF Core e gerar migration.
3. Criar `SkillDiscoveryService` e queries de aplicação.
4. Criar endpoints `/api/settings` e `/api/skills`.
5. Redesenhar `Login.razor` com MudBlazor.
6. Criar `Settings.razor` e `Skills.razor`.
7. Integrar `MudTheme` e `ThemeToggle` no `MainLayout`.
8. Escrever testes.
9. Validar build e testes.

---

## 21. Rollback Strategy

- Reverter migration e entidades.
- Restaurar `Login.razor` original.
- Remover `Settings.razor`, `Skills.razor` e endpoints.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Leitura de `SKILL.md` lenta | Médio | Média | cachear em memória por 5 minutos |
| `GITHUB_TOKEN` vazado no client | Alto | Baixa | mascara no DTO e usa input password |
| Tema pisca no carregamento | Médio | Média | carrega preferência via cookie/localStorage antes do render |
| EF migration conflita com schema | Médio | Baixa | migration idempotente e testada |

---

## 23. Definition of Done

- [ ] SPEC aprovado (`Status: Approved`).
- [ ] Implementação segue o SPEC.
- [ ] Testes automatizados criados.
- [ ] Build sem warnings.
- [ ] Documentação atualizada (`docs/installation.md` se necessário).
- [ ] PR com screenshots.

---

## 24. Key Reminder

> The SPEC is the contract. Do not expand scope beyond UI de login, configurações e listagem de skills.

---

## Pending Questions

1. A tela `/skills` deve ser pública ou exigir autenticação?
2. A lista de skills deve incluir diretórios `~/.gemini/antigravity-cli/skills` e `~/.cognition/skills`?
3. `GITHUB_TOKEN` deve ser validado com chamada à API do GitHub ao salvar?
4. O tema deve respeitar preferência do sistema (`prefers-color-scheme`) por padrão?

---

## Human Approval Checklist

- [ ] Telas e abas claras.
- [ ] Persistência e segurança do token aceitáveis.
- [ ] Escopo sem variáveis de servidor (URL, data dir, port) é o desejado.
