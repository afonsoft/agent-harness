# SPEC-20260919-harness-context-memory

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `harness-context-memory` |
| Type | `Feature` (Harness Engine & Context Engineering) |
| Stack | `.NET 10 / Microsoft.Extensions.AI or Semantic Kernel / SQLite / Tokenizer / C# 14` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-harness-context-memory` |
| Ticket | [#162 — E7](https://github.com/afonsoft/taskboard-ai/issues/162) |
| Status | `Done` |

---

## 1. User Story

**As an** orquestrador de agentes de IA (Harness Engine)
**I want** uma camada de Context Engineering automatizada que monte dinamicamente o System Prompt, gerencie o orçamento de tokens, realize compactação de histórico (anti-context-rot) e persista memórias cross-session
**So that** os modelos recebam exatamente o contexto relevante de projeto e histórico sem estourar limites de contexto, sem degradar a qualidade do raciocínio e mantendo aprendizados entre diferentes execuções.

### Problem Context

No `taskboard-ai` atual:
1. **Prompting Estático e Incompleto:** O `KnownCliAgentAdapter` apenas concatena `Branch:`, `Scope:` e `Instructions`. Ele não inspeciona `AGENTS.md`, `CLAUDE.md`, `.claude/rules/`, status git ou diffs recentes.
2. **Context Rot em Sessões Longas:** Em sessões multi-turn (como a descrita na spec `web-cli-agent`), o histórico cresce indefinidamente. Não há motor de compactação (`microcompact`, `collapse`, `summarization`), degradando as respostas do modelo.
3. **Amnésia entre Sessões:** Lições aprendidas em um run (ex: *"este repositório exige TreatWarningsAsErrors"* ou *"o bundle Blazor precisa de limpeza manual antes de publicar"*) são perdidas após o encerramento do processo.

---

## 2. Scope

### In scope

- **Montador Dinâmico de Contexto de Projeto (`IProjectContextCompiler`):**
  - Busca recursiva de arquivos de instrução (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, `.github/copilot-instructions.md`).
  - Injeção de metadados do ambiente (OS, arquitetura, runtime .NET, diretório de trabalho do worktree).
  - Injeção de contexto Git (branch atual, último commit SHA, git status limpo/sujo).
  - Injeção de catálogo sumarizado de Skills disponíveis (`SKILL.md` frontmatters).
- **Gestão de Orçamento de Tokens e Compactação (`IContextCompactor`):**
  - Contagem/estimativa de tokens de prompt (via tokenizer TikToken/BPE local compatível).
  - Política de compactação automática ao atingir 80% do budget da janela do modelo:
    - Nível 1 (Snip): remove saídas de ferramentas antigas ou truncadas.
    - Nível 2 (Collapse): substitui arquivos lidos por referências compactas.
    - Nível 3 (Summarize): sintetiza blocos de conversação anteriores em um resumo executivo de intenção e decisões tomadas.
- **Armazenamento de Memória Persistente Cross-Session (`ICrossSessionMemoryStore`):**
  - Repositório SQLite de fatos, decisões arquiteturais e lições aprendidas (`MemoryItem`).
  - Recuperação contextual por tags ou busca de similaridade/palavras-chave durante a montagem do prompt.

### Out of scope

- Fine-tuning de modelos locais.
- RAG vetorial pesado distribuído (a busca de memória local usa SQLite FTS5 ou similaridade léxica leve).

---

## 3. Technical Context

### Where the change happens

- **Contracts:** `Taskboard.Application.Contracts/Harness/Context/IProjectContextCompiler.cs`, `IContextCompactor.cs`, `IMemoryStore.cs`.
- **Integrations:** `Taskboard.Integrations/Harness/Context/ProjectContextCompiler.cs`, `TiktokenCompactor.cs`, `SqliteMemoryStore.cs`.
- **Domain:** `Taskboard.Domain/Entities/Harness/ProjectMemoryItem.cs`.

### Files to read before implementing

- `.claude/CONTEXT.md` (regras do harness sobre token budget e compactação)
- `src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs`
- `src/Taskboard.Domain.Shared/ValueObjects/ModelRef.cs`

### Files to create or modify

```text
src/Taskboard.Domain.Shared/Harness/MemoryType.cs                      [new]
src/Taskboard.Domain/Entities/Harness/ProjectMemoryItem.cs           [new]
src/Taskboard.Application.Contracts/Harness/Context/IContextCompiler.cs [new]
src/Taskboard.Application.Contracts/Harness/Context/IContextCompactor.cs [new]
src/Taskboard.Application.Contracts/Harness/Context/IMemoryService.cs [new]
src/Taskboard.Application.Contracts/Harness/Dtos/ContextCompilationDto.cs [new]
src/Taskboard.Integrations/Harness/Context/ProjectContextCompiler.cs  [new]
src/Taskboard.Integrations/Harness/Context/ContextCompactor.cs        [new]
src/Taskboard.Integrations/Harness/Context/SqliteMemoryStore.cs       [new]
tests/Taskboard.Tests.Unit/Harness/ProjectContextCompilerTests.cs     [new]
tests/Taskboard.Tests.Unit/Harness/ContextCompactorTests.cs           [new]
```

---

## 4. Requirements

### RF-001: Compilação Hierárquica de Instruções
- **Description:** O compilador de contexto deve escanear a raiz do repositório/worktree e carregar `AGENTS.md` ou `CLAUDE.md`.
- **Rules:** Se ambos existirem, unifica mantendo as regras específicas de plataforma sem duplicar seções comuns.
- **Input → Output:** `CompileSystemPromptAsync(worktreePath, agentType)` → Texto markdown consolidado pronto para injeção.

### RF-002: Injeção de Estado Git e Ambiente
- **Description:** O prompt gerado deve conter uma tag `<env>` com dados precisos do SO, data atual, branch e commit base do worktree.
- **Input → Output:**
  ```xml
  <env>
    Working directory: /home/ubuntu/.taskboard/worktrees/run_123
    Git branch: feature/agent-run_123-login
    Base commit: a1b2c3d
    Runtime: Linux x64, .NET 10.0.100
  </env>
  ```

### RF-003: Compactação de Histórico Multi-Turn
- **Description:** Em sessões longas, quando a soma de tokens de mensagens e tool calls exceder `MaxPromptTokens * 0.80`, o compactor deve condensar as mensagens mais antigas preservando a última instrução do usuário e o system prompt intactos.
- **Input → Output:** 50 turnos (60k tokens) → 1 Resumo estruturado + 5 últimos turnos (12k tokens).

### RF-004: Captura e Reuso de Memória Cross-Session
- **Description:** O harness deve permitir gravar lições aprendidas (ex: `AddMemoryAsync(repoId, topic, fact)`).
- **Rules:** Durante o início de qualquer run no mesmo repositório, memórias relevantes ao tópico são injetadas em uma seção `<project_memory>`.
- **Input → Output:** Query do usuário sobre "deploy" → Injeta memórias salvas com tag `deploy`.

---

## 5. API Contract

```http
POST /api/harness/context/compile
Content-Type: application/json
{
  "worktreePath": "/home/ubuntu/.taskboard/worktrees/run_01j7abcde",
  "agentType": "Claude",
  "maxTokenBudget": 32000
}
→ 200 OK
{
  "systemPrompt": "# Instructions\n...",
  "estimatedTokens": 4120,
  "injectedRules": ["CLAUDE.md", "global-rules.md"],
  "memoriesInjectedCount": 3
}

POST /api/harness/memory
Content-Type: application/json
{
  "repositoryFullName": "afonsoft/taskboard-ai",
  "topic": "build-system",
  "content": "Sempre executar rm -rf publish/wwwroot/_framework antes de rodar dotnet publish",
  "tags": ["build", "blazor", "publish"]
}
→ 201 Created
```

---

## 6. Acceptance Criteria

- [x] **Given** um repositório com `AGENTS.md` e `CLAUDE.md`, **when** o compilador é executado, **then** as instruções do projeto são injetadas no início do prompt.
- [x] **Given** um histórico de mensagens que atinge 85% do limite de tokens, **when** `CompactIfNeededAsync` é chamado, **then** a contagem de tokens é reduzida para menos de 50% através da síntese das mensagens antigas.
- [x] **Given** uma lição persistida no banco com tag `tests`, **when** um novo run com escopo de testes é inicializado, **then** a lição aparece no bloco `<project_memory>`.

---

## 7. Task Plan

- [x] **T1 — Contracts & DTOs:** Definir `IProjectContextCompiler`, `IContextCompactor`, `IMemoryService`.
- [x] **T2 — Prompt Compiler Implementation:** Implementar descoberta de arquivos de instrução e injeção de `<env>` e git context.
- [x] **T3 — Context Compactor:** Implementar estratégias de remoção de payload repetido e síntese de histórico.
- [x] **T4 — SQLite Memory Store:** Implementar repositório e endpoints REST de memórias cross-session.
- [x] **T5 — Testes Unitários:** Testes cobrindo token counting, compactação e compilação hierárquica.

---

## 8. Organization Guardrails

- Nunca incluir segredos, senhas ou tokens de API lidos do ambiente no texto do System Prompt.
- Respeitar o limite de 500 linhas para arquivos de memória gerados automaticamente.

---

## 9. Definition of Done

- [x] Motor de compilação de contexto testado e aprovado com xUnit.
- [x] Compactador de histórico operacional em cenários de alta volumetria de mensagens.
- [x] Memória cross-session persistida em SQLite e recuperável por repositório.
