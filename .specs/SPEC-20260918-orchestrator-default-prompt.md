# SPEC-20260918-orchestrator-default-prompt

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `orchestrator-default-prompt` |
| Type | `Feature` (config default) |
| Stack | `.NET 10 / Blazor` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260918-orchestrator-default-prompt` |
| Ticket | — |
| Status | `Done` |

## 1. User Story

**As a** administrador executando issues com agentes CLI
**I want** que o prompt default instrua o agente a usar a skill `orchestrator` para gerenciar todo o processo
**So that** cada execução siga o fluxo governado (planejar → delegar → validar → reportar) em vez de agir ad-hoc.

**Problem context:**
O `AgentPromptTemplate.Builtin` (en-US) manda clonar o repo, usar skills, consultar o MCP `knowledge` e usar `manage-taskboard` — mas não instrui o agente a orquestrar o trabalho. A skill `orchestrator` já está no catálogo instalado (`afonsoft/skills`, v2.3.1) e é o entry point do harness: planeja, audita, delega a skills especializadas e re-valida até a entrega.

## 2. Scope

**In scope:**
- Reescrever `AgentPromptTemplate.Builtin` incluindo o uso da skill `orchestrator` como gestora do processo, mantendo placeholders e instruções existentes (clone em `~/repos`, skills, MCP `knowledge`, `manage-taskboard`).
- Atualizar testes de `AgentPromptTemplate` para o novo texto.
- Remover/ajustar override persistido `Taskboard:Agents:DefaultPrompt` no SQLite de produção se existir (o builtin só vale sem override — aprendizado PR #100).
- Documentação do placeholder/comportamento (docs features en/pt-br) se necessário.

**Out of scope:**
- Novos placeholders.
- Mudar como o template é renderizado/apresentado na UI.
- Editar a skill `orchestrator` em si (vem de `afonsoft/skills` upstream).

## 3. Technical Context

**Where the change happens:**
`src/Taskboard.Application.Contracts/Agents/AgentPromptTemplate.cs` (const `Builtin`). A UI (`/agents` — "Default agent prompt") lê `IAgentPromptTemplateService` → override `Taskboard:Agents:DefaultPrompt` ou builtin. Produção: verificar `ConfigurationOverrides` no SQLite — se houver linha para a key, removê-la ou atualizá-la para o novo texto.

**Files to read before implementing:**
- `src/Taskboard.Application.Contracts/Agents/AgentPromptTemplate.cs`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` (override path)
- `tests/Taskboard.Tests.Unit/Agents/AgentPromptTemplateTests.cs`
- `.claude/skills/orchestrator/SKILL.md` (o que a skill faz)

**Files to create or modify:**
```text
src/Taskboard.Application.Contracts/Agents/AgentPromptTemplate.cs
tests/Taskboard.Tests.Unit/Agents/AgentPromptTemplateTests.cs
tests/Taskboard.Tests.Integration/AgentRunEndpointsTests.cs (se assertar texto)
docs/features.md, docs/features.pt-br.md (se descreverem o default)
```

## 4. Requirements

### RF-001: Builtin com orchestrator
- **Description:** O `Builtin` passa a instruir: usar a skill `orchestrator` para gerenciar todo o processo da execução (planejar o trabalho da issue, delegar às skills especializadas, validar antes de concluir), clonar `{repoUrl}` em `~/repos`, consultar o MCP `knowledge` quando precisar de contexto e usar `manage-taskboard` para movimentar/atualizar o card.
- **Rules:** texto en-US; placeholders `{repoUrl}`, `{issueTitle}`, `{issueBody}` preservados; seções `Issue:`/`{issueBody}` mantidas; ≤ `MaxLength` (8192).
- **Texto proposto:**
  ```text
  Use the orchestrator skill to manage the entire process for this task: plan the work, delegate to the specialized skills, and validate the result before finishing.
  Clone the repository {repoUrl} into the current working directory (~/repos) and apply the fixes described in the issue below.
  Use the available skills to optimize the process, consult the "knowledge" MCP for additional context when needed, and use the manage-taskboard skill to move and update the issue card.

  Issue: {issueTitle}

  {issueBody}
  ```
- **Input → Output:** `Render(Builtin, ...)` → prompt com instrução do orchestrator na primeira linha.

### RF-002: Override de produção
- **Description:** Verificar `ConfigurationOverrides` para `Taskboard:Agents:DefaultPrompt`; se existir, remover (builtin assume) ou atualizar para o novo texto — o que for decidido na implementação, documentado no PR.
- **Rules:** nunca deixar override desatualizado mascarando o builtin novo (bug real do PR #100).

### RF-003: Testes
- **Description:** `AgentPromptTemplateTests` asserta que o builtin contém `orchestrator`, `~/repos`, `manage-taskboard`, `knowledge` e os 3 placeholders.
- **Input → Output:** suite unit verde.

**Business rules / invariants:**
- Render continua trim + replace ordinal; sem mudança de assinatura.
- Fallback quando skill orchestrator não estiver instalada no CLI é responsabilidade do agente (prompt apenas instrui; skill vem do sync global de skills).

## 5. API Contract

Sem mudança — `GET/PUT /api/agents/prompt-template` inalterados.

## 6. Acceptance Criteria

- **Dado** a página `/agents`, **quando** abro "Default agent prompt", **então** o texto default inclui a instrução da skill `orchestrator`.
- **Dado** uma execução enfileirada sem prompt custom, **quando** o agente recebe o prompt, **então** a primeira instrução é usar `orchestrator` para gerenciar o processo.
- **Dado** produção com override antigo, **quando** o deploy sobe, **então** a tela exibe o novo builtin (override removido/renovado).
- **Edge:** override custom do usuário não é sobrescrito silenciosamente — se houver override válido do usuário, o SPEC registra a decisão tomada.

## 7. Task Plan

1. **T1** — Novo `Builtin` + testes unit.
2. **T2** — Verificar/tratar override em produção (SQLite `ConfigurationOverrides`).
3. **T3** — Build + suites + docs + PR + merge + deploy.

## 8. Organization Guardrails

- Branch `feature/devin-20260918-orchestrator-default-prompt`; nada em `main`.
- Texto do prompt em en-US (convenção do builtin); sem placeholders novos.

## 9. Definition of Done

- [ ] Builtin instrui `orchestrator` + mantém clone/skills/knowledge/manage-taskboard.
- [ ] Placeholders e `Issue:`/`{issueBody}` preservados; testes verdes.
- [ ] Override de produção tratado e documentado.
- [ ] PR merged + deploy; tela mostra o novo texto.

## 10. Revision — 2026-09-22 (Knowledge-First merged prompt)

O `Builtin` foi substituído pelo prompt "Knowledge-First, Spec-Driven Development and Harness Engineering":

- Placeholders `{repoUrl}`, `{issueTitle}`, `{issueBody}`, `{issueComments}` no bloco de contexto no topo (não mais em seção `ISSUE` no final).
- `# Mandatory rules` + passos numerados: wiki discovery (`read_wiki_structure`/`read_wiki_contents`), task management (`manage-taskboard`), preparo do repo em `~/repos`, harness validation (mantém `orchestrator`/`create-agent-harness`), spec resolution (`write-specs`/`execute-specs`), execution, validation e finalize (`write_knowledge` + relatório final).
- Cada passo registra `write_note`; o processo termina com `write_knowledge`.
- `NormalizeComments` passou a detectar o cabeçalho `Comments:` de forma case-insensitive — o builtin usa `Issue comments:` e não deve renderizar cabeçalho duplicado.
- O texto merged também foi persistido como override `Taskboard:Agents:DefaultPrompt` em `ConfigurationOverrides` (SQLite de produção), a pedido do usuário — como o valor é idêntico ao builtin, `customized` permanece `false`.
