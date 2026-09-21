# Harness do agent-harness

Estrutura de harness para agentes LLM (Claude Code, Devin CLI, Google Antigravity).

## Estrutura

```text
.claude/
├── settings.json          # Permissões do Claude Code
├── CONTEXT.md             # Estratégias de carregamento de contexto
├── RULES.md               # Guardrails
├── MEMORY.md              # Estado cross-session
├── TOOLS.md               # Ferramentas e MCP
├── WORKFLOWS.md           # Automação
├── README.md              # Este arquivo
├── rules/
│   └── global-rules.md    # Regras always-on
├── agents/
│   ├── plan.md            # Sub-agent de planejamento
│   ├── review.md          # Sub-agent de revisão
│   └── test.md            # Sub-agent de testes
└── skills/
    └── taskboard/
        └── SKILL.md       # Skill principal do projeto

.devin/
└── config.json            # Configuração do Devin CLI (lê .claude/)

.agent/
└── skills/
    └── taskboard/
        └── SKILL.md       # Symlink para .claude/skills/taskboard/SKILL.md
```

## Compatibilidade

| Plataforma | Principal | Skills | Rules |
|---|---|---|---|
| Claude Code | `CLAUDE.md` | `.claude/skills/` | `.claude/rules/` |
| Devin CLI | `CLAUDE.md` + `.devin/config.json` | `.claude/skills/` (importado) | `.claude/rules/` |
| Google Antigravity | `CLAUDE.md` (compatível) | `.agent/skills/` (workspace) | `CLAUDE.md` |

## Como adicionar uma nova skill

1. Criar pasta `lowercase-kebab-case` (max 64 chars) em `.claude/skills/`.
2. Adicionar `SKILL.md` com YAML frontmatter (`name`, `description`).
3. `name` no frontmatter deve ser igual ao nome da pasta.
4. Opcionalmente adicionar `references/`, `templates/`, `scripts/`, `assets/`.
5. Para Antigravity, criar symlink em `.agent/skills/`.

## Como executar o verification loop

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

## Exclusões

- Não criar `.claudeignore` ou `.devinignore`.
- Exclusões de descoberta são controladas por `permissions.deny` em `.claude/settings.json` e `.devin/config.json`.
