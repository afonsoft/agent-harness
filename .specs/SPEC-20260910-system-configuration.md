# SPEC-20260910: Melhorias de Configuração do Sistema

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | System Configuration Improvements |
| Product / System | taskboard-ai |
| Module / Bounded Context | Infrastructure / Host |
| Change type | Enhancement |
| Repository | afonsoft/taskboard-ai |
| Suggested branch | `devin/spec-system-configuration` |
| Technical owner | afonsoft |
| Status | `Done` (status corrigido — implementado e issues fechadas; reconciliado em 2026-09-17) |
| Date | 2026-09-10 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

A análise de gap no `Taskboard.Server` e projetos satélites mostrou que o host ASP.NET Core é configurado de forma mínima, confiando principalmente em helpers estáticos e variáveis de ambiente:

- Não há `appsettings.json` estruturado; conexão SQLite e credenciais admin são montadas programaticamente.
- `TaskboardEnvironment` é estático e dificulta testes unitários.
- Não há `IOptions<T>` com validação para configurações.
- Não há response compression, output cache, rate limiting, HSTS, localization nem health checks avançados.
- A ordem dos middlewares em `Program.cs` não segue o pipeline recomendado do ASP.NET Core (ex: `UseStaticFiles` antes de `UseAuthentication`, antiforgery após middleware customizado).
- O middleware pipeline não declara `UseRouting` explicitamente.

### Objective

Tornar a configuração do `Taskboard.Server` estruturada, validada, segura e performática, usando APIs nativas do ASP.NET Core sem adicionar dependências externas.

### Expected outcome

- `appsettings.json` e `appsettings.{Environment}.json` com esquema claro.
- `IOptions<TaskboardOptions>` e `IOptions<AdminOptions>` validados no startup.
- Response compression e output cache ativos.
- Health checks com endpoints `/health`, `/health/ready` e `/health/live`.
- Rate limiting em login e API.
- Pipeline de middleware auditado e corrigido.

### Out of scope

- Mudar banco para PostgreSQL/SQL Server (coberto em `SPEC-011` se necessário).
- Implementar OpenTelemetry/observabilidade avançada (fora do escopo deste SPEC).
- Alterar CI/CD workflows (protegido por hard rule).

---

## 2. Agent Role

> Backend/ASP.NET Core engineer focado em configuração, middleware e performance do host.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não modificar `.github/workflows` sem aprovação humana.
- Não expor secrets, tokens ou connection strings em logs.
- Não alterar contratos HTTP sem versionar/documentar breaking change.

---

## 4. Product Context

### Functional context

O `Taskboard.Server` hospeda Minimal APIs, Blazor Server, SignalR hub, SSE e fallback estático. É o ponto central de configuração para URLs, dados, admin, CORS e middleware.

### Technical context

- ASP.NET Core 10 Minimal APIs.
- `WebApplication.CreateBuilder(args)`.
- `TaskboardEnvironment` e `AdminUser.CreateFromConfiguration` carregam env vars.
- `Program.cs` registra serviços e middleware.

### Relevant files

- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Server/Services/AdminUser.cs`
- `src/Taskboard.Application.Contracts/Configuration/TaskboardEnvironment.cs`
- `Directory.Build.props`
- `Directory.Packages.props`

---

## 5. Task Definition

### Main task

Refatorar a configuração e o pipeline do `Taskboard.Server` para usar opções tipadas, validação, compressão, cache, rate limiting e health checks.

### Subtasks

1. Criar `appsettings.json` e `appsettings.{Environment}.json`.
2. Definir `TaskboardOptions` e `AdminOptions` com `IOptions<T>`.
3. Refatorar `TaskboardEnvironment` e `AdminUser` para usar `IOptions`/`IConfiguration`.
4. Adicionar response compression, output cache, rate limiting e request localization.
5. Adicionar health checks com EF Core e disco.
6. Auditar e corrigir ordem dos middlewares.

### Do not do

- Não adicionar serviços de cloud (Key Vault, App Configuration) sem novo SPEC.
- Não modificar workflows do GitHub Actions.
- Não alterar os contratos das rotas públicas.

---

## 6. Functional Requirements

### FR-001: Configuration files

Criar em `src/Taskboard.Server/`:

- `appsettings.json` com seções `Taskboard`, `Admin`, `Logging`, `AllowedHosts`.
- `appsettings.Development.json` com valores de dev.
- `appsettings.Production.json` com valores otimizados para produção.

Exemplo:

```json
{
  "Taskboard": {
    "Port": 47823,
    "DataDir": ".data",
    "Database": {
      "ConnectionStringName": "Taskboard"
    }
  },
  "Admin": {
    "Username": "admin"
    // "Password" vindo de env ou user-secrets
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### FR-002: Strongly typed options

- Criar `TaskboardOptions` em `Taskboard.Application.Contracts/Configuration` (ou `Taskboard.Server` se for host-only):
  - `Port`, `DataDir`, `Database:ConnectionStringName`.
- Criar `AdminOptions` em `Taskboard.Server/Configuration`:
  - `Username`, `Password` (obrigatória em produção via validação).
- Registrar com `services.Configure<TaskboardOptions>(...)` e `services.Configure<AdminOptions>(...)`.
- Adicionar `ValidateOnStart` para ambos.

### FR-003: Refatorar `TaskboardEnvironment`

- Tornar `TaskboardEnvironment` um serviço resolvido de `IOptions<TaskboardOptions>` ou `IConfiguration`.
- Manter a compatibilidade com env vars (usar `IConfiguration` para que env sobreponha `appsettings`).
- Remover a natureza estática para permitir testes com `IConfiguration` mockada.

### FR-004: Response compression

- `builder.Services.AddResponseCompression(options => ...)` com Brotli e Gzip.
- `app.UseResponseCompression()` antes de `UseStaticFiles`.
- Aplicar aos MIME types de `text/css`, `application/javascript`, `text/html`, `application/json`.

### FR-005: Output cache

- `builder.Services.AddOutputCache()`.
- `app.UseOutputCache()`.
- Políticas:
  - `StaticAssets` para arquivos de `wwwroot` (1 dia).
  - `ReadOnlyApi` para `GET /api/meta`, `GET /health` (curto, 60s).
  - Nunca cachear endpoints de autenticação, SSE e SignalR.

### FR-006: Health checks

- `builder.Services.AddHealthChecks()`
  - Implementar `TaskboardDbContextHealthCheck` que executa `dbContext.Database.CanConnectAsync(CancellationToken)` (sem pacote NuGet adicional).
  - `AddCheck("data-directory", ...)`
- Mapear:
  - `/health` — rápido.
  - `/health/ready` — dependências (banco).
  - `/health/live` — alive.

### FR-007: Rate limiting

- `builder.Services.AddRateLimiter(options => ...)`.
- Políticas:
  - `login`: 5 requisições por minuto no endpoint `POST /api/login`.
  - `api`: 100 requisições por minuto por IP no prefixo `/api`.
- `app.UseRateLimiter()` após `UseRouting` e antes dos endpoints.

### FR-008: Request localization

- `builder.Services.AddRequestLocalization(options => ...)` com `pt-BR` e `en-US`.
- `app.UseRequestLocalization()` no início do pipeline.

### FR-009: Middleware order

Revisar `Program.cs` para a ordem correta:

```csharp
app.UseExceptionHandler();
// app.UseHsts(); // Production
app.UseHttpsRedirection(); // se certificado
app.UseCors("Dev");
app.UseRequestLocalization();
app.UseResponseCompression();
app.UseStaticFiles();
app.UseRouting(); // explícito
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseOutputCache();
app.UseRateLimiter();
// ... endpoints
```

Adicionar `UseRouting` explicitamente para garantir que CORS, antiforgery e rate limiting funcionem corretamente.

---

## 7. Business Rules

- Env vars continuam tendo precedência sobre `appsettings`.
- `Admin:Password` nunca pode ser armazenado em texto claro no `appsettings.json` de produção (usar env ou user-secrets).
- SSE e SignalR nunca devem sofrer cache nem rate limiting.
- Health checks não devem expor informações sensíveis.

---

## 8. Domain Modeling

N/A.

---

## 9. Expected Architecture

```text
src/Taskboard.Server/
  appsettings.json
  appsettings.Development.json
  appsettings.Production.json
  Configuration/
    TaskboardOptions.cs
    AdminOptions.cs
    TaskboardOptionsValidator.cs
  Program.cs                # middleware order revisado
src/Taskboard.Application.Contracts/Configuration/
  ITaskboardConfiguration.cs # se necessário para camada Application
```

---

## 10. API Contracts

N/A. Nenhuma rota pública é alterada. Apenas adicionam-se `/health/ready` e `/health/live`.

---

## 11. Application Contracts

- `TaskboardOptions` e `AdminOptions` podem residir em `Taskboard.Application.Contracts` se forem consumidos por `Application`.
- `ITaskboardConfiguration` expõe URL, data dir e connection string.

---

## 12. Persistence and Data

N/A. O schema SQLite não muda. Apenas o local da connection string muda.

---

## 13. Integrations

- MudBlazor (sem mudanças).
- EF Core health check (built-in).

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Config inválida | `Admin:Password` ausente em produção | falha no startup com mensagem clara |
| Env var sobrepondo appsettings | `TASKBOARD_PORT=9999` | usa 9999 |
| Health check falha | SQLite inacessível | `GET /health/ready` retorna 503 |
| Rate limit excedido | 6 logins em 60s | `429 Too Many Requests` |
| Arquivo appsettings ausente | env define tudo | inicia normalmente com `IConfiguration` de env |

---

## 15. Few-Shot Examples

```csharp
// TaskboardOptions
public sealed class TaskboardOptions
{
    [Range(1, 65535)]
    public int Port { get; set; } = 47823;

    [Required]
    public string DataDir { get; set; } = ".data";

    public DatabaseOptions Database { get; set; } = new();
}

// Program.cs snippet
builder.Services.AddOptions<TaskboardOptions>()
    .BindConfiguration("Taskboard")
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
    });
});
```

---

## 16. Non-Functional Requirements

### Performance

- Latência de `/health` < 10ms.
- Redução de 30-50% no tamanho de assets estáticos com Brotli.

### Security

- Credenciais nunca em arquivos commitados.
- Rate limit em autenticação.
- HSTS em produção.

### Reliability

- Health checks cobrindo banco e disco.
- Configuração validada no startup.

---

## 17. Mandatory Guardrails

- Nunca logar senhas, tokens ou connection strings.
- Não commitar `appsettings.Production.json` com valores reais.
- Não modificar `.github/workflows`.
- Preservar env vars legíveis (`TASKBOARD_PORT`, `TASKBOARD_ADMIN_*`).

---

## 18. Expected Tests

### Unit tests

| Class | Scenarios |
|---|---|
| `TaskboardOptions` | validação, defaults, bind |
| `AdminOptions` | senha ausente em produção |
| `TaskboardEnvironment` | resolução de env vs appsettings |

### Integration tests

| Flow | Validation |
|---|---|
| `GET /health/ready` | 200 quando banco OK |
| `POST /api/login` 6x em 1 min | 429 na 6ª |
| `GET /css/site.css` | `Content-Encoding` gzip/brotli |
| `GET /api/meta` | retorna com cache control |

---

## 19. Acceptance Criteria

- [ ] `appsettings.json` e variants criados.
- [ ] `TaskboardOptions` e `AdminOptions` com `ValidateOnStart`.
- [ ] `TaskboardEnvironment` não é mais estático ou é wrapper de `IOptions`.
- [ ] Response compression ativo.
- [ ] Output cache configurado para estáticos e leituras seguras.
- [ ] Health checks em `/health`, `/health/ready`, `/health/live`.
- [ ] Rate limiting em login e API.
- [ ] Middleware order revisado e com `UseRouting`.
- [ ] Build e testes passam sem warnings.

---

## 20. Implementation Plan

1. Criar `appsettings*.json` e preparar valores de dev.
2. Criar `TaskboardOptions` e `AdminOptions` com validação.
3. Refatorar `AdminUser` e `TaskboardEnvironment` para consumir opções.
4. Adicionar response compression, output cache, rate limiting.
5. Adicionar health checks.
6. Revisar e ajustar ordem do pipeline.
7. Adicionar testes de opções e integração.
8. Validar `dotnet build` e `dotnet test`.

---

## 21. Rollback Strategy

- Reverter `Program.cs` para a configuração anterior.
- Restaurar `TaskboardEnvironment` estático se necessário.
- Remover `appsettings*.json` e usar apenas env vars.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Appsettings em Produção vazam credenciais | Alto | Baixa | validar que `Admin:Password` não está hardcoded; usar env/user-secrets |
| Rate limiter bloqueia agentes automatizados | Médio | Média | política separada para `/api` com IP e/ou token |
| Mudança no pipeline quebra antiforgery | Alto | Média | executar testes de integração de login/logout |
| Saúde do SQLite com EF Core aumenta startup | Baixo | Médio | health check leve ou HealthCheckOptions.Timeout |

---

## 23. Definition of Done

- [ ] SPEC revisado.
- [ ] Implementação segue o SPEC.
- [ ] Testes criados.
- [ ] Build sem warnings.
- [ ] Documentação de instalação atualizada para novas env vars e appsettings.
- [ ] PR descreve mudanças de configuração e riscos.

---

## 24. Key Reminder

> The SPEC is the contract. Do not expand scope beyond server configuration.

---

## Pending Questions

1. A senha admin continuará vindo de `TASKBOARD_ADMIN_PASSWORD` ou queremos suportar `dotnet user-secrets`?
2. O rate limit de `/api` deve ser por IP ou por usuário autenticado?
3. HTTPS deve ser ativado por padrão em produção (redirecionamento e HSTS)?

---

## Human Approval Checklist

- [ ] Escopo limitado a configuração/middleware.
- [ ] Requisitos de segurança claros.
- [ ] Riscos e rollback aceitáveis.
