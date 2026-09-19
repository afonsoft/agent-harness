# Gap Analysis — 2026-09-19 (ADE & Agent Harness Evolution)

- Repository: `/home/ubuntu/repos/taskboard-ai` | Branch: `feature/devin-20260919-web-cli-agent` | Commit: `fa06ac4`
- Phase reached: `gate`
- Mode: `full`

---

## 1. Source Inventory

| Source | Status | Notes |
| --- | --- | --- |
| `.specs/` | `present` | 87 arquivos SPEC (75 concluídos/históricos, 12 pendentes: 1 Approved, 11 Drafts) |
| `docs/` | `present` | Documentação bilíngue sincronizada (en-us e pt-br) |
| `docs/architecture/` | `present` | Diagramas arquiteturais Mermaid e especificações conceituais |
| `.claude/CONTEXT.md` | `present` | Estratégia de carregamento e governança de contexto |
| `.claude/MEMORY.md`, `.claude/memory/` | `present` | 5 relatórios de gap analysis anteriores arquivados |
| `CLAUDE.md` / `AGENTS.md` / `README.md` | `present` | Branding Harness consolidado, regras e convenções |
| Tests / Linters / CI | `present` | 460 unit tests + 156 integration tests verdes; build 0 warnings |
| `gh auth` + remotes | `ok` | Autenticado como afonsoft/taskboard-ai; **0 PRs abertos no GitHub** |

---

## 2. AS-IS × TO-BE Matrix

| Tópico | AS-IS (Código / Testes / Implementado) | TO-BE (Specs / Arquitetura Alvo) | Fontes |
|---|---|---|---|
| **Web CLI Agent** | `/ai-chat` usa `MockLLMProvider` e enfileiramento linear one-shot. | Sessão interativa persistente com CLI externo via JSON-RPC/ACP stdio, steer/queue e permissões inline. | `SPEC-20260919-web-cli-agent.md`, Epic #153 (#154-#159) |
| **Workspace Isolation** | Execução de agentes roda diretamente no `RepoPath` raiz compartilhado. | Git Worktrees isolados por run em `~/.taskboard/worktrees/{runId}` com branch dedicada `feature/agent-{runId}`. | `SPEC-20260919-harness-workspace-isolation.md` |
| **Verification Loop** | Exit code 0 move issue para Review mesmo se build/testes quebrarem. | Motor automático de compilação (`TreatWarningsAsErrors`), testes xUnit e ratchet de cobertura com feedback loop. | `SPEC-20260919-harness-verification-loop.md` |
| **Multi-Agent DAG** | 1 único agente por issue em batch linear. | Pipelines em DAG de equipes especializadas (`Architect` → `Builder` → `Tester` → `Reviewer`) com context handoff. | `SPEC-20260919-ade-multi-agent-orchestration.md` |
| **Cockpit & HITL** | Terminal escuro de logs puros (`TaskLogTab.razor`). | Cockpit WASM com stream de eventos tipados, visualizador side-by-side de Git Diff, steer e gates de aprovação. | `SPEC-20260919-ade-cockpit-hitl.md` |
| **Context & Memory** | Concatenação estática de Branch, Scope e Instructions em string simples. | Compilação hierárquica (`AGENTS.md`/`CLAUDE.md`), compactador anti-rot e SQLite cross-session memory. | `SPEC-20260919-harness-context-memory.md` |
| **Security Gateway** | Apenas checagem estática de variáveis de ambiente (`WithoutTaskboardEnv`). | Classificador dinâmico de comandos de shell pré-fork (`Safe`/`Write`/`Dangerous`), path jail e secret scrubber. | `SPEC-20260919-harness-security-permission-gateway.md` |
| **Living Specs** | Markdown estático solto na pasta `.specs/` sem modelagem de domínio. | Motor de Living Specs na UI (`/specs`), parser Markdig, detecção de drift e rastreamento de BDDs. | `SPEC-20260919-ade-living-specs.md` |
| **FinOps & OTel** | Sem telemetria estruturada nem controle de gastos ou tokens. | Spans OpenTelemetry, precificação por modelo, cálculo de custos em USD e travas de teto orçamentário. | `SPEC-20260919-ade-observability-finops.md` |
| **CLI DB Metrics** | Sem acesso às sessões salvas nos bancos de dados locais dos CLIs. | Camada de leitura segura somente-leitura dos SQLite locais de Devin, Codex, OpenCode e Claude. | `SPEC-20260919-cli-db-reader.md`, `cli-metrics.md` |

---

## 3. Candidatos e Veredictos

| Key | Categoria | Veredito | Prioridade | Spec | Issue | Evidência |
|---|---|---|---|---|---|---|
| `GAP-implementation-web-cli-agent` | implementation | **CONFIRMADO** | high | `SPEC-20260919-web-cli-agent.md` | Epic #153 (#154-#159) | SPEC Approved; 6 slices abertos; ausente em `src/Taskboard.Server/Services/` |
| `GAP-harness-workspace-isolation` | architecture | **CONFIRMADO** | high | `SPEC-20260919-harness-workspace-isolation.md` | — | Execução em `KnownCliAgentAdapter.cs:33` aponta para `RepoPath` raiz sem worktree |
| `GAP-harness-verification-loop` | tests | **CONFIRMADO** | high | `SPEC-20260919-harness-verification-loop.md` | — | `AgentOrchestrationService.cs` move para Review baseado apenas em ExitCode do CLI |
| `GAP-ade-multi-agent-orchestration` | architecture | **CONFIRMADO** | high | `SPEC-20260919-ade-multi-agent-orchestration.md` | — | Zero suporte a DAG ou handoff de papéis em `Taskboard.Application` |
| `GAP-ade-cockpit-hitl` | frontend | **CONFIRMADO** | high | `SPEC-20260919-ade-cockpit-hitl.md` | — | `TaskLogTab.razor` renderiza apenas texto cru; sem diff viewer ou cards estruturados |
| `GAP-harness-security-permission-gateway` | security | **CONFIRMADO** | high | `SPEC-20260919-harness-security-permission-gateway.md` | — | Sem parser AST pré-fork para classificar comandos perigosos de bash |
| `GAP-harness-context-memory` | architecture | **CONFIRMADO** | medium | `SPEC-20260919-harness-context-memory.md` | — | `KnownCliAgentAdapter.cs:42-59` não injeta regras de projeto nem memória cross-session |
| `GAP-ade-living-specs` | requirements | **CONFIRMADO** | medium | `SPEC-20260919-ade-living-specs.md` | — | Nenhum endpoint REST ou componente Blazor para `.specs/` |
| `GAP-ade-observability-finops` | observability | **CONFIRMADO** | medium | `SPEC-20260919-ade-observability-finops.md` | — | Nenhuma tabela de métricas de custo ou tokens em `Taskboard.EntityFrameworkCore` |
| `GAP-implementation-cli-metrics` | implementation | **CONFIRMADO** | low | `SPEC-20260919-cli-db-reader.md` | — | Leitores de DB SQLite locais ainda em spec draft |

---

## 4. Status de Pull Requests

- **Verificação via `gh pr list`:** **0 Pull Requests abertos**.
- O último PR foi o `#152` (`docs(memory): deploy lesson + ratchet/CLI-test decisions`), que já foi aprovado e mergeado com sucesso na `main`.
- Não há PRs pendentes de merge no repositório remoto.

---

## 5. Próxima Etapa / Gate de Decisão

A frente de trabalho imediata já aprovada é a **Epic #153 (Web CLI Agent)**, composta por 6 slices:
1. **#154 (S1)**: Domain/contratos + migration `AddWebCliAgent`
2. **#155 (S2)**: `AcpSessionClient` + capability nos adapters
3. **#156 (S3)**: `AgentSessionManager` + `PermissionGate` + endpoints + SSE
4. **#157 (S4)**: UI: modo agent + renderers tipados + steer/queue/Stop
5. **#158 (S5)**: Capability + fallback one-shot
6. **#159 (S6)**: Verificação, docs e PR

As demais 11 SPECs do ADE e Harness Platform estão estruturadas em `Draft` prontas para virarem Epics subsequentes na ordem de dependência definida no `CAPABILITY-MAP.md`.
