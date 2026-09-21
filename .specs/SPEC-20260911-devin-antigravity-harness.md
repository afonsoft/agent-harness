# SPEC-20260911-devin-antigravity-harness

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Devin CLI and Antigravity Harness |
| Product / System | agent-harness |
| Module / Bounded Context | Agent Harness |
| Change type | Fix / Setup |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feature/devin-antigravity-harness` |
| Technical owner | afonsoft |
| Status | Done (layout consolidado em .claude/skills — ver ACs atualizados) |
| Date | 2026-09-11 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem
O repositório possui `.claude/` e `skills-lock.json`, mas os diretórios `.devin/` e `.agent/` não estão presentes, impedindo Devin CLI e Google Antigravity de carregar skills/configuração deste repo.

### Objective
Adicionar os arquivos e diretórios mínimos para Devin CLI e Google Antigravity reconhecerem o repositório.

### Expected outcome
- `.devin/config.json` apontando para `.claude/skills` e `.claude/rules`.
- `.devin/skills/` e `.agent/skills/` presentes (symlinks/cópia ou arquivos mínimos).
- Nenhuma skill fica órfã após `npx skills add afonsoft/skills`.

### Out of scope
- Criar novas skills do zero (reutilizar as já instaladas via `skills-lock.json`).
- Modificar `CLAUDE.md` ou `.claude/rules`.

---

## 2. Agent Role

> DevOps / harness engineer (agent configuration, symlinks, CLI tooling).

---

## 3. Agent Autonomy Level

3

### Restrictions
- Nunca commitar tokens.
- Não criar `AGENTS.md` (proibido por `CLAUDE.md`).
- Não modificar `.github/workflows` sem aprovação.

---

## 4. Product Context

### Functional context
Agentes CLI devem encontrar a configuração e as skills do projeto ao iniciar uma sessão.

### Technical context
- `npx skills add afonsoft/skills` popula `.claude/skills` e `.devin/skills`.
- Devin CLI lê `.devin/config.json`.
- Google Antigravity lê `.agent/skills/`.

### Relevant stack
- Node.js `npx skills`, shell, symlinks.

---

## 5. Task Definition

### Main task
Completar o harness Devin CLI e Antigravity.

### Subtasks
- Criar `.devin/config.json` com os caminhos corretos.
- Garantir `.devin/skills/` (symlink para `.claude/skills/` ou conteúdo real).
- Garantir `.agent/skills/` (symlink para `.claude/skills/` ou conteúdo real).
- Validar que `skills-lock.json` continua como fonte da verdade.

### Do not do
- Não duplicar skills se `.claude/skills` já as contém.
- Não alterar permissões globais do usuário.

---

## 6. Functional Requirements

### FR-001: `.devin/config.json`
Arquivo JSON apontando `skillsDir` e `rulesDir` para `.claude/skills` e `.claude/rules`.

### FR-002: `.devin/skills/`
Diretório acessível contendo as skills instaladas (ou symlink para `.claude/skills`).

### FR-003: `.agent/skills/`
Diretório acessível para Google Antigravity (ou symlink para `.claude/skills`).

### FR-004: `skills-lock.json` preservado
O manifesto fixado continua valendo; nenhuma skill fica órfã.

---

## 7. Business Rules

- `CLAUDE.md` proíbe `AGENTS.md`; `AGENTS.md` não deve ser criado.
- Symlinks são preferidos para evitar duplicação.

---

## 8. Domain Modeling

N/A — configuração de arquivos.

---

## 9. Expected Architecture

```text
.devin/
  config.json
  skills/ -> ../.claude/skills
.agent/
  skills/ -> ../.claude/skills
```

Caso symlinks não sejam suportados no target, copiar conteúdo do `.claude/skills`.

---

## 10. API Contracts

N/A — configuração.

---

## 11. Testing

- Shell/CI: `test -d .devin/skills` e `test -d .agent/skills`.
- `dotnet build` não deve ser afetado.

---

## 12. Acceptance Criteria

- [x] `.devin/config.json` existe e é JSON válido (aponta para `.claude/skills` e `.claude/rules`).
- [x] Skills consolidadas em `.claude/skills/` — `.devin/skills/` e `.agent/skills/` foram removidos na consolidação (commit 62dbf81); Devin CLI consome via `.devin/config.json` e Antigravity via skills globais `~/.gemini/skills/`.
- [x] Nenhuma skill listada em `skills-lock.json` fica inacessível.
- [x] `dotnet build Taskboard.sln` continua passando.
