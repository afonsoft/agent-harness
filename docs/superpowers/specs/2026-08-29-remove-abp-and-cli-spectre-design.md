# Design: Remover ABP do Dashboard + Migrar CLI para Spectre.Console.Cli

- **Date**: 2026-08-29
- **Status**: Approved (design validated in chat)
- **Author**: opencode agent
- **Branch policy**: não commitar em `main` (branch protegida) — este doc fica como rascunho local.

## Context

O repositório `agent-harness` usa ABP de forma mínima: o repositório (`IRepository<T>` /
`EfCoreRepository<T>`) é **custom**, e o ABP real aparece só no `Taskboard.Domain`, via
`Volo.Abp.Domain.Entities` (base `AggregateRoot`/`Entity`). O objetivo do usuário é deixar o
dashboard "cru" (sem framework) e trocar o CLI `taskctl` do `System.CommandLine` para
`Spectre.Console.Cli`.

Descoberta-chave: o `Taskboard.Domain` já possui classes-base próprias
(`Taskboard.AggregateRoot<TKey>`) que **herdam** de `Volo.Abp.Domain.Entities.BasicAggregateRoot`.
Portanto remover o ABP = desacoplar essas bases do ABP e criar uma `Entity<TKey>` custom.

## Parte A — Dashboard: remover ABP totalmente

### Escopo (somente `Taskboard.Domain`)
Arquivos afetados:
- `src/Taskboard.Domain/AggregateRoot.cs`
- 10 entidades em `src/Taskboard.Domain/Entities/` que herdam `Entity<X>`:
  `TaskRelation`, `WorkflowSequence`, `TaskActivity`, `WorkflowWorkspace`, `AiChatEvent`,
  `ProjectSummary`, `Comment`, `WorkflowNode`, `Attachment`, `AiChatRun`.
- `src/Taskboard.Domain/Taskboard.Domain.csproj` (remover `Volo.Abp.Ddd.Domain`).

### Novas classes-base (namespace `Taskboard`)
```csharp
public abstract class Entity<TKey> where TKey : notnull
{
    public TKey Id { get; protected set; } = default!;
    // igualdade por Id (replica comportamento do Entity<> do ABP)
    public override bool Equals(object? obj) { ... }
    public override int GetHashCode() { ... }
    // operadores == / !=
}

public abstract class AggregateRoot<TKey> : Entity<TKey> where TKey : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public long Version { get; protected set; } = 1;
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void AddDomainEvent(IDomainEvent e) => _domainEvents.Add(e);
    protected void IncrementVersion() => Version++;
}
```

### Mudanças
1. `AggregateRoot.cs`: remover `using Volo.Abp.Domain.Entities;`; deixar de herdar
   `BasicAggregateRoot<TKey>, IEntity<TKey>`; herdar `Entity<TKey>`; remover
   `AddLocalEvent`/`ClearLocalEvents` (ABP) — `_domainEvents` já cobre o necessário.
2. 10 entidades: remover `using Volo.Abp.Domain.Entities;` (já têm `using Taskboard;`,
   então `Entity<X>` resolve para a base custom).
3. **Guid-keyed entities** (`TaskRelation : Entity<Guid>`, etc.): o ABP auto-gerava o Guid.
   Com a base custom, garantir que cada `Create(...)` define `Id` explicitamente
   (`Guid.NewGuid()`), senão o `Id` fica `Guid.Empty`.
4. `Domain.csproj`: remover `PackageReference Include="Volo.Abp.Ddd.Domain"`.

### Validação
- `dotnet build Taskboard.sln` (0 warnings / 0 errors — `TreatWarningsAsErrors`).
- `dotnet test` (unit 46 + integration 7) verde.
- Nenhum projeto fora de `Domain` referencia `Volo.Abp` (verificado por grep).

### Fora de escopo
- REST/HTTP/SSE, `IRepository`, `EfCoreRepository`, Server, Application — permanecem inalterados.

## Parte B — CLI: System.CommandLine → Spectre.Console.Cli

### Escopo (`src/Taskboard.Cli`)
- `Taskboard.Cli.csproj`: trocar `System.CommandLine` por `Spectre.Console` +
  `Spectre.Console.Cli`.
- `Program.cs` (e comandos): reescrever a árvore de comandos usando `CommandApp` +
  `CommandSettings`/`Command<TSettings>` por subcomando. Subcomandos existentes:
  `project` (list/create/map), `issue` (list/get/create/update/move/archive/restore/relation),
  `comment` (create), `attachment` (create), `cloud`, `context`.

### Abordagem
- **Manter a lógica HTTP intacta** (chamadas ao `TaskboardClient`/API). Apenas a camada de
  definição de comandos muda para o modelo do Spectre.Console.Cli:
  - `CommandSettings` com `[CommandArgument]`/`[CommandOption]` para cada comando.
  - `Command<TSettings>` com `ExecuteAsync(CommandContext, TSettings)`.
  - Registro via `new CommandApp().Configure(cfg => cfg.AddCommand<...>("verb"))` ou
    `CommandApp<RootCommand>`.
- Manter `ProjectReference` a `Application.Contracts` (DTOs compartilhados) — CLI já enxuto.

### Validação
- `dotnet build` do CLI.
- Smoke test: `dotnet run --project src/Taskboard.Cli -- project list --json` (ou equivalente)
  retorna o projeto `local` (seed criado em `Program.cs` do Server).
- `dotnet test` da solution continua verde.

## Riscos
- Igualdade de entidades em EF Core: implementar `Equals`/`GetHashCode` por `Id` para não quebrar
  o change tracker.
- Guid auto-gen: revisar todos os `Create` de entidades `Entity<Guid>`.
- CLI: mapear fielmente argumentos/opções atuais para `CommandSettings`.

## Plano de execução
1. Parte A (Domain) → build + testes.
2. Parte B (CLI Spectre) → build + smoke test.
3. Build + test da solution completa ao final.
