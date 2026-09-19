# Follow-ups — pendências que não bloqueiam mas não podem cair no esquecimento

## Migração CLI (Spectre.Console.Cli) — restos da sessão 2026-08-31

### Bloqueantes (fazer antes de declarar a migração concluída)

- [x] **Remover duplicata de `context:current`** em `src/Taskboard.Cli/Program.cs`. — removida antes do gap-analysis-20260919; `ConfigureCommands` registra cada comando uma única vez.
- [x] **Smoke tests:** `--help`, `context:current`, `cloud:status` — `tests/Taskboard.Tests.Unit/Cli/CliSmokeTests.cs` (SPEC-20260919-cli-test-coverage). `project:list`/`issue:get` não existem mais (superados pelo board GitHub `ghissue:*`).
- [ ] **Commit + push** (ver mensagem sugerida em `cli-migration.md`).

### Não bloqueantes

- [x] **Testes de CLI com `CommandAppTester`** — `tests/Taskboard.Tests.Unit/Cli/CliSmokeTests.cs` cobre `--help` raiz e de cada comando (SPEC-20260919-cli-test-coverage).
- [x] **Check para `[CommandArgument]` sem `<>`/`[]`** — implementado como teste de guarda por reflection (`Dado_CommandArgument_Quando_Registrado_Entao_PlaceholderUsaColchetes`) em vez de CI, pois `.github/workflows/**` é protegido por hard rule.
- [x] **Comentário inline** em `Program.cs` acima do primeiro `CommandArgument` (`CloudLoginSettings`, linhas ~152-153).
- [ ] **Decisão/ADR** registrando *por que* Spectre foi escolhido em vez de `System.CommandLine`.
- [ ] **Revisar UX diff** com a CLI original — confirmar que nenhum comando foi perdido e que mudanças posicional → `--flag` foram intencionais.

## Convenção para este arquivo

Cada item deve ter: `- [ ]` checkbox, contexto mínimo de *onde* e *por que*, e dono se aplicável. Itens concluídos viram `- [x]` e são limpos na próxima sessão.