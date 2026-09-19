# MEMORY.md — State Management

## Decisões Técnicas

| Data | Decisão | Motivo | Alternativas Descartadas |
|---|---|---|---|
| 2026-08-24 | .NET 10 / C# 14 | Alinhado com .NET unification | Manter Node.js original |
| 2026-08-24 | ABP N-Layer DDD | Convenções afonsoft | Clean Architecture pura |
| 2026-08-24 | EF Core + SQLite | Local-first, portátil | PostgreSQL (muito pesado) |
| 2026-08-24 | Minimal APIs | Simplicidade e performance | Controllers tradicionais |
| 2026-09-10 | Kanban GitHub via labels (Octokit + MudBlazor) | Sem estado extra; GitHub como fonte da verdade | GitHub Projects API (GraphQL) |
| 2026-09-10 | Orquestração de agentes CLI: `BackgroundService` + `Channel<T>` + SignalR | Não bloquear threads da UI; streaming por grupo `issueId` | Polling HTTP; `IHostedService` com fila própria |
| 2026-09-10 | `IAgentLogBroadcaster` em Application.Contracts, implementado no Server | Evitar dependência `Integrations → Server` | Referenciar `IHubContext` direto em Integrations |
| 2026-09-10 | Skills `afonsoft/skills` instaladas em `.claude/skills` e `.devin/skills` (cópia, `skills-lock.json`) | Plataformas declaradas em CLAUDE.md; evitar 50+ pastas de IDEs | `npx skills add --all` sem filtro |
| 2026-09-13 | Skills atualizadas via `npx skills update` (cópias reais em `.claude/skills`, lockfile regenerado); slash commands vendorados em `.claude/commands` | Upstream renomeou `grill-me-with-spec`→`write-specs`, `execute-tdd-spec`→`execute-spec` e adicionou `mermaid-architecture` | Symlinks para `.agents/skills` (store gitignored) |
| 2026-09-19 | Coverage gate vira ratchet: `COVERAGE_THRESHOLD` em `dotnet.yml` só sobe — 45→65 (baseline medido 66.26%), próximos degraus 70→75→80 (meta 90%) | Eliminar contradição gate 45% vs hard rule ≥80%; aprovação do usuário "subir gradualmente" (SPEC-20260919-coverage-gate-ratchet, #145/#150) | Pular direto para 80% (falharia: 66.26% real) |
| 2026-09-19 | Testes de CLI com `Spectre.Console.Cli.Testing` (`CommandAppTester`) + `InternalsVisibleTo` + `Program.ConfigureCommands` extraído | Testes reutilizam o registro de produção; guard por reflection cobre placeholders `CommandArgument` crus | `Spectre.Console.Testing` (só tem `TestConsole`, não `CommandAppTester`) |

## Débitos Técnicos

| Item | Impacto | Prioridade |
|---|---|---|
| Persistência real de anexos | Médio | Média |
| Integração LLM real | Alto | Média |
| UI Blazor/MAUI | Alto | Baixa (fase 2) |

## Lições Aprendidas

| Contexto | Erro | Como Evitar |
|---|---|---|
| Specs | Duas pastas `.specs` e `.specs2` causaram confusão | Unificar via merge e SDD |
| SPECs | SPECs entregues ficando em `Approved` (2 recorrências) | Convenção: PR de entrega marca `Status: Done` no mesmo PR (global-rules.md soft rule 4) |
| Spectre.Console.Cli | `[CommandArgument(0, "name")]` cru vira markup e quebra todo `--help` | Template `"<name>"`/`"[name]"`; guard por reflection em `CliSmokeTests` |
| Deploy Blazor WASM | `dotnet publish -o` não limpa `_framework/` — bundles fingerprinted acumulam e o runtime pode resolver manifest stale → NotFound em rota existente (Board `/`, 2026-09-19) | `rm -rf publish/wwwroot/_framework` antes do publish; usuário faz `Ctrl+F5` (aba aberta mantém WASM antigo em memória) |

## Políticas de Limpeza

- Memórias de branches deletadas devem ser descartadas.
- Fatos desatualizados devem ser removidos.
- Nunca armazenar PII, secrets ou credenciais.

## Tiers de Memória

| Tier | Persistência | Conteúdo | Implementação |
|---|---|---|---|
| Procedural | Sempre | Como trabalhar | CLAUDE.md, rules |
| Semantic | Sob demanda | Fatos, padrões | `.specs/`, `docs/`, `.claude/knowledge/` |
| Episodic | Cross-session | Experiências | MEMORY.md |
