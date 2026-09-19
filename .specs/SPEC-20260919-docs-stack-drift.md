# SPEC-20260919 — Docs Stack Drift: CLAUDE.md/docs ainda citam System.CommandLine e arquitetura removida

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `docs-stack-drift` |
| Type | `Docs` |
| Stack | `Markdown` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `docs/devin-20260919-docs-stack-drift` |
| Ticket | `GAP-documentation-docs-stack-drift` (gap-analysis-20260919) — Issue #142, Epic #140 |
| Status | `Approved` — aprovada pelo usuário (2026-09-19, gate gap-analysis) |

Origin: gap-analysis-20260919. A CLI migrou para **Spectre.Console.Cli** em
2026-08-31 (`.specs/cli-migration.md`, `SPEC-003` §line 322 "Resolvido:
Spectre.Console.Cli"; `src/Taskboard.Cli/Program.cs:3` + csproj), mas
documentos-norma continuam dizendo `System.CommandLine`. Além disso,
`docs/architecture/architecture.md` descreve agregados removidos pelo
`projects-removal` e o motor de workflow antigo — ambos obsoletos.

## 1. User Story

**As a** agente ou dev que lê CLAUDE.md/docs como contrato,
**I want** que stack e arquitetura documentadas reflitam o código real,
**so that** decisões de implementação não partam de um TO-BE fantasma.

## 2. Scope

### In scope (todos docs-only)

- `CLAUDE.md` tabela Stack: `CLI | System.CommandLine` → `Spectre.Console.Cli` (pin 0.49.1 em `Directory.Packages.props`).
- `docs/technologies.md:11` e `docs/packages.md:13` (+ mirrors `*.pt-br.md`): `System.CommandLine` → `Spectre.Console.Cli`.
- `docs/architecture/architecture.md`:
  - "taskctl CLI … `System.CommandLine`" → Spectre.Console.Cli.
  - Aggregates `Project`/`Task` no Domain → removidos; board é GitHub-backed (issues + labels são a fonte da verdade); aggregates reais hoje: `AiChatThread`, `AgentRun`, `IssueHistoryEvent`, `ConfigurationOverride`, `User`/`AgentPreference` (verificar DbContext).
  - "Serving the SPA frontend as static files" → Blazor WebAssembly (`Taskboard.Blazor` WASM, `Taskboard.Client`).
  - "Workflow Engine: graph-based engine que automatiza transições" → `/workflow` é monitor read-only de GitHub Actions (Octokit `Actions.*`); a superfície legada está em remoção (SPEC-20260919-legacy-workflow-surface).
  - Conferir `architecture.html`/`architecture.json` — se geram o .md, atualizar a fonte.
- `docs/features.md` + `features.pt-br.md`:
  - `## Automation`: "Workflow workspaces (JSON board config keyed by workspace id)" → monitor GitHub Actions.
  - `## MCP Server`: "4 tools" → contagem real (`[McpServerTool]` em `src/Taskboard.Mcp/Tools/TaskboardTools.cs` = 5 hoje; fixar texto sem número ou com o número correto — decidir na implementação).

### Out of scope

- Mudanças de código.
- `docs/superpowers/` (planos históricos datados — registro, não contrato).
- Reescrita estrutural da architecture.html.

## 3. Technical Context

- Fonte de verdade do stack: `src/Taskboard.Cli/Taskboard.Cli.csproj` (`Spectre.Console` + `Spectre.Console.Cli`), `.specs/cli-migration.md`.
- Entidades atuais: `src/Taskboard.Domain/Entities/` + `TaskboardDbContext`.
- SPEC-003 já documenta Spectre como decisão — docs devem convergir para ela.

## 4. Functional Requirements

| ID | Requirement |
|---|---|
| RF-001 | Nenhuma menção a `System.CommandLine` como stack atual em `CLAUDE.md`, `docs/technologies*.md`, `docs/packages*.md`, `docs/architecture/`. |
| RF-002 | `architecture.md` não lista `Project`/`Task` como agregados do Domain e não descreve o antigo motor de workflow como arquitetura atual. |
| RF-003 | Frontend descrito como Blazor WebAssembly, não SPA. |
| RF-004 | `features*.md` Automation/MCP refletem o estado real (GH Actions monitor; toolset MCP atual). |
| RF-005 | Mirrors pt-br atualizados junto com os en-us (convenção do repo). |

## 5. Acceptance Criteria

- **AC-1** `grep -rn "System.CommandLine" CLAUDE.md docs/` retorna apenas menções históricas contextuais (ex.: nota de migração), se houver — nunca como stack vigente.
- **AC-2** `architecture.md` descreve agregados que existem em `src/Taskboard.Domain/Entities/`.
- **AC-3** Cada arquivo en-us alterado tem o mirror pt-br alterado no mesmo PR.
- **AC-4** `dotnet build` verde (docs não quebram nada; sanity check).

## 6. DoD

- [ ] RF-001..005 implementados.
- [ ] Diff revisado: só Markdown.
- [ ] SPEC → `Status: Done`; PR merged.
