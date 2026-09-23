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
| 2026-09-23 | Provisioning server-side de clones: `IRepositoryProvisioningService` clona para `<WorkspaceRoot>/<repo>` no start do pipeline, valida `origin` do dir existente e rejeita estrangeiro/não-repo (422 `RepositoryProvisioningFailed`) | Bug real: `~/repos` era clone de `LangGraph-UI` — fallback ao root criou worktree do repo errado (`pipe_0bfefc2f`, falhou `No .sln found`) | Fallback silencioso ao workspace root; confiar em `repositoryPath` do client |
| 2026-09-23 | Auto-retry durável: `AutoRetryCount`+`NextAutoRetryAtUtc` por stage (default 5×1min por CLI, `Taskboard:Pipelines:AutoRetry`), rotação para próximo CLI elegível não-tentado, esgotamento → `PipelineStatus.Failed` + `FailureReason` | `AwaitingRetry` ficava preso para sempre (só retry manual); estado persistido sobrevive restart do processo | Retry no mesmo tick; rotação imediata sem janela |
| 2026-09-23 | Realtime Cockpit via grupo SignalR compartilhado `runs` + evento `run_status` em toda transição; UI atualiza linha/detalhe com refresh debounced (~300-400ms) | Lista `/cockpit` exigia F5 — engine só emitia `status` em pause/resume | Polling na UI; re-fetch por evento sem debounce |
| 2026-09-23 | Eventos duráveis: `IAgentExecutionEventSink` persiste lifecycle/verification/error/approval/steer/metric (espelhados `run:`→`issue:`); `GET /runs/{id}/events` retorna envelope `CockpitEventsPage` (durável + buffer live-only) | Replay sobrevive restart; antes só `output` voltava do buffer volátil | Só aumentar o buffer em memória |
| 2026-09-23 | PTY do run via `TerminalHub.OpenForRun(runId)` — cwd = worktree persistido, path resolvido server-side e confinado ao worktree root (`RunShell.razor` reusa plumbing xterm.js de `terminal.js`) | Nunca confiar em path vindo do client para abrir shell | Aceitar `repositoryPath`/workdir arbitrário do client |
| 2026-09-23 | Aprovação com resumo: `ApprovalRequestDto.Summary` (repo + run curto + stage + handoff) → toast + `Notification` do browser (`taskboardNotify`) + modal; deny → `Failed` terminal | Operador precisava abrir a página do run para perceber o gate | Só modal in-page |

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
| Workspace root | `~/repos` pode ser ele mesmo um clone → qualquer fallback para o root cria worktree do repo errado silenciosamente | Nunca tratar workspace root como repo; provisioning valida `git rev-parse` + `origin` antes de reusar dir existente |
| Integration tests | Provisioner real faria `git clone` via rede nos testes de endpoint | `FakeRepositoryProvisioningService` na factory (`RemoveAll<IRepositoryProvisioningService>`), marker `.foreign-repo` simula conflito |
| DomainException → HTTP | Novo error code sem mapping cai no `default` (400) em vez do status correto | Registrar no switch do `GlobalExceptionHandler` (`RepositoryProvisioningFailed` → 422) |
| Endpoint de eventos | Mudar retorno de `List<>` para envelope quebra client+teste que desserializam array | Atualizar `TaskboardClient` e testes no mesmo commit; eventos emitidos só ao buffer volátil (ex: `steer`) precisam ir ao sink durável para aparecer no replay |

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
