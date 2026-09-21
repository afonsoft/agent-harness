# SPEC-009: Skill `manage-taskboard`

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Agent Skill manage-taskboard |
| Product / System | agent-harness |
| Module / Bounded Context | Agent Skills |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-skill-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

A skill atual `skills/manage-taskboard/SKILL.md` orienta agentes de IA a usar `taskctl` e o MCP server.

### Objective

Criar skill equivalente para o ecossistema .NET 10, seguindo a especificação Agent Skills (agentskills.io) e as convenções `afonsoft/agents-skills`.

### Expected outcome

Pasta `skills/manage-taskboard/` contendo `SKILL.md` com YAML frontmatter, descrição, instruções de uso, e referências ao `taskctl` .NET e MCP server.

### Out of scope

- Instalador multi-IDE (pode reutilizar `install.sh` existente).

---

## 2. Agent Role

> Technical writer + agent skill designer familiarizado com agentskills.io.

---

## 3. Agent Autonomy Level

3

---

## 4. Product Context

### Functional context

A skill permite que agentes (Claude, Devin, OpenCode, Cursor, etc) invoquem `taskctl` e consumam o MCP server.

### Technical context

- SKILL.md descreve `taskctl` .NET (Spectre.Console.Cli).
- Frontmatter com `name` e `description`.
- Ferramentas: Bash, Read, Edit, Write.

### Relevant stack

- Markdown + YAML
- Agent Skills Specification
- .NET 10

---

## 5. Task Definition

### Main task

Escrever `SKILL.md` para a skill `manage-taskboard` orientando agentes a usar `taskctl` e MCP server.

### Subtasks

- Documentar CLI `taskctl` commands.
- Documentar MCP server tools.
- Fornecer exemplos de uso.
- Listar variáveis de ambiente.

### Do not do

- Não modificar outros arquivos de skill.

---

## 6. Functional Requirements

### FR-001: Frontmatter YAML

**Description:**  
Skill deve ter frontmatter com `name`, `description`, `tools`.

```yaml
---
name: manage-taskboard
description: Gerencie o Dashi Taskboard via CLI taskctl .NET e via servidor MCP para agentes de IA.
tools:
  - Bash
  - Read
  - Edit
  - Write
---
```

### FR-002: Variáveis de ambiente

**Description:**  
Documentar variáveis de ambiente obrigatórias.

| Variável | Propósito | Padrão |
|---|---|---|
| `TASKBOARD_URL` | URL base da API REST | `http://127.0.0.1:47823` |
| `TASKBOARD_THREAD_ID` | Vínculo de thread | - |

### FR-003: CLI Commands

**Description:**  
Documentar subcomandos disponíveis.

```bash
# Projetos
taskctl project list --json
taskctl project create --id <id> --name <name> --workspace-path <path>

# Issues
taskctl issue list --project <project> --status <status>
taskctl issue get <identifier>
taskctl issue create --project <project> --title <title> --status <status> --priority <priority>
taskctl issue update <identifier> --title <title>
taskctl issue move <identifier> --status <status>
taskctl issue archive <identifier>
taskctl issue restore <identifier>

# Comentários
taskctl comment list <identifier>
taskctl comment add <identifier> <body>
taskctl comment update <commentId> <body>
taskctl comment delete <commentId>

# Anexos
taskctl attachment upload <identifier> <file>
taskctl attachment download <attachmentId> <output>

# Cloud
taskctl cloud login <url>
taskctl cloud status
taskctl cloud logout

# Contexto
taskctl context current
```

### FR-004: MCP Tools

**Description:**  
Documentar tools MCP disponíveis.

```
list_projects
get_project
create_project
list_issues
get_issue
create_issue
update_issue
move_issue
archive_issue
restore_issue
add_comment
upload_attachment
cloud_status
```

### FR-005: Build e Instalação

**Description:**  
Instruir como compilar e instalar.

```bash
dotnet build src/Taskboard.Cli/Taskboard.Cli.csproj
dotnet build src/Taskboard.Mcp/Taskboard.Mcp.csproj

# Criar link simbólico
ln -s $(pwd)/src/Taskboard.Cli/bin/Debug/net10.0/taskctl ~/.local/bin/taskctl
```

---

## 7. Business Rules

- Output `--json` é preferido para processamento programático.
- Nunca expor tokens ou chaves de API.
- URLs de API via variável de ambiente `TASKBOARD_URL`.

---

## 8. Domain Modeling

Nenhum; skill é documentação.

---

## 9. Expected Architecture

```text
skills/manage-taskboard/
  SKILL.md
  references/
    cli.md
```

---

## 10. API Contracts

Ver `SPEC-002-rest-api.md` e `SPEC-003-cli.md`.

---

## 11. Application Contracts

Nenhum; skill é documentação.

---

## 12. Persistence and Data

Nenhum; skill é documentação.

---

## 13. Integrations

- CLI `taskctl`
- MCP server

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| API offline | taskctl --url inválido | erro "Connection refused" |
| MCP tool não encontrada | tool inválida | erro protocolo MCP |
| Variável de ambiente faltando | TASKBOARD_URL não definida | usa default |

---

## 15. Few-Shot Examples

```bash
# Criar projeto
taskctl project create --id my-project --name "My Project" --json

# Listar issues
taskctl issue list --project my-project --status todo --json

# Criar issue
taskctl issue create --project my-project --title "Fix bug" --priority high --json
```

---

## 16. Non-Functional Requirements

- Documentação clara e concisa.
- Exemplos práticos.
- Facing para múltiplos agentes (Claude, Devin, OpenCode).

---

## 17. Mandatory Guardrails

- Nunca expor tokens ou chaves de API.
- Preferir `--json` para saída programática.

---

## 18. Expected Tests

Nenhum; skill é documentação.

---

## 19. Acceptance Criteria

- [x] Frontmatter YAML válido.
- [x] Comandos CLI documentados.
- [x] MCP tools documentadas.
- [x] Variáveis de ambiente listadas.
- [x] Exemplos de uso.
- [x] Build instructions.

---

## 20. Implementation Plan

1. Criar pasta `skills/manage-taskboard/`.
2. Escrever `SKILL.md` com frontmatter e conteúdo.
3. Criar `references/cli.md` com referência detalhada.
4. Testar com agente (Claude/Devin).

---

## 21. Rollback Strategy

- Remover skill.
- Manter CLI e MCP.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Documentação desatualizada | Médio | Média | Manter skill sincronizada com CLI/MCP |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] Skill implementada e testada.
- [x] Agentes conseguem usar skill.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Skill deve ser instalada globalmente ou por workspace? (Global via `~/.agents/skills/`)

## Human Approval Checklist

- [x] Skill clara e concisa.
- [x] Exemplos funcionais.
- [x] Variáveis de ambiente documentadas.