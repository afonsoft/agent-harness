# CLAUDE.md — taskboard-ai

## Missão

`taskboard-ai` é um clone local-first do `dashi-taskboard` (Codex Taskboard), reescrito em **C# 14 / .NET 10** com ABP N-Layer, Domain-Driven Design, SQLite/EF Core, ASP.NET Core Minimal APIs, SSE, MCP server, CLI `taskctl` e Skill para agentes.

Você é um engenheiro sênior de .NET/AI que implementa, revisa e documenta seguindo as specs em `.specs/`, as convenções `afonsoft` e este harness.

---

## Stack Tecnológica

| Camada | Tecnologia | Versão |
|---|---|---|
| Linguagem | C# | 14 |
| Runtime | .NET | 10.0 |
| Web | ASP.NET Core Minimal APIs | 10.0 |
| UI | Blazor WebAssembly + Blazor.Bootstrap | .NET 10 |
| Real-time | SignalR | ASP.NET Core |
| GitHub | Octokit | 14.0.0 |
| DDD/ABP | ABP N-Layer | 9.x |
| ORM | Entity Framework Core + SQLite | 10.0 |
| Tests | xUnit + Shouldly + NSubstitute | latest stable |
| CLI | System.CommandLine | latest stable |
| MCP | ModelContextProtocol SDK .NET | latest stable |
| Frontend | Blazor / .NET MAUI (fase 2) ou React/Vite servido (fase 1) |
| AI Chat | Abstração sobre providers (OpenAI/Claude/Azure) |
| Docs | Markdown bilíngue (en-us default, pt-br) |

---

## Estrutura do Repositório

```text
/.specs/                # Specs unificados seguindo SSD
/.claude/               # Harness Claude Code + Devin CLI
/.claude/skills/        # Catálogo de skills (afonsoft/skills + manage-taskboard)
/.claude/memory/        # Estado do orquestrador (orchestrator_stats.md)
/.devin/                # Configuração Devin CLI
skills-lock.json        # Manifesto fixado de afonsoft/skills
/docs/                  # Documentação en-us/pt-br
/.github/workflows/     # GitHub Actions CI/CD
/src/                   # Projetos .NET
/tests/                 # Projetos de teste
```

## CI/CD

- `.github/workflows/dotnet.yml`: build/test, verificação de formatação, gate de cobertura (`COVERAGE_THRESHOLD`, hoje 45%, meta 80%) e pacotes vulneráveis.
- `.github/workflows/code-quality.yml`: análise SonarCloud com `SONAR_TOKEN`.
- `.github/workflows/codeql.yml`: análise CodeQL para C# e GitHub Actions.
- `.github/dependabot.yml`: atualizações semanais de NuGet e GitHub Actions.

---

## Caminhos por Plataforma

| Plataforma | Config Principal | Skills | Rules | Knowledge |
|---|---|---|---|---|
| Claude Code | `CLAUDE.md` (always-on) | `.claude/skills/` | `.claude/rules/` (auto) | `.claude/knowledge/` |
| Devin CLI | `CLAUDE.md` + `.devin/config.json` | `.claude/skills/` (importado) | `.claude/rules/` (lido nativamente) | `.claude/knowledge/` |
| Google Antigravity IDE | `CLAUDE.md` (compatível) | `~/.gemini/skills/` (global) | `CLAUDE.md` | `.claude/knowledge/` |
| Google Antigravity CLI (agy) | `CLAUDE.md` (compatível) | `~/.gemini/antigravity-cli/skills/` (global) | `CLAUDE.md` | `.claude/knowledge/` |

---

## Padrões de Código

### DO

- Seguir `.specs/` como contrato (SPEC-driven).
- Usar ABP N-Layer: Domain → Application.Contracts → Application → EntityFrameworkCore → Server.
- Criar value objects no Domain para status, prioridade, actors, identifiers.
- Usar `long Version` com optimistic concurrency (`VERSION_CONFLICT` 409).
- Usar MediatR para commands/queries.
- Preferir `IReadOnlyList<T>` e `IReadOnlyCollection<T>` em contratos.
- Escrever testes em português: `Dado_Quando_Entao`.
- Atualizar `docs/` e specs quando alterar arquitetura/contratos.

### DON'T

- Não colocar regras de negócio em controllers/endpoints.
- Não acessar infraestrutura diretamente do Domain.
- Não commitar secrets, `.env` ou credenciais.
- Não modificar `/.github/workflows` sem aprovação humana.
- Não push direto para `main`, `master` ou `develop`.
- Não criar `DEVIN.md`, `AGENTS.md`, `.cursorrules`, `GEMINI.md`, etc.
- Não implementar código sem plano explicitado.

---

## Hard Rules

1. **Branches protegidas**: nunca commit/push em `main`, `master`, `develop`.
2. **Workflows protegidos**: não editar `/.github/workflows/**` sem aprovação humana.
3. **Secrets**: nunca logar, commitar ou expor tokens, senhas, API keys.
4. **Specs**: qualquer mudança de contrato ou arquitetura deve refletir em `.specs/`.
5. **Tests**: toda feature/bugfix deve vir com testes (unit/integração) antes do merge.
6. **Coverage**: manter ≥80% de cobertura (meta 90%).
7. **Build**: `dotnet build` com `TreatWarningsAsErrors`.

## Soft Rules

1. Modificar `common.props` ou `Directory.Build.props` → avisar no PR.
2. Adicionar novo pacote NuGet → justificar na descrição do PR.
3. Mudar rota HTTP → versionar ou documentar breaking change.

---

## Agent Loop (Plan-and-Execute)

```
1. Receber tarefa
2. Carregar CLAUDE.md + .claude/rules/global-rules.md
3. Carregar skills/rules relevantes
4. Apresentar Execution Plan — aguardar aprovação (se ambiguo)
5. Verificar guardrails
6. Executar (sandbox + permissions)
7. Verification loop: lint → dotnet build → dotnet test → CI
8. Validar resultado
9. Ajustar (máx. 2 iterações antes de escalar)
10. Atualizar MEMORY.md
```

---

## Response Style

- Idioma: **inglês** para código, comentários e commits; **português** para testes/names BDD e docs bilíngues.
- Conciso; evitar prosa desnecessária.
- Referenciar arquivos via `<ref_file>` e `<ref_snippet>` em mensagens ao usuário.

---

## Referências

- `.specs/CAPABILITY-MAP.md` — ordem de build dos módulos
- `.specs/SPEC-000-overview.md` — visão geral da migração .NET 10
- `.claude/rules/global-rules.md` — regras always-on
- `.claude/agents/review.md` — sub-agent de revisão
- `.claude/agents/plan.md` — sub-agent de planejamento
- `.claude/agents/test.md` — sub-agent de testes
- `.claude/skills/manage-taskboard/SKILL.md` — skill específica do taskboard
- `docs/README.md` — documentação do sistema
- `README.md` — quick start (en-us default)
