# SPEC-20260911 — Refinar GitHub Actions

## Metadata

- Feature: Refine GitHub Actions
- Product: agent-harness
- Bounded context: CI/CD
- Suggested branch: feature/refine-github-actions
- Status: Done — entregue via PRs #52–#56 (bumps de actions); verificado em gap-analysis-20260919
- Date: 2026-09-11
- Technical owner: afonsoft
- Target agent: Devin

## Objective

Corrigir versões das Actions usadas em `.github/workflows/dotnet.yml`, garantir permissões mínimas e manter o build/teste/cobertura/vulnerabilidades intactos.

## Expected outcome

- Todas as `uses:` em `dotnet.yml` usam versões existentes e estáveis.
- O job `vulnerabilities` declara `permissions: contents: read`.
- `dotnet build Taskboard.sln` continua passando sem warnings.

## Out of scope

- Novas steps de deploy, notificação ou publicação.
- Alterações em `codeql.yml`, `code-quality.yml` ou `dependabot.yml`.

## Restrictions

- Nunca expor secrets no YAML.
- Não alterar a lógica de coverage nem o threshold atual sem justificativa.

## Functional requirements

- **FR-001 — Atualizar versões de Actions**  
  Substituir `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/cache@v6`, `actions/upload-artifact@v7` e `codecov/codecov-action@v7` por versões `v4`.

- **FR-002 — Declarar permissões mínimas**  
  Adicionar `permissions: contents: read` ao job `vulnerabilities`.

## Acceptance criteria

- `dotnet build Taskboard.sln` passa sem warnings.
- `yamllint` (se disponível) ou `dotnet build` não quebra.
- Nenhum secret é adicionado.
