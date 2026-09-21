# SPEC-003: CLI `taskctl`

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | CLI taskctl |
| Product / System | agent-harness |
| Module / Bounded Context | CLI |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-cli-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O CLI atual `cli/taskctl.mjs` oferece comandos para projetos, cloud, issues, comentários, anexos e contexto.

### Objective

Criar um CLI .NET global tool `taskctl` (usando **Spectre.Console.Cli**) que consuma a API REST do Taskboard e ofereça os mesmos subcomandos e saída JSON.

### Expected outcome

`taskctl` em .NET 10 equivalente ao Node.js: `project list/create/map`, `cloud login/status/logout`, `issue list/get/create/update/move/archive/restore/relation`, `comment list/add/update/delete`, `attachment download/upload`, `context current`.

### Out of scope

- Empacotamento NuGet global tool pode ser feito posteriormente.

---

## 2. Agent Role

> Senior .NET CLI engineer usando **Spectre.Console.Cli** e `HttpClient`.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não introduzir lógica de negócio no CLI.
- Não persistir secrets em texto plano.

---

## 4. Product Context

### Functional context

O CLI permite automação, scripts e integração com agentes de IA fora do navegador.

### Technical context

- Node.js `cli/taskctl.mjs` (legado).
- Default API URL `http://127.0.0.1:47823`.
- Saída JSON opcional (`--json`).
- Schema version 2.

### Relevant stack

- .NET 10
- **Spectre.Console.Cli** (v0.49.1)
- `HttpClient`
- `Microsoft.Extensions.DependencyInjection`

---

## 5. Task Definition

### Main task

Implementar CLI `taskctl` em C#.

### Subtasks

- `project list`, `project create`, `project map`
- `cloud login`, `cloud status`, `cloud logout`
- `issue list`, `issue get`, `issue create`, `issue update`, `issue move`, `issue archive`, `issue restore`, `issue relation`
- `comment list`, `comment add`, `comment update`, `comment delete`
- `attachment download`, `attachment upload`
- `context current`

### Do not do

- Não implementar autenticação complexa de cloud nesta spec (ver `SPEC-006`, `SPEC-010`).

---

## 6. Functional Requirements

### FR-001: Configuração

**Description:**  
Ler URL base de `TASKBOARD_URL` ou `--url`; armazenar config em `~/.config/taskctl/settings.json` (ou `~/.taskctl/config.json`).

### FR-002: Projetos

```bash
taskctl project list --json
taskctl project create --id my-project --name "My Project" --workspace-path /path --json
taskctl project map <project> --workspace-path /path
```

### FR-003: Issues

```bash
taskctl issue list --project local --status todo --json
taskctl issue get <identifier> --json
taskctl issue create --project local --title "x" --status todo --priority high --json
taskctl issue update <identifier> --title "y" --json
taskctl issue move <identifier> --status done --json
taskctl issue archive <identifier> --json
taskctl issue restore <identifier> --json
taskctl issue relation <identifier> add parent <target>
taskctl issue relation <identifier> remove parent <target>
```

### FR-004: Comentários

```bash
taskctl comment list <identifier> --json
taskctl comment add <identifier> "texto" --json
taskctl comment update <commentId> "novo" --json
taskctl comment delete <commentId>
```

### FR-005: Anexos

```bash
taskctl attachment upload <identifier> /path/to/file.png --json
taskctl attachment download <attachmentId> /output/path.png
```

### FR-006: Contexto

```bash
taskctl context current --json
```

---

## 7. Business Rules

- CLI sempre consome API HTTP; nenhuma lógica de negócio local.
- Saída `--json` é o padrão para integração com agentes.
- Erros da API são propagados com código e mensagem.
- Exit codes: 0 sucesso, 1 erro genérico, 2 validação, 3 servidor offline, 4 auth, 5 conflito.
- **Convenção crítica**: Todos os `CommandArgument` usam placeholders com `<>` (obrigatório) ou `[]` (opcional). Nomes "cru" quebram o `StyleParser` do Spectre.

---

## 8. Domain Modeling

Nenhum; CLI é presentation.

---

## 9. Expected Architecture

Console app `Taskboard.Cli` usando **Spectre.Console.Cli** e `HttpClientFactory`.

```text
src/Taskboard.Cli/
  Program.cs
  Commands/
    ProjectCommands.cs
    IssueCommands.cs
    CommentCommands.cs
    AttachmentCommands.cs
    CloudCommands.cs
    ContextCommands.cs
  Services/
    TaskboardApiClient.cs
    CliConfigService.cs
    OutputFormatter.cs
```

---

## 10. API Contracts

Ver `SPEC-002-rest-api.md`.

---

## 11. Application Contracts

Não aplica. CLI consome HTTP diretamente.

---

## 12. Persistence and Data

Config local em JSON (`~/.config/taskctl/settings.json`).

---

## 13. Integrations

Taskboard HTTP API.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Servidor offline | --url inválido | mensagem de erro amigável, exit 3 |
| Argumentos insuficientes | create sem --title | help |
| Resposta não-JSON | --json com erro | stderr claro |
| Version conflict | issue update com version stale | retorna 409 e mensagem |
| CommandArgument sem <> | [CommandArgument(0, "project")] | crash StyleParser |

---

## 15. Few-Shot Examples

```bash
taskctl project list --json
# [{"id":"local","name":"全局","issueCount":0}]

$env:TASKBOARD_URL="http://127.0.0.1:47823"
taskctl issue create --project local --title "Fix bug" --status todo --priority high --json
```

---

## 16. Non-Functional Requirements

- Startup < 500ms.
- JSON output deterministic.
- Cross-platform (Windows, macOS, Linux).

---

## 17. Mandatory Guardrails

- Não persistir credenciais em texto plano.
- Não implementar lógica de domínio no CLI.
- Usar `CancellationToken` para timeouts.
- **CommandArgument placeholders SEMPRE com `<>` ou `[]`**.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| `taskctl project list --json` | parse correto |
| `taskctl issue create` | POST payload correto |
| `taskctl issue move` | PATCH / move endpoint |
| Config load | env var override |
| Exit codes | 0, 3, 5 |

---

## 19. Acceptance Criteria

- [x] Todos os subcomandos implementados.
- [x] Saída JSON funcional.
- [x] Exit codes respeitados.
- [x] Configuração via env/args.
- [x] CommandArgument placeholders corretos (`<>`/`[]`).

---

## 20. Implementation Plan

1. Criar `Taskboard.Cli` console app.
2. Configurar **Spectre.Console.Cli** root com subcommands.
3. Implementar `TaskboardApiClient` com `HttpClientFactory`.
4. Implementar `CliConfigService`.
5. Implementar commands por recurso.
6. Adicionar tests de integração CLI.

---

## 21. Rollback Strategy

- Reverter para versão anterior do CLI.
- Manter compatibilidade com API.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| `Spectre.Console.Cli` API instável | Médio | Baixa | Pin versão 0.49.1 |
| Diferenças de saída JSON vs Node.js | Médio | Média | Validar com testes contract |
| CommandArgument placeholder bug | Alto | Média | Convenção `<>`/`[]` documentada em `gotchas.md` |

---

## 23. Definition of Done

- [x] CLI funcional.
- [x] Tests passam.
- [x] Documentação atualizada (`cli-migration.md`, `gotchas.md`, `followups.md`).

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. A config pode usar `appsettings.json` ou `~/.taskctl/config.json`? (Resolvido: `~/.config/taskctl/settings.json`)
2. Empacotar como global tool NuGet? (Futuro)
3. Usar `System.CommandLine` ou `CommandLineParser`? (Resolvido: **Spectre.Console.Cli**)

## Human Approval Checklist

- [x] Subcomandos listados.
- [x] Exit codes definidos.
- [x] Configuração clara.
- [x] Convenção CommandArgument documentada.