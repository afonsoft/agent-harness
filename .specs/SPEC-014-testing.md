# SPEC-014: Testing Strategy

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Testing Strategy |
| Product / System | agent-harness |
| Module / Bounded Context | Quality Assurance |
| Change type | Implementation |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-tests-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O sistema precisa de testes automatizados para garantir qualidade e regressão.

### Objective

Definir estratégia de testes: unit, integration, e2e.

### Expected outcome

- **Unit tests**: Domain, Application.
- **Integration tests**: API endpoints, EF Core.
- **Test libraries**: xUnit + Shouldly + NSubstitute.

### Out of scope

- Testes E2E com Playwright (futuro).
- Testes de performance (futuro).

---

## 2. Agent Role

> QA engineer especializado em .NET testing.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não quebrar build com testes falhando.
- Não remover testes existentes sem justificativa.

---

## 4. Product Context

### Functional context

Testes automatizados para todas as funcionalidades.

### Technical context

- xUnit como test runner.
- Shouldly para assertions.
- NSubstitute para mocking.
- `Microsoft.AspNetCore.Mvc.Testing` para integration tests.

### Relevant stack

- .NET 10
- xUnit 2.9.x
- Shouldly 4.3.x
- NSubstitute 5.3.x

---

## 5. Task Definition

### Main task

Implementar estratégia de testes.

### Subtasks

- Unit tests para domínio.
- Integration tests para API.
- Test helpers e fixtures.

### Do not do

- Não testar implementação detalhes.

---

## 6. Functional Requirements

### FR-001: Unit Tests - Domain

**Description:**  
Testar entidades, value objects e domain services.

**Naming:** Dado_Quando_Entao (português).

**Exemplos:**

```csharp
public class Dado_uma_tarefa_valida
{
    private Task _task;
    
    public void Quando_criar()
    {
        _task = Task.Create(...);
    }
    
    public void Entao_deve_ter_identifier()
    {
        _task.Identifier.ShouldNotBeNull();
    }
}
```

**Classes a testar:**

- `TaskStatus` - valores válidos/inválidos
- `TaskPriority` - valores válidos/inválidos
- `Actor` - tipos user/agent
- `Recurrence` - interval/unit válidos/inválidos
- `TaskIdentifier` - formatos local e JIRA
- `Project` - numeração, labels, workspace
- `Task` - criação, move, archive, restore, delete, versionamento, patch
- `TaskRelation` - parent único, related simétrico, self-relation
- `Comment` - body vazio, thread_id, edit
- `Attachment` - kind, size, filename
- `AiChatThread` - runs, events, status

### FR-002: Integration Tests - API

**Description:**  
Testar endpoints HTTP com `WebApplicationFactory`.

**Setup:**

```csharp
public class ServerEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    
    public ServerEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }
}
```

**Testes:**

```csharp
[Fact]
public async Task Dado_NenhumProjeto_Quando_ListarProjects_Entao_Returns200()
{
    var response = await _client.GetAsync("/api/projects");
    response.StatusCode.ShouldBe(HttpStatusCode.OK);
}
```

### FR-003: Test Projects

**Description:**

```text
tests/
  Taskboard.Tests.Unit/
    Domain/
      Entities/
        TaskTests.cs
        ProjectTests.cs
        TaskRelationTests.cs
      ValueObjects/
        TaskStatusTests.cs
        TaskPriorityTests.cs
        ActorTests.cs
        RecurrenceTests.cs
        TaskIdentifierTests.cs
  Taskboard.Tests.Integration/
    ServerEndpointsTests.cs
```

---

## 7. Business Rules

- Testes Dado_Quando_Entao em português.
- NSubstitute para mocking.
- Shouldly para assertions (fluente).
- Integration tests usam `WebApplicationFactory`.

---

## 8. Domain Modeling

Nenhum; testes validam domínio.

---

## 9. Expected Architecture

```text
tests/
  Taskboard.Tests.Unit/
    Taskboard.Tests.Unit.csproj
    Domain/
      Entities/
      ValueObjects/
  Taskboard.Tests.Integration/
    Taskboard.Tests.Integration.csproj
    ServerEndpointsTests.cs
```

---

## 10. API Contracts

Ver `SPEC-002-rest-api.md`.

---

## 11. Application Contracts

Nenhum.

---

## 12. Persistence and Data

Testes de integração usam banco SQLite em memória (`:memory:` ou arquivo temporário).

---

## 13. Integrations

Nenhum.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Test |
|---|---|
| Domain exception |Dado_valor_invalido_Quando_criar_Entao_throws |
| 404 Not Found | Dado_id_invalido_Quando_Get_Entao_404 |
| Version conflict | Dado_version_antiga_Quando_Update_Entao_409 |

---

## 15. Few-Shot Examples

```csharp
public class TaskStatusTests
{
    [Theory]
    [InlineData("todo")]
    [InlineData("in_progress")]
    [InlineData("done")]
    public void Dado_status_valido_Quando_criar_Entao_nao_throws(string status)
    {
        var action = () => TaskStatus.From(status);
        action.ShouldNotThrow();
    }
    
    [Fact]
    public void Dado_status_invalido_Quando_criar_Entao_throws()
    {
        var action = () => TaskStatus.From("invalid");
        action.ShouldThrow<DomainException>();
    }
}
```

---

## 16. Non-Functional Requirements

- Cobertura ≥80% (meta 90%).
- Tempo de execução < 5 min para suite completa.
- Testes independentes (sem ordem).

---

## 17. Mandatory Guardrails

- Não remover testes existentes.
- Não commitar testes falhando.
- Nomear testes de forma descritiva.

---

## 18. Expected Tests

| Categoria | Qtd | Cobertura |
|---|---|---|
| Domain entities | 5+ | 90% |
| Value objects | 5+ | 90% |
| API endpoints | 10+ | 70% |

---

## 19. Acceptance Criteria

- [x] Unit tests para domain.
- [x] Integration tests para API.
- [x] Nomeação Dado_Quando_Entao.
- [x] NSubstitute + Shouldly.
- [x] Build falha se testes falham.

---

## 20. Implementation Plan

1. Criar `Taskboard.Tests.Unit` project.
2. Adicionar packages (xUnit, Shouldly, NSubstitute).
3. Criar domain tests Dado_Quando_Entao.
4. Criar `Taskboard.Tests.Integration` project.
5. Implementar API integration tests.
6. Configurar CI para rodar testes.

---

## 21. Rollback Strategy

- Reverter branch.
- Manter testes anteriores.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Testes quebrando CI | Médio | Média | Validar local antes de push |
| Coverage baixa | Médio | Baixa | Adicionar testes progressivamente |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] Unit tests implementados.
- [x] Integration tests implementados.
- [x] CI configurado.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Usar test parallelization? (Resolvido: xUnit parallel por padrão)
2. Testes de UI com Playwright? (Futuro)

## Human Approval Checklist

- [x] Estratégia clara.
- [x] Naming conventions definidas.
- [x] Ferramentas definidas.