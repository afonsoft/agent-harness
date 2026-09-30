# Gap Analysis — 2026-09-29

- Repository: `/home/ubuntu/repos/agent-harness` | Branch: `main` | Commit: `09eddba`
- Phase reached: `issues-created` (gate aprovado → specs Approved → Epic #395 + slices #396/#397)
- Mode: `full` (sweep pós-merge PRs #391/#393/#394)

---

## 1. Source Inventory

| Source | Status | Notes |
|---|---|---|
| `.specs/` | `present` | 134 SPECs; `check-spec-status.sh` → "OK: no spec status drift"; 0 non-terminal |
| `docs/` | `present` | features.md cobre chat/webcli; **gap**: installation.md env table + api.md endpoints desatualizados |
| `docs/architecture/` | `present` | |
| `.claude/memory/` | `present` | este é o 12º report; anterior 2026-09-28 (done) |
| `CLAUDE.md`/`AGENTS.md`/`README.md` | `present` | |
| Tests/CI | `present` | `CliMetricsSyncTests` 2/4 falhando na main (bug real, ver §3) |
| gh auth + remotes | `ok` | issues criadas: Epic #395, slices #396/#397; #390/#392 fechadas (housekeeping) |
| submodules | none | |

## 2. AS-IS × TO-BE highlights

| Topic | AS-IS | TO-BE | Sources |
|---|---|---|---|
| ManagedJobService loop | `WhenAny` trata delay cancelado como intervalo decorrido | shutdown não executa job | `ManagedJobService.cs` ~L45-60; testes §4 |
| Env vars reference | tabela sem `HARNESS_WEB_CLI_AGENT_ENABLED` + 4 `HARNESS_CHAT_*` | aliases documentados | `installation.md` L64-74 vs `RuntimeConfigurationService.cs` L79-105 |
| API reference | `/api/local/chat/*` ausente em api.md | família documentada | `api.md` vs `Program.cs` L1792-1960 |
| EF model | sem pending changes | — | `dotnet ef migrations has-pending-model-changes` |
| Chat tools security | gateway + jail + scrub + master switch | confinamento | `ShellExecTool.cs:41`, `FileSystemTools.cs`, `ChatService.cs:493` |
| Provider key masking | DTO `HasApiKey`/`KeyHint` | key nunca sai do server | `ChatDtos.cs:4`, `ChatService.cs:539` |

## 3. Candidates e Verdicts

| Key | Categoria | Veredito | Prioridade | Destino |
|---|---|---|---|---|
| `GAP-tests-cli-metrics-sync-flaky` | tests/bug | **CONFIRMADO** | impact med · effort S | SPEC-20260929-managed-job-shutdown-extra-run → Issue #396 |
| `GAP-documentation-chat-webcli-docs` | documentation | **CONFIRMADO** | low · effort S | SPEC-20260929-chat-webcli-docs → Issue #397 |
| `GAP-operation-done-issues-open` | operation | CONFIRMADO (housekeeping) | trivial | #390/#392 fechadas |
| Provider API key leak | security | REJEITADO | — | endpoints serializam `ChatProviderDto` mascarado |
| Chat tools confinement | security | REJEITADO | — | gateway/jail/scrub/switch wireados |
| Spec status drift | automation | REJEITADO | — | `check-spec-status.sh` OK |
| EF pending model changes | implementation | REJEITADO | — | nenhum |
| SonarCloud external check | operation | REJEITADO | — | passou no #394 (transitório) |
| `~/.agent-harness/publish.prev` | operation | REJEITADO | — | artefato de rotação do update, regenera por design |
| Stale remote-tracking refs | operation | REJEITADO | — | `git fetch --prune` executado |

## 4. Evidências (root cause)

`ManagedJobService.ExecuteAsync`:

```csharp
var delayTask = Task.Delay(wait, stoppingToken);
var signalTask = signals.ReadAsync(stoppingToken).AsTask();
var winner = await Task.WhenAny(delayTask, signalTask);
if (winner == delayTask) { await RunOnceSafeAsync(stoppingToken); continue; }
```

`Task.Delay` cancelado **completa** e vence o `WhenAny` → `RunOnceSafeAsync` roda no shutdown.
Saída real do teste na `main` (`09eddba`):

- `Dado_Enabled_..._SincronizaNoStartup`: `Calls` expected `1`, was `2` (startup + shutdown).
- `Dado_Disabled_..._NaoSincroniza`: `Calls` expected `0`, was `1` (só shutdown).

Regressão introduzida por SPEC-20260929-jobs-dashboard (migração para `ManagedJobService`).

## 5. SPECs gerados

- `.specs/SPEC-20260929-managed-job-shutdown-extra-run.md` — Approved → Issue #396 (slice+todo)
- `.specs/SPEC-20260929-chat-webcli-docs.md` — Approved → Issue #397 (slice+todo)
- Epic: #395 `gap-analysis-20260929` (epic+todo)

## 6. Pendências

- Orquestração/execução dos 2 SPECs (aguardando trigger do usuário — Phase 7 não autorizada no gate).
- Housekeeping residual: nenhum.
