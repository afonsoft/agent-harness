# SPEC-20260918-harness-rebranding

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `harness-rebranding` |
| Type | `Refactor` (Docs/Branding) |
| Stack | `.NET 10 / Blazor / Docs` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260918-harness-rebranding` |
| Ticket | — |
| Status | `Draft` |

## 1. User Story

**As a** mantenedor do projeto
**I want** que o produto se apresente como **Harness** (Harness Engineering) no título, README e informações do sistema
**So that** a identidade reflita que ele é uma plataforma de harness para agentes de IA — o taskboard é uma das capacidades, não o todo.

**Problem context:**
O projeto evoluiu de "clone do dashi-taskboard" para uma plataforma que gerencia CLIs de agentes, terminal, VS Code Web, MCP, skills e execução de tasks — "taskboard" não descreve mais o produto. Aprovado: rename **somente de branding/docs** — namespaces `Taskboard.*`, csproj, nome do repositório e paths de produção (`~/.taskboard`) permanecem.

## 2. Scope

**In scope:**
- Título do app (`<title>` → `Harness`), cabeçalho/branding na UI (NavMenu, telas de login/empty states se exibirem o nome).
- `README.md` + `README.pt-br.md`: reescrito como **Harness** — mais completo, descrevendo todas as capacidades atuais (board GitHub, board local, agentes CLI, terminal multi-abas, VS Code Web, MCP/taskctl, skills/RAG, AI chat, histórico).
- `docs/*.md` (en + pt-br): títulos e menções de produto "taskboard-ai" → "Harness" onde forem branding (texto técnico como `Taskboard:` config keys, `taskctl`, paths e namespaces permanece).
- `AGENTS.md`/`CLAUDE.md`: seção "Missão" atualizada (Harness — plataforma local-first de harness engineering para agentes de IA).
- `--app-name` do code-server (`Taskboard` → `Harness`) e textos visíveis de página (`/editor`, login).
- `docs/README.md` e metadados de sistema (meta endpoint `name`, se exposto).

**Out of scope:**
- Renomear projetos `.csproj`, namespaces `Taskboard.*`, assemblies, solution file.
- Renomear repositório GitHub, URLs (`task.afonsoft.dev`), env vars (`TASKBOARD_*`), config keys (`Taskboard:*`), paths (`~/.taskboard`), `taskctl`, serviço systemd.
- Mudança de comportamento/código funcional.

## 3. Technical Context

**Where the change happens:**
Superfície de texto/branding apenas: `index.html` (title), `NavMenu.razor`, README(s), `docs/`, `AGENTS.md`, `CodeServerProcessManager` (`--app-name`).

**Files to read before implementing:**
- `README.md`, `README.pt-br.md`, `AGENTS.md`
- `src/Taskboard.Client/wwwroot/index.html`
- `src/Taskboard.Blazor/Layout/NavMenu.razor` + páginas com nome visível (`Login`, `Home`, `Editor`)
- `src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs` (`--app-name`)
- `docs/features*.md`, `docs/api*.md`, `docs/installation*.md`, `docs/README.md`

**Files to create or modify:**
```text
README.md, README.pt-br.md, AGENTS.md, CLAUDE.md (se espelhar a missão)
src/Taskboard.Client/wwwroot/index.html
src/Taskboard.Blazor/Layout/NavMenu.razor (+ páginas com "Taskboard" visível)
src/Taskboard.Integrations/Vscode/CodeServerProcessManager.cs  (--app-name Harness)
src/Taskboard.Server/Program.cs (meta endpoint name, se houver campo name)
docs/*.md (títulos/branding)
tests/... (ajustar asserts de --app-name e de título/meta se existirem)
```

## 4. Requirements

### RF-001: Título e branding na UI
- **Description:** `<title>` vira `Harness`; qualquer texto de marca visível ao usuário (menu, login, editor) usa "Harness" ou "Harness Engineering".
- **Rules:** `code-server --app-name Harness` (atualizar teste que asserta `Taskboard`); sem alterar ids, classes CSS ou rotas.
- **Input → Output:** aba do browser mostra "Harness"; code-server intitulado "Harness".

### RF-002: README completo como Harness
- **Description:** Reescrever `README.md` (en) e `README.pt-br.md`: nome **Harness**, tagline "local-first harness engineering platform for AI agents", visão geral atualizada cobrindo todas as capacidades entregues (GitHub board + board local, 13 CLIs de agente com install/login/execute, terminal multi-abas, VS Code Web, VS Code deep-link, histórico de issues, skills + RAG MCP, AI chat, taskctl/MCP), stack, arquitetura, quick start.
- **Rules:** manter badges atuais (URLs do repo continuam `taskboard-ai`); tabelas de stack atualizadas; contagens de testes atualizadas; não prometer features inexistentes.
- **Input → Output:** README descreve o produto como Harness e reflete o estado real do `main`.

### RF-003: Docs e AGENTS.md
- **Description:** `docs/*` e `AGENTS.md`/`CLAUDE.md` — textos de produto viram "Harness"; referências técnicas (`Taskboard:` config, `taskctl`, `~/.taskboard`, `Taskboard.Server.dll`, namespaces) permanecem com nota de nomenclatura no AGENTS.md ("produto: Harness; identificadores técnicos: taskboard").
- **Rules:** grep pós-edição: nenhuma string de código/config foi renomeada por engano.

### RF-004: Meta endpoint
- **Description:** Se `/api/meta` expõe `name`/`product`, retorna "Harness" (verificar contrato; se for só versão/flag, sem mudança).
- **Input → Output:** `GET /api/meta` → `{ "name": "Harness", ... }` quando aplicável.

**Business rules / invariants:**
- Zero mudança funcional: mesmas rotas, mesmas config keys, mesmos serviços.
- Compatível com deploy existente (env/systemd/paths intocados).

## 5. API Contract

Somente `GET /api/meta` campo `name` (se existir). Sem breaking changes.

## 6. Acceptance Criteria

- **Dado** o app carregado, **quando** olho a aba do browser/menu/login, **então** vejo "Harness".
- **Dado** o README, **quando** leio, **então** o produto é Harness e todas as capacidades atuais estão descritas.
- **Dado** `grep -r "app-name"`, **quando** confiro o code-server, **então** é `Harness` e o teste passa.
- **Dado** `git grep -l "taskboard-ai" docs/ README*`, **quando** confiro, **então** restam apenas referências corretas a repo/URL/nome técnico — nunca branding.
- **Edge:** nenhum arquivo de config/env/código renomeado.

## 7. Task Plan

1. **T1** — Title/NavMenu/login/`--app-name` + testes ajustados.
2. **T2** — README.md + README.pt-br.md completos.
3. **T3** — docs/* + AGENTS.md/CLAUDE.md + nota de nomenclatura.
4. **T4** — Build + suites + grep de sanidade + PR + merge + deploy.

## 8. Organization Guardrails

- Branch `feature/devin-20260918-harness-rebranding`; nada em `main`.
- PROIBIDO renomear namespaces/csproj/repo/paths/env — aprovado branding-only.
- Textos de produto em pt-BR onde a doc é pt-BR; código em inglês.

## 9. Definition of Done

- [ ] `Harness` no título/menu/login/code-server.
- [ ] READMEs completos e fiéis ao estado atual.
- [ ] AGENTS.md com missão Harness + nota de nomenclatura.
- [ ] Zero referências de branding a "taskboard" na UI; zero renames técnicos.
- [ ] Build + testes verdes; PR merged + deploy.
