# Migração da CLI: System.CommandLine → Spectre.Console.Cli

**Status:** concluído — migração Spectre mergeada; duplicata context:current removida; smoke tests em CliSmokeTests (SPEC-20260919-cli-test-coverage)
**Última atualização:** 2026-08-31
**Pendências bloqueantes:** ver seção "Pendências" — há duplicata de registro e commit/push não finalizado.

## Decisão de arquitetura

Substituir `System.CommandLine` por `Spectre.Console.Cli`. Motivação: melhor renderização de ajuda (rich rendering), integração nativa com o sistema de cores (`IAnsiConsole`) e tabelas amigáveis no suporte a `--help`.

## Versão pinada

`Directory.Packages.props` fixa `Spectre.Console` e `Spectre.Console.Cli` na versão **0.49.1**.
Assinatura de `ExecuteAsync` em uso: `public override Task<int> ExecuteAsync(CommandContext, TSettings)`.

## ⚠️ Conhecimento crítico: convenção de `CommandArgument`

O segundo parâmetro de `[CommandArgument]` é o **placeholder de exibição**.
- `"<project>"` → obrigatório, renderiza `<project>`
- `"[project]"` → opcional, renderiza `[project]`
- `"project"` (cru) → **crash**: Spectre renderiza `[project]` como markup e lança `Could not find color or style 'project'`.

> Aplicado: todos os `CommandArgument` em `src/Taskboard.Cli/Program.cs` foram envoltos em `<...>` (pendente commit).

## Pendências bloqueantes (fazer antes de fechar a tarefa)

1. **Remover duplicata de `context:current`** — existe nas linhas 17 e 43 de `src/Taskboard.Cli/Program.cs`. Deletar a linha 43.
2. **Commit + push** — mensagem sugerida:
`feat: migrate CLI to Spectre.Console.Cli`

## Inventário de comandos (auditoria)
Grupo | Comando | Argumentos posicionais
---|---|---
project | list, create, map | `<project>` (map)
issue | list, get, create, update, move, archive, restore, relation | `<identifier>` (+ relação: `<action>` `<type>` `<target>`)
comment | list, add, update, delete | `<identifier>`/`<commentId>` + `<body>`
attachment | upload, download | `<identifier>`/`<attachmentId>` + `<file>`/`<output>`
context | current | —
cloud | login, status, logout | `<url>` (login)

## Melhorias futuras
- Testes automatizados com `CommandAppTester`.
- Check de CI para validação de nomes de argumento.
