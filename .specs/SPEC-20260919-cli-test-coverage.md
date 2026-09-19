# SPEC-20260919 — CLI Test Coverage: taskctl sem nenhum teste

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `cli-test-coverage` |
| Type | `Tests` |
| Stack | `.NET 10 / Spectre.Console.Cli 0.49.1 / xUnit` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260919-cli-test-coverage` |
| Ticket | `GAP-tests-cli-coverage` (gap-analysis-20260919) — Issue #144, Epic #140 |
| Status | `Done` — entregue via PR #148 (merged) |

Origin: gap-analysis-20260919 — `src/Taskboard.Cli` (taskctl) tem **zero**
cobertura: nenhum `.cs` de teste referencia `Taskboard.Cli`, `taskctl` ou
`CommandAppTester`. TO-BEs violados: `SPEC-003-cli` §18 ("Expected Tests" —
item 6 "testes de integração CLI", AC "Tests passam"), `followups.md`
("Testes de CLI com `CommandAppTester`", "Check de CI para
`[CommandArgument]` sem `<>`/`[]`", "Smoke tests: `--help`, `context:current`…")
e a hard rule de testes do `global-rules.md`.

## 1. User Story

**As a** maintainer do Harness,
**I want** smoke tests da CLI taskctl (help, comandos, contrato JSON) e o
guard de `CommandArgument`,
**so that** regressões como o crash de `--help` por placeholder cru
(gotchas.md) nunca voltem silenciosamente.

## 2. Scope

### In scope

- Projeto de teste para a CLI — preferência: `tests/Taskboard.Tests.Unit/Cli/`
  referenciando `Taskboard.Cli` (avaliar se `Program`/commands são visíveis;
  caso contrário `InternalsVisibleTo` ou projeto `Taskboard.Cli.Tests` novo —
  decidir na implementação pelo caminho mais simples).
- Pacote `Spectre.Console.Cli.Testing` (`CommandAppTester`) — **nova dependência
  NuGet, justificada**: é o harness oficial de testes do Spectre.Console.Cli.
  (Nota de implementação: `CommandAppTester` vive no pacote
  `Spectre.Console.Cli.Testing`, não em `Spectre.Console.Testing` — este último
  só expõe `TestConsole`.)
- Smoke tests: `--help` no root e em cada command group registrado em
  `Program.cs` (garante que nenhum `CommandArgument` cru quebra o parser —
  regressão direta do bug documentado em `gotchas.md`); `context:current`
  retorna exit 0 e JSON parseável.
- Guard do `gotchas.md` "Prevenção": teste que varre os comandos registrados
  e falha se algum `[CommandArgument(n, "...")]` não iniciar com `<` ou `[`
  (via reflection nos attributes) — **não** step de CI (workflows são
  protegidos por hard rule).
- Comentário inline de uma linha em `Program.cs` acima do primeiro
  `CommandArgument` registrando a convenção (item pendente do followups.md).

### Out of scope

- Testes de integração que chamam a API real (CLI → HTTP fica para quando
  houver `TestServer` reutilizável).
- Mudança de comportamento de qualquer comando.
- Cobertura exaustiva de todos os subcomandos — foco em smoke + o guard.

## 3. Technical Context

- `src/Taskboard.Cli/Program.cs` — registra commands via
  `config.AddCommand<T>("name")` (`context:current` linha 16, etc.).
- Convenção `CommandArgument`: `<nome>` obrigatório / `[nome]` opcional —
  cru = crash do `StyleParser` (`gotchas.md`).
- `CommandAppTester` (Spectre.Console.Cli.Testing) instancia o app em memória e
  captura output/exit code — padrão oficial.
- Hard rule: workflows `.github/**` intocados — o guard vive como teste, não CI.

## 4. Functional Requirements

| ID | Requirement |
|---|---|
| RF-001 | `taskctl --help` e o `--help` de cada command group executam com exit 0 e output não-vazio no `CommandAppTester`. |
| RF-002 | `context:current` (e demais comandos que não exigem rede, se houver) rodam no tester com exit esperado. |
| RF-003 | Teste de convenção: nenhum `CommandArgument` registrado usa placeholder cru (reflection sobre os `CommandSettings`/`[CommandArgument]`). |
| RF-004 | Comentário inline da convenção adicionado em `Program.cs`. |
| RF-005 | `followups.md` atualizado (itens de teste CLI → `[x]` ou cross-ref deste SPEC). |

## 5. Acceptance Criteria

- **AC-1** `dotnet test` inclui os novos testes de CLI, todos verdes.
- **AC-2** Reintroduzir um `CommandArgument` cru derruba o teste do guard (RED demonstrável).
- **AC-3** Nenhum arquivo em `.github/workflows/` modificado.
- **AC-4** Nova dependência `Spectre.Console.Cli.Testing` justificada no PR (soft rule).

## 6. DoD

- [x] RF-001..005 implementados.
- [x] `dotnet build` clean; `dotnet test` verde (460 unit + novos CLI).
- [ ] SPEC → `Status: Done`; PR merged.
