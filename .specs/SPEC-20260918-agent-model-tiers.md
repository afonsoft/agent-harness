# SPEC-20260918-agent-model-tiers

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `agent-model-tiers` |
| Type | `Feature` |
| Stack | `.NET 10 / Blazor` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260918-agent-model-tiers` |
| Ticket | — |
| Status | `Draft` |

## 1. User Story

**As a** administrador executando uma issue com um agente CLI
**I want** escolher o tier de modelo (Lite / Normal / Ultra) junto à seleção do Agent CLI
**So that** eu controle o custo/qualidade de cada execução sem precisar conhecer o nome exato do modelo de cada CLI.

**Problem context:**
Hoje o `AgentConfigTab` permite escolher apenas o CLI — o modelo fica no default de cada ferramenta. O usuário quer um combo de 3 tiers, mapeado para um modelo real de cada CLI que aceite flag de modelo, e um badge no card do board mostrando qual CLI está executando e seu estado.

## 2. Scope

**In scope:**
- Enum `AgentModelTier` (`Lite`, `Normal`, `Ultra`) + tabela curada `AgentCliModels`: `AgentType → (modelFlag, tier → modelName)`.
- `AgentExecutionRequest` ganha `ModelTier` (default `Normal`).
- `AgentCliInvocation.BuildArguments` injeta a flag de modelo quando o tier mapeia para a CLI.
- `AgentConfigTab`: combo **Modelo** com Lite/Normal/Ultra + tooltip mostrando o modelo real resolvido para a CLI selecionada; combo desabilitado ("gerenciado pela CLI") quando a CLI não aceita flag.
- Preview do comando reflete a flag de modelo.
- Modal da issue mais largo (ex.: `modal-80` ~80vw ≥768px).
- Card do board: badge do agente passa a exibir CLI + estado (Running com spinner/pulse, Queued, terminal states) — já existe `agent-badge`; estender com status textual.

**Out of scope:**
- Campo de modelo livre/custom (aprovado: mapeamento curado apenas).
- Listar modelos via API da CLI em runtime (tabela é estática, validada contra `--help` na implementação).
- Persistir tier por issue/usuário (default Normal sempre; localStorage por issue só para prompt, como hoje).
- Fallback automático para outro modelo se a CLI rejeitar o nome.

## 3. Technical Context

**Where the change happens:**
`AgentCliInvocation` (contracts) é a fonte única do argv por CLI — a flag de modelo entra lá. `AgentExecutionRequest` atravessa `AgentOrchestrationService.EnqueueAsync` até o `KnownCliAgentAdapter`, que monta o processo. UI: `AgentConfigTab` (combo + preview), `TaskDetailDialog`/`KanbanBoard` (tamanho do modal, badge do card).

**Files to read before implementing:**
- `src/Taskboard.Application.Contracts/Agents/AgentCliInvocation.cs`
- `src/Taskboard.Application.Contracts/Agents/AgentExecutionRequest.cs`
- `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor`
- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` (badge `agent-badge`, linhas ~120-140, helpers `GetAgentBadgeClass`/`GetAgentShortName`)
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` + `KnownCliAgentAdapter.cs`
- `src/Taskboard.Client/wwwroot/css/site.css` (`.modal-70`, `.agent-badge`)

**Files to create or modify:**
```text
src/Taskboard.Application.Contracts/Agents/AgentModelTier.cs        (novo)
src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs        (novo: tabela curada)
src/Taskboard.Application.Contracts/Agents/AgentCliInvocation.cs    (flag de modelo no argv)
src/Taskboard.Application.Contracts/Agents/AgentExecutionRequest.cs (campo ModelTier)
src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs      (propagar tier → AgentRun)
src/Taskboard.Domain/Agents/AgentRun.cs + DTO + EF config           (persistir Model/Tier — migration)
src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor         (combo + preview)
src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor            (badge estado)
src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor       (modal-80)
src/Taskboard.Client/wwwroot/css/site.css                           (.modal-80, badge running)
tests/Taskboard.Tests.Unit/Agents/                                 (tier mapping + argv)
tests/Taskboard.Tests.Integration/                                 (request com tier)
```

## 4. Requirements

### RF-001: `AgentModelTier` + tabela curada `AgentCliModels`
- **Description:** Enum `Lite|Normal|Ultra` (pt-BR UI: `Lite`, `Normal`, `Ultra`) e mapa estático por `AgentType` com a flag de modelo e o modelo de cada tier.
- **Rules:** CLIs sem flag de modelo documentada → `SupportsModelSelection=false` (combo desabilitado, argv inalterado). Nomes exatos de modelo validados contra `cli --help` da versão instalada na implementação; entradas incertas marcadas `// validate`.
- **Tabela inicial (validar na implementação):**

| CLI (`AgentType`) | Flag | Lite | Normal (default) | Ultra |
|---|---|---|---|---|
| Claude | `--model` | `haiku` | `sonnet` | `opus` |
| Codex | `-m` | `gpt-5.1-codex-mini` | `gpt-5.1-codex` | `gpt-5.1-codex-max` |
| OpenCode | `-m` | model barato do provider configurado | provider default | provider top |
| Devin | — | *CLI-managed* | | |
| Antigravity (`agy`) | `--model` | `gemini-3-flash` | `gemini-3-pro` | `gemini-3-pro` (deep) |
| Kimi | `--model` | lite do kimi | `kimi-k2` | `kimi-k2-max` |
| Grok | `--model` | `grok-4-fast` | `grok-4` | `grok-4-heavy` |
| Aider | `--model` | `deepseek` | `sonnet` | `opus` |
| Cline | — | *CLI-managed* | | |
| Continue (`cn`) | `--model`? | *validar; se ausente → CLI-managed* | | |
| Copilot | `--model` | cheap copilot | `claude-sonnet-4.5` | `gpt-5.1` |
| Qwen | `-m`/`--model` | `qwen3-coder-flash` | `qwen3-coder-plus` | `qwen3-max` |
| Kiro (`kiro-cli`) | `chat --model` | *validar* | default | *validar* |

- **Input → Output:** `(AgentType, AgentModelTier) → string? modelName` (null quando CLI-managed).

### RF-002: Tier no request e no argv
- **Description:** `AgentExecutionRequest` ganha `AgentModelTier ModelTier` (default `Normal` — serialização compatível com requests antigos sem o campo). `AgentCliInvocation.BuildArguments(agentType, prompt, tier)` insere a flag de modelo antes do prompt quando há mapeamento.
- **Rules:** flag sempre separada do prompt (argument list, nunca string interpolation); `PreviewCommandLine` reflete o tier.
- **Input → Output:** request com `ModelTier=Lite` + `AgentType=Claude` → argv `claude --dangerously-skip-permissions --model haiku -p <prompt>`.

### RF-003: Persistir o tier no `AgentRun`
- **Description:** `AgentRun`/`AgentRunDto` registra `ModelTier` (e o `ModelName` resolvido) para auditoria — histórico mostra qual modelo rodou.
- **Rules:** migration EF (`AgentRuns` novas colunas nullable); runs antigas → null = "modelo default da CLI".

### RF-004: UI — combo + preview + modal maior
- **Description:** `AgentConfigTab` ganha select **Modelo** (Lite/Normal/Ultra, default Normal) entre o select de Agente e o preview; tooltip/texto auxiliar mostra o modelo real (`haiku`, `sonnet`…); desabilitado quando CLI-managed. Modal da issue sobe para `modal-80` (novo CSS ~80vw ≥768px).
- **Rules:** tier selecionado vai no `AgentExecutionRequest`; preview atualiza ao trocar tier/agente.

### RF-005: Badge de execução no card
- **Description:** No `KanbanBoard`, o `agent-badge` existente passa a exibir o estado ao lado do tipo do CLI: `Running` → spinner/pulse + "running", `Queued` → "queued", estados terminais → badge muted com o estado (como hoje, mas com texto de estado visível).
- **Rules:** sem nova query por card — reusa `GetLatestRunsAsync` já carregado; badge atualiza quando a lista de runs recarrega.

**Business rules / invariants:**
- `Normal` é sempre o default — execução sem tier explícito equivale a Normal.
- Modelo resolvido é determinístico: mesma `(AgentType, Tier)` → mesmo `modelName`.
- CLI sem suporte nunca recebe flag de modelo no argv.

## 5. API Contract

Sem novos endpoints. `POST /api/agents/executions` aceita o campo `modelTier` adicional no body (retrocompatível). `GET /api/agents/runs` inclui `modelTier`/`modelName` nos itens.

## 6. Acceptance Criteria

- **Dado** uma issue com Claude selecionado e tier Lite, **quando** executo, **então** o argv contém `--model haiku` e o `AgentRun` persiste `ModelTier=Lite`, `ModelName=haiku`.
- **Dado** uma CLI sem flag de modelo (Devin/Cline), **quando** abro o Agent Config, **então** o combo Modelo aparece desabilitado com "gerenciado pela CLI" e o argv não muda.
- **Dado** tier Normal selecionado (default), **quando** o preview renderiza, **então** mostra a flag do modelo Normal da CLI.
- **Dado** uma execução Running, **quando** vejo o card no board, **então** o badge mostra o CLI + indicador de execução.
- **Edge:** `AgentRun` antigo sem tier → DTO retorna null → histórico mostra "default".

## 7. Task Plan

1. **T1** — `AgentModelTier` + `AgentCliModels` (tabela validada via `--help` dos CLIs instalados no host) + testes de mapeamento.
2. **T2** — `BuildArguments`/`PreviewCommandLine` com tier + testes de argv por CLI.
3. **T3** — `AgentExecutionRequest.ModelTier` + propagação orchestrator/adapter + `AgentRun.ModelTier/ModelName` + migration + testes.
4. **T4** — `AgentConfigTab` combo + `modal-80` + badge estado no card + CSS.
5. **T5** — Build + suites + validação do argv real com `cli --help` no host.
6. **T6** — Docs bilíngues + PR + merge + deploy.

## 8. Organization Guardrails

- Branch `feature/devin-20260918-agent-model-tiers`; nada em `main`.
- Nomes de modelos NUNCA hardcoded fora de `AgentCliModels` (fonte única).
- Sem secrets; flags vão pelo argument list (sem shell).
- Migration nova; nunca editar migration aplicada.

## 9. Definition of Done

- [ ] Combo Lite/Normal/Ultra funcional no Agent Config, desabilitado para CLIs sem flag.
- [ ] Argv/preview refletem o tier; `AgentRun` persiste tier+modelo.
- [ ] Badge do card exibe CLI + estado de execução.
- [ ] Modal da issue ~80vw.
- [ ] Testes unit (mapeamento/argv) + integration (request com tier) verdes; build sem warnings.
- [ ] Tabela de modelos validada contra `--help` dos CLIs instalados.
- [ ] Docs en/pt-br + SPEC `Done`; PR merged + deploy.
