# SPEC-20261008-remove-empty-aichat-project — Remove dead `Taskboard.AiChat` project

| Campo | Valor |
| --- | --- |
| Status | **Draft** |
| Owner | @afonsoft |
| Ticket | GAP-implementation-empty-aichat-project |
| Área | Taskboard.sln, src/Taskboard.AiChat/ |

## Contexto

`src/Taskboard.AiChat/Taskboard.AiChat.csproj` existe na solution (`Taskboard.sln:8`) mas contém **zero** arquivos `.cs` fora de `obj/bin` — apenas um `ProjectReference` para `Application.Contracts`. O código de chat vive em `Taskboard.Application{,.Contracts}/AiChat` e `Taskboard.Blazor`; o projeto é um artefato abandonado de uma camada planejada que nunca recebeu conteúdo.

## Evidência (gap-analysis 2026-10-08)

- AS-IS: `find src/Taskboard.AiChat -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" | wc -l` → `0`; csproj contém só `<IsPackable>false</IsPackable>` + ProjectReference para Contracts; presente em `Taskboard.sln` linha 8.
- TO-BE: higiene da solution — todo projeto referenciado no `.sln` deve conter código ou ter razão documentada (regra implícita do repo: `.specs/followups.md` já trata restos de refactors como pendência).
- Diferença: projeto morto compila um assembly vazio e polui a solution/publish graph.

## Requisito

Remover `src/Taskboard.AiChat/` do `Taskboard.sln` e deletar o diretório. Confirmar que nenhum `.csproj`/`cs` referencia `Taskboard.AiChat` como projeto (o *namespace* `Taskboard.AiChat` usado em `Application.Contracts/AiChat/` permanece — é namespace, não dependência de assembly). Build + suite completa devem passar sem regressão.

## Critérios de Aceite

- [ ] `grep -c "Taskboard.AiChat" Taskboard.sln` → `0` (apenas a entrada do projeto removida).
- [ ] Diretório `src/Taskboard.AiChat/` ausente.
- [ ] `dotnet build` + `dotnet test` verdes (2540 testes baseline).
- [ ] `dotnet publish` do Server/Mcp inalterado.

## Fora de escopo

Renomear o namespace `Taskboard.AiChat` em Contracts (mudança de namespace tocando dezenas de arquivos — não compensa o risco por higiene).
