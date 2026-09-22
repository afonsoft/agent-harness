---
name: manage-taskboard
description: Gerencie o Harness (agent-harness) via CLI taskctl .NET e via servidor MCP para agentes de IA.
tools:
  - Bash
  - Read
  - Edit
  - Write
---

## Contexto

O `agent-harness` (produto: **Harness**) é um taskboard local-first em .NET 10 cuja fonte de verdade é o **GitHub**: o board, as issues, os comentários e o histórico vêm de repositórios GitHub. Esta skill permite que um agente inspecione issues do board, leia o histórico unificado e publique comentários de handoff através do CLI `taskctl` ou do servidor `Taskboard.Mcp`.

- URL padrão da API REST: `http://127.0.0.1:47823`
- CLI: `src/Taskboard.Cli`
- Servidor MCP: `src/Taskboard.Mcp`

> Projetos e issues locais (`/api/projects`, `/api/tasks`, comentários e anexos locais) foram removidos — o GitHub é a única fonte de verdade do board.

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
| `HARNESS_URL` | URL base da API REST do Taskboard | `http://127.0.0.1:47823` |
| `HARNESS_THREAD_ID` | Vínculo de thread para contexto do agente | - |

Nunca exponha tokens ou chaves de API na saída ou logs da skill.

## Usando `taskctl`

Prefira `--json` ao processar a saída programaticamente.

```bash
# Workspace/contexto atual (repo, branch, caminho)
taskctl context:current --json

# Timeline unificada da issue (movimentações de coluna + execuções de agente)
taskctl ghissue:history <issueId> [--take 50] --json

# Listar comentários da issue no GitHub (contexto deixado por humanos/agentes)
taskctl ghissue:comments owner/name <issueNumber> [--take 50] --json

# Publicar comentário na issue no GitHub (handoff para o próximo agente/etapa)
taskctl ghissue:comment owner/name <issueNumber> "<body>" --json

# Sessão cloud
taskctl cloud:login --url HTTPS_ORIGIN --actor-name NAME
taskctl cloud:status --json
taskctl cloud:logout
```

**Convenção de handoff**: comentários na issue do GitHub são o canal de passagem de contexto entre agentes e etapas. Ao **assumir** uma issue, leia o histórico (`ghissue:history`) e os comentários (`ghissue:comments`) — o prompt gerado pela UI já injeta os comentários automaticamente na seção `Comments:`. Ao **concluir uma etapa**, publique um comentário (`ghissue:comment`) resumindo o que foi feito, decisões tomadas e o estado atual, para que o próximo agente ou etapa continue sem perda de contexto.

## Usando o servidor MCP

### Executar o servidor

```bash
cd src/Taskboard.Mcp
HARNESS_URL=http://127.0.0.1:47823 dotnet run
```

O servidor usa transporte STDIO e expõe 4 tools:

| Tool | Propósito |
|---|---|
| `get_issue_history` | Timeline unificada de uma issue do board (movimentações + runs de agente) |
| `list_github_issue_comments` | Comentários da issue no GitHub, em ordem cronológica |
| `add_github_issue_comment` | Publica comentário na issue do GitHub (handoff) |
| `cloud_status` | Status da conexão com a nuvem |

### Registrar no Claude Desktop

Adicione em `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "taskboard": {
      "command": "dotnet",
      "args": ["run", "--project", "/caminho/completo/para/src/Taskboard.Mcp"],
      "env": {
        "HARNESS_URL": "http://127.0.0.1:47823"
      }
    }
  }
}
```

### Registrar no OpenCode / Cursor / Gemini / Devin

Use o mesmo par command/args. Configure `HARNESS_URL` no ambiente do IDE/agente.

## Fluxo de trabalho principal

1. **Descobrir**: execute `taskctl context:current --json` para verificar o workspace/branch vinculado.
2. **Ler primeiro**: antes de assumir uma issue, execute `taskctl ghissue:history <issueId> --json` e `taskctl ghissue:comments owner/repo <n> --json`.
3. **Pegar apenas `todo`/`backlog`**: mova a coluna pelo board/UI (o GitHub é a fonte de verdade das colunas via labels).
4. **Respeitar vínculo de thread**: se `HARNESS_THREAD_ID` estiver definido, vincule comentários e contexto a essa thread.
5. **Reportar**: ao concluir uma etapa, publique um comentário (`ghissue:comment`) resumindo o que foi feito e o resultado.
6. **Concluir apenas com aprovação**: feche a issue/mova para `done` apenas após o usuário aceitar explicitamente o resultado.

## Terminologia

- `companion`: o dispositivo/serviço companion na nuvem (não traduza como "companion").
- `local companion`: o companion loopback/nuvem rodando localmente.
- `backlog`: ainda não aprovado; não assuma.
- `todo`: pronto para trabalho.
- `in_progress`, `in_review`, `done`, `blocked`, `canceled`: estados padrão do fluxo (labels do board).

## Referências

- `references/cli.md` — referência completa dos comandos `taskctl`
- `.specs/SPEC-003-cli.md` — especificação do CLI
- `.specs/SPEC-004-mcp.md` — especificação do servidor MCP
- `.specs/SPEC-20260918-projects-removal.md` — remoção dos projetos/issues locais
