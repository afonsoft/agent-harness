---
name: manage-taskboard
description: Gerencie o Dashi Taskboard via CLI taskctl .NET e via servidor MCP para agentes de IA.
tools:
  - Bash
  - Read
  - Edit
  - Write
---

## Contexto

O `taskboard-ai` é um clone local-first do Dashi Taskboard em .NET 10. Esta skill permite que um agente inspecione e altere projetos, issues, comentários e anexos através do CLI `taskctl` ou do servidor `Taskboard.Mcp`.

- URL padrão da API REST: `http://127.0.0.1:47823`
- CLI: `src/Taskboard.Cli`
- Servidor MCP: `src/Taskboard.Mcp`

## Instalação

Compile o CLI e o servidor MCP a partir da raiz do repositório:

```bash
dotnet build src/Taskboard.Cli/Taskboard.Cli.csproj
dotnet build src/Taskboard.Mcp/Taskboard.Mcp.csproj
```

### Criar link simbólico para o `taskctl`

```bash
ln -s $(pwd)/src/Taskboard.Cli/bin/Debug/net10.0/taskctl ~/.local/bin/taskctl
```

No macOS ou quando o caminho contiver espaços, coloque o binário entre aspas.

## Variáveis de ambiente

| Variável | Propósito | Padrão |
|---|---|---|
| `TASKBOARD_URL` | URL base da API REST do Taskboard | `http://127.0.0.1:47823` |
| `TASKBOARD_THREAD_ID` | Vínculo de thread para contexto do agente | - |

Nunca exponha tokens ou chaves de API na saída ou logs da skill.

## Usando `taskctl`

Prefira `--json` ao processar a saída programaticamente.

```bash
# Listar projetos
taskctl project list --json

# Criar uma issue
taskctl issue create --project local --title "Corrigir bug" --status todo --priority high --json

# Mover uma issue
taskctl issue move --identifier TASK-local-1 --status in_progress --json

# Adicionar comentário
taskctl comment create --identifier TASK-local-1 --body "Investigando." --json

# Listar anexos de uma issue
taskctl attachment list --identifier TASK-local-1 --json
```

### Board GitHub: histórico e comentários

Issues do board GitHub (cards do Kanban) têm uma superfície própria, identificada pelo `issueId` numérico do GitHub e por `owner/repo` + número da issue:

```bash
# Timeline unificada da issue (movimentações de coluna + execuções de agente)
taskctl ghissue:history <issueId> [--take 50] --json

# Listar comentários da issue no GitHub (contexto deixado por humanos/agentes)
taskctl ghissue:comments owner/name <issueNumber> [--take 50] --json

# Publicar comentário na issue no GitHub (handoff para o próximo agente/etapa)
taskctl ghissue:comment owner/name <issueNumber> "<body>" --json
```

**Convenção de handoff**: comentários na issue do GitHub são o canal de passagem de contexto entre agentes e etapas. Ao **assumir** uma issue, leia o histórico (`ghissue:history`) e os comentários (`ghissue:comments`) — o prompt gerado pela UI já injeta os comentários automaticamente na seção `Comments:`. Ao **concluir uma etapa**, publique um comentário (`ghissue:comment`) resumindo o que foi feito, decisões tomadas e o estado atual, para que o próximo agente ou etapa continue sem perda de contexto.

### Resolução de identificadores

Use o identificador escopo-de-projeto `TASK-<projeto>-<número>` quando disponível. O `taskctl` resolve para o GUID interno automaticamente.

## Usando o servidor MCP

### Executar o servidor

```bash
cd src/Taskboard.Mcp
TASKBOARD_URL=http://127.0.0.1:47823 dotnet run
```

O servidor usa transporte STDIO e expõe 16 tools.

### Registrar no Claude Desktop

Adicione em `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "taskboard": {
      "command": "dotnet",
      "args": ["run", "--project", "/caminho/completo/para/src/Taskboard.Mcp"],
      "env": {
        "TASKBOARD_URL": "http://127.0.0.1:47823"
      }
    }
  }
}
```

### Registrar no OpenCode / Cursor / Gemini / Devin

Use o mesmo par command/args. Configure `TASKBOARD_URL` no ambiente do IDE/agente.

## Fluxo de trabalho principal

1. **Descobrir**: execute `taskctl project list --json` e `taskctl issue list --project <projeto> --json` antes de agir.
2. **Ler primeiro**: para uma issue existente, execute `taskctl issue get --identifier <id> --json` e `taskctl comment list --identifier <id> --json` antes de decidir alterações.
3. **Pegar apenas `todo`**: mova `todo` -> `in_progress` apenas após confirmar que a issue pode ser assumida e que há autorização. Passe o `version` atual para evitar conflitos `409`.
4. **Respeitar vínculo de thread**: se `TASKBOARD_THREAD_ID` estiver definido, vincule comentários e contexto a essa thread.
5. **Executar no workspace correto**: use `taskctl context current --json` para verificar o workspace/branch vinculado. Execute comandos lá.
6. **Reportar**: após alterações, comente o que foi feito e o resultado. Mova para `in_review` com o `version` atual.
7. **Concluir apenas com aprovação**: mova para `done` apenas após o usuário aceitar explicitamente o resultado. Use `blocked` ou `canceled` caso contrário.

## Tratamento de conflitos

Se ocorrer `VERSION_CONFLICT` (`409`), releia a issue com `taskctl issue get` e tente novamente uma vez. Se ainda conflitar, pare e pergunte ao usuário.

## Terminologia

- `companion`: o dispositivo/serviço companion na nuvem (não traduza como "companion").
- `local companion`: o companion loopback/nuvem rodando localmente.
- `backlog`: ainda não aprovado; não assuma.
- `todo`: pronto para trabalho.
- `in_progress`, `in_review`, `done`, `blocked`, `canceled`: estados padrão do fluxo.

## Referências

- `references/cli.md` — referência completa dos comandos `taskctl`
- `.specs/SPEC-003-cli.md` — especificação do CLI
- `.specs/SPEC-004-mcp.md` — especificação do servidor MCP
- `.specs/CAPABILITY-MAP.md` — ordem dos módulos e gates de aceitação
