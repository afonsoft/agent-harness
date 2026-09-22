# SPEC-20260922-flaky-integration-tests

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `flaky-integration-tests` |
| Type | `Bugfix` (test determinism) |
| Stack | `xUnit / WebApplicationFactory / .NET 10` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `fix/20260922-flaky-integration-tests` |
| Ticket | [#307](https://github.com/afonsoft/agent-harness/issues/307) — GAP-tests-flaky-integration (epic #306) |
| Status | `Done — delivered in PR #311` |

---

## 1. User Story

**As a** mantenedor do repo
**I want** a suite de integração determinística (sem flakes)
**So that** um CI vermelho signifique regressão real, e não timing/race — o sinal de CI hoje é degradado por 2 testes flaky documentados.

### Problem Context

Dois testes de integração falham intermitentemente na suite completa e passam isolados:

1. **`McpEndpointsTests.Dado_Autenticado_Quando_PostMcpRemove_Entao_202ERemoveEntry`** — reproduzido em 2026-09-22 (`dotnet test` em `main` @`11404db`): `HasManagedEntry(claudeConfig)` retornou `True` após deadline de 10s. O endpoint `POST /api/mcp/remove` é fire-and-forget (`Program.cs:2262-2269` — `mcp.RequestRemoval()` retorna 202 imediato; a remoção real roda em background worker). O teste faz polling do arquivo `~/.claude.json` fake com deadline fixo — sob carga paralela da suite o worker não completa a tempo.
2. **`CliMetricsEndpointsTests.Dado_SyncManual_Quando_PostSync_Entao_RetornaResultado`** — race documentada com o startup sync (`orchestrator_stats.md`): `result.SourcesSynced` depende de o sync inicial do host ter terminado.

**Evidência:**

- AS-IS: `tests/Taskboard.Tests.Integration/McpEndpointsTests.cs:248-265` (polling `File` com deadline 10s); `tests/Taskboard.Tests.Integration/CliMetricsEndpointsTests.cs:24-33`.
- Run de 2026-09-22: `Failed: 1, Passed: 261` — falha = `PostMcpRemove`; ambos passam isolados (3/3).
- Registrado como flake conhecido em `.claude/memory/orchestrator_stats.md` há ≥2 sessões — flake recorrente sem issue/spec até agora.

---

## 2. Scope

### In scope

- Tornar `PostMcpRemove` determinístico: substituir polling de arquivo+deadline por sinal observável do próprio sistema — polling de `GET /api/mcp` (status) ou `GET /api/mcp/log` até a operação constar como concluída, mantendo o assert final sobre `.claude.json`.
- Tornar `SyncManual` determinístico: aguardar/sincronizar com o startup sync do host (ex.: endpoint de status, `TaskCompletionSource` injetável na factory, ou retry com condição — não `Task.Delay` fixo).
- Se o worker de provisioning não expõe conclusão observável, adicionar transição de status mínima (`Removing → Removed`) consultável via `GetStatus()` — já existe modelo de status em `mcp.GetStatus()`.

### Out of scope

- Mudar a semântica do endpoint `POST /api/mcp/remove` (mantém 202 + background).
- Reescrever o `McpProvisioningService` — apenas superfície de observabilidade se necessário.
- Outros testes flaky que não os 2 documentados.
- Infra de teste de UI/E2E (Playwright é "futuro" por SPEC-014).

---

## 3. Acceptance Criteria (BDD)

- **AC1:** `dotnet test tests/Taskboard.Tests.Integration -c Release` passa 262/262 em 3 execuções consecutivas na suite completa (não isolada).
- **AC2:** `PostMcpRemove` não usa `Task.Delay`/deadline-fixo como única barreira — sincroniza via status observável do provisioning.
- **AC3:** `SyncManual` não depende de timing do startup sync.
- **AC4:** Nenhum teste passa a escrever/depender do `~/.claude.json` real — continua isolado via `HomeDir` da factory.

---

## 4. Tasks

- [ ] **T1:** reproduzir ambos os flakes na suite; identificar o sinal de conclusão correto para cada um.
- [ ] **T2:** se necessário, expor conclusão de `RequestRemoval` via `GetStatus()`/`McpOperationLog` (mudança mínima).
- [ ] **T3:** reescrever os 2 testes para sincronizar via sinal; rodar suite 3× verde.

---

## 5. Verification

- 3× `dotnet test tests/Taskboard.Tests.Integration -c Release` → 262/262.
- `grep -n "Task.Delay\|deadline" McpEndpointsTests.cs CliMetricsEndpointsTests.cs` → sem polling cego novo.

---

## 6. Risks & Open Questions

1. Se o worker não tiver transição de estado observável, T2 adiciona uma — avaliar se `McpOperationLog` (já existe, `GET /api/mcp/log`) é suficiente antes de criar superfície nova.
2. `SyncManual`: confirmar se a race é "startup sync ainda rodando" ou "sources vazias" — o fix difere.
