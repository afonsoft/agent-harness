# SPEC-20260928-taskboard-env-fallback-removal

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `taskboard-env-fallback-removal` |
| Type | `Backend / Ops` |
| Stack | `.NET 10` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260928-taskboard-env-fallback-removal` |
| Ticket | [#359](https://github.com/afonsoft/agent-harness/issues/359) (Epic [#356](https://github.com/afonsoft/agent-harness/issues/356)), PR [#364](https://github.com/afonsoft/agent-harness/pull/364) |
| Status | `Done` |
| Related | `SPEC-20260922-harness-home-rename` (origem do fallback; §48/§67 deferem a remoção "num ciclo futuro, com spec própria") |

## 1. User Story

**As a** mantenedor do Harness
**I want** remover os fallbacks legados `TASKBOARD_*` / `~/.taskboard` / `taskboard.sqlite` / `.taskboard-skills.json`
**So that** um ciclo após o rename (2026-09-22 → hoje), só exista a superfície canônica `HARNESS_*`/`~/.agent-harness`, eliminando a ambiguidade de resolução e a dívida de compatibilidade.

**Problem context:**

`SPEC-20260922-harness-home-rename` introduziu fallback com warning para todas as variáveis `TASKBOARD_*` e adiou a remoção ("fica para ciclo futuro"). Um ciclo já passou e nenhum spec/issue trackea a remoção — `grep -rln "TASKBOARD_" src/` ainda lista `HarnessEnv.cs`, `RuntimeConfigurationService.cs`, `TaskboardEnvironment.cs`, `TaskboardApiClient.cs`, `ApiKeyAuthenticationHandler.cs`, `Program.cs`, `WithoutHarnessEnv.cs`. O host local já está migrado (`~/.agent-harness`, `data/harness.sqlite`, `harness-server.service`).

**Breaking change:** hosts/scripts ainda usando `TASKBOARD_*` pararão de funcionar — o spec deve obrigar bump/minor nota de migração e checklist de validação do host.

## 2. Scope

**In scope:**

- Remover fallback de leitura `TASKBOARD_*` em `HarnessEnv.cs` (`LegacyPrefix`, resolução com warning) e todos os call sites que consultam nomes legados.
- Remover fallback de diretório `~/.taskboard`/`TASKBOARD_HOME`/`TASKBOARD_DIR`/`TASKBOARD_DATA_DIR` e de `taskboard.sqlite`.
- Remover leitura-fallback de `.taskboard-skills.json`.
- `WithoutHarnessEnv` continua removendo ambos os prefixos de processos de agente? **Decisão:** manter a remoção de `TASKBOARD_*` também — envs legados não devem vazar para agentes mesmo que o host ainda os tenha.
- Atualizar docs (README, installation, api) removendo menções a fallback; nota de migração no CHANGELOG.
- Atualizar testes que cobrem o comportamento de fallback.

**Out of scope:**

- Renomear namespaces `Taskboard.*`, projetos, `taskctl` ou `Taskboard:*` config section — permanecem por decisão do SPEC original.
- Remover arquivos físicos do host (`~/.agent-harness` cruft) — housekeeping separado.
- Mudar `install.sh --migrate` além de remover o que fica sem efeito.

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Domain.Shared/Configuration/HarnessEnv.cs` — `LegacyPrefix`, resolução dual.
- `src/Taskboard.Application.Contracts/Configuration/TaskboardEnvironment.cs`, `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` — aliases de env.
- `src/Taskboard.Server/Program.cs`, `src/Taskboard.Server/Auth/ApiKeyAuthenticationHandler.cs` — leitura de env no boot/auth.
- `src/Taskboard.Mcp/Services/TaskboardApiClient.cs` — client MCP.
- `src/Taskboard.Integrations/Execution/WithoutHarnessEnv.cs` — scrub de env de agentes.
- `tests/**` — fixtures que setam `TASKBOARD_*` (factory, `RuntimeConfigurationServiceTests`, `McpEndpointsTests`, testes de fallback/migração adicionados no SPEC-20260922).

**Files to read before implementing:**

- `SPEC-20260922-harness-home-rename.md` (§48-57, RF-003/RF-007, AC7/AC8 — o inverso desta remoção)
- Todos os arquivos listados acima.

## 4. Requirements

### RF-001: Resolução única `HARNESS_*`

- **Description:** `HarnessEnv` resolve apenas `HARNESS_*`; a presença de `TASKBOARD_*` é ignorada (ou gera warning único de depuração, se decidido — decisão do implementador, documentada).

### RF-002: Home/data/skills manifest canônicos

- **Description:** Apenas `~/.agent-harness`, `harness.sqlite`, `.harness-skills.json` são lidos; ausência do novo nome não consulta o legado.

### RF-003: Scrub de agentes mantém ambos os prefixos

- **Description:** `WithoutHarnessEnv` segue removendo `HARNESS_*` **e** `TASKBOARD_*` do ambiente de processos de agente — mesmo sem fallback de leitura, vazamento de env legado ao agente continua proibido.

### RF-004: Docs + migração

- **Description:** README/installation sem menção a fallback ativo; CHANGELOG nota breaking change + instrução (definir `HARNESS_*` equivalentes antes do upgrade).

## 5. API Contract

- Breaking: variáveis `TASKBOARD_*` deixam de ser lidas.
- Sem mudança de endpoints HTTP/DTOs.

## 6. Acceptance Criteria

- [x] **Given** `grep -rn "TASKBOARD_" src/ install.sh` **when** executado **then** só restam ocorrências de scrub (`WithoutHarnessEnv`) e comentários de compatibilidade removidos.
- [x] **Given** host com apenas `TASKBOARD_*` setado e app novo **when** inicia **then** usa defaults canônicos (não lê legado) — comportamento documentado.
- [x] **Given** `dotnet build` + `dotnet test` **when** executados **then** verde; testes de fallback removidos/substituídos por testes de "não-fallback".
- [x] **Given** o host local **when** o deploy ocorre **then** `harness-server` segue healthy (env já em `HARNESS_*`).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Host legado não migrado | só `TASKBOARD_TOKEN` | App falha/usa default conforme doc de migração — não silencia |
| Agente com env legado | process env `TASKBOARD_*` | `WithoutHarnessEnv` remove (RF-003) |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** inventariar todos os read sites de `TASKBOARD_*`/`~/.taskboard`/`taskboard.sqlite`/`.taskboard-skills.json`.
- [x] **T2 — Implementation:** remover fallbacks; manter scrub dual em `WithoutHarnessEnv`.
- [x] **T3 — Tests:** remover testes de fallback; adicionar cobertura de "não-fallback" + scrub dual.
- [x] **T4 — Docs:** README/installation/CHANGELOG (breaking + migração).
- [x] **T5 — Done + PR:** `Status = Done` e PR na branch do spec.

## 8. Organization Guardrails

- Não renomear namespaces/projetos/`taskctl`/`Taskboard:*` config.
- `.github/workflows/**` intocado.
- Validar `install.sh --migrate` continua funcional para hosts ainda em `~/.taskboard` (o migrate faz a transição; após migrado, runtime não lê legado).

## 9. Definition of Done

- [ ] Fallback removido, scrub dual mantido, docs/CHANGELOG atualizados.
- [ ] Build/test verde.
- [ ] `Status = Done` no mesmo PR.
