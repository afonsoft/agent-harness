---
name: agent-harness-global
---

# agent-harness — Global Rules

> Compatível com Claude Code e Devin CLI.

## Escopo do Agent

- Implementar, revisar e documentar o `agent-harness` em C# 14 / .NET 10 seguindo as specs em `.specs/`.
- Criar branches, commits e PRs; nunca push direto em branches protegidas.

## Hard Rules

1. **Secrets**: nunca logar, commitar ou expor tokens, senhas ou API keys.
2. **Specs**: toda mudança de contrato/arquitetura deve ser refletida em `.specs/`.
3. **Tests**: features/bugfixes precisam de testes (unit/integration).
4. **Coverage**: gate ratchet — `COVERAGE_THRESHOLD` em `dotnet.yml` nunca desce e sobe a cada sprint até ≥80% (meta 90%). Hoje: 65%.
5. **Build**: `dotnet build` com `TreatWarningsAsErrors`.
6. **Don't**: não criar `DEVIN.md`, `AGENTS.md`, `.cursorrules`, `.geminiignore`, etc.

## Soft Rules

1. Modificar `common.props` → avisar no PR.
2. Adicionar pacote NuGet → justificar no PR.
3. Mudar rota HTTP → documentar breaking change.
4. **SPEC lifecycle**: PR que entrega uma SPEC marca `Status: Done` (com
   referência ao PR) no mesmo PR — nunca deixar SPECs entregues em
   `Approved`/`Draft` (anti-regressão, SPEC-20260919-stale-spec-status).
   Rodar `./scripts/check-spec-status.sh` antes de abrir PR para detectar
   drift (`--fix` corrige a linha de Status quando há PR merged evidenciado).

## Planejamento Obrigatório

Antes de qualquer modificação, apresentar Execution Plan com:
1. Goal and context
2. Impacted files/modules
3. Implementation strategy
4. Risks and mitigations
5. Validation steps (build, test, lint)
6. Rollback plan

## Comportamento

- Código e commits em inglês; testes BDD e documentação podem ser em português.
- Prefira minimal changes; não refatore sem necessidade.
- Execute `dotnet build` e `dotnet test` após alterações relevantes.
