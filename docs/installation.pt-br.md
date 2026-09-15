# Instalação

Este guia cobre como instalar, compilar, configurar e executar o `taskboard-ai` localmente.

> **Idioma padrão:** Inglês (en-us). Veja a [installation.md](installation.md) para a versão em inglês.

## Visão Geral

O `taskboard-ai` é um quadro de tarefas local-first e AI-native escrito em **C# 14 / .NET 10**. Ele oferece um sistema de tarefas com SQLite, API REST, eventos Server-Sent Events (SSE), a CLI `taskctl`, um servidor MCP e uma interface web Blazor WebAssembly.

## Pré-requisitos

| Ferramenta | Versão mínima | Observações |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 | Necessário para build, testes e execução |
| [Git](https://git-scm.com/) | 2.30 | Para clonar o repositório |
| `dotnet-ef` | 10.0 | Opcional, para migrations |
| `jq` | 1.6 | Opcional, para parsear exemplos de saída JSON da CLI |

Verifique a versão do .NET SDK:

```bash
dotnet --version
```

A saída deve começar com `10.`.

## Clone

```bash
git clone https://github.com/afonsoft/taskboard-ai.git
cd taskboard-ai
```

## Build

Restaure e compile a solução:

```bash
dotnet restore Taskboard.sln
dotnet build Taskboard.sln
```

Para build em Release:

```bash
dotnet build Taskboard.sln --configuration Release
```

O repositório usa `TreatWarningsAsErrors`; o build deve terminar sem warnings.

## Testes

Execute a suíte completa de testes:

```bash
dotnet test Taskboard.sln
```

## Variáveis de ambiente

| Variável | Padrão | Descrição |
|---|---|---|
| `TASKBOARD_PORT` | `47823` | Porta HTTP usada pelo servidor |
| `TASKBOARD_DATA_DIR` | `.data` sob o content root do servidor | Diretório onde `taskboard.sqlite` e `.admin-password` são armazenados |
| `GITHUB_TOKEN` | *(nenhum)* | Token de acesso pessoal do GitHub para o board Kanban (`/github-board`) |
| `TASKBOARD_URL` | `http://127.0.0.1:47823` | URL base usada pela CLI `taskctl` e pelo servidor MCP |
| `TASKBOARD_API_KEY` | *(nenhuma)* | Chave de API enviada como `X-Api-Key` pela `taskctl`/MCP para autenticar na `/api` do servidor (todos os endpoints, exceto `login`, `auth/me`, `meta` e health, exigem cookie ou API key) |
| `ASPNETCORE_URLS` | *(nenhum)* | Sobrescreve `TASKBOARD_PORT` com uma URL completa, como `http://0.0.0.0:47823` |

Defina as variáveis para a sessão atual do shell:

```bash
export TASKBOARD_PORT=47823
export TASKBOARD_DATA_DIR="$PWD/.data"
export GITHUB_TOKEN="ghp_your_token"
```

> **Breaking change:** As variáveis legadas `CODEX_TASKBOARD_PORT` e `CODEX_TASKBOARD_DATA_DIR` não são mais lidas. Use `TASKBOARD_PORT` e `TASKBOARD_DATA_DIR`.

## Executar o servidor

```bash
dotnet run --project src/Taskboard.Server
```

O servidor irá:
- Aplicar as migrations do EF Core automaticamente.
- Criar o projeto padrão `local` se ele não existir.
- Escutar em `http://127.0.0.1:47823` por padrão.

Verifique se o servidor está executando:

```bash
curl http://127.0.0.1:47823/health
```

Resposta esperada:

```json
{ "status": "ok", "timestamp": "..." }
```

Para mudar a porta:

```bash
TASKBOARD_PORT=8080 dotnet run --project src/Taskboard.Server
```

## Executar a CLI

A CLI é invocada através do projeto `taskctl`:

```bash
dotnet run --project src/Taskboard.Cli -- --help
```

Listar projetos:

```bash
dotnet run --project src/Taskboard.Cli -- project list
```

Criar um projeto:

```bash
dotnet run --project src/Taskboard.Cli -- project create --id my-project --name "My Project" --workspace-path /abs/path/to/project
```

A CLI usa `TASKBOARD_URL` para encontrar o servidor. Para apontar para outra URL:

```bash
TASKBOARD_URL=http://127.0.0.1:8080 dotnet run --project src/Taskboard.Cli -- project list
```

## Executar o servidor MCP

O servidor MCP se comunica via STDIO:

```bash
dotnet run --project src/Taskboard.Mcp
```

Para configuração do cliente, veja [plugins.md](./plugins.md) e [SPEC-004](../.specs/SPEC-004-mcp.md).

## Usar a interface web

Abra `http://127.0.0.1:47823/github-board` em um navegador. As credenciais de admin padrão são configuradas a partir de `appsettings.json` ou variáveis de ambiente (`TASKBOARD_ADMIN_USERNAME`, `TASKBOARD_ADMIN_PASSWORD`).

## Board Kanban do GitHub

Para habilitar o board Kanban do GitHub, defina `GITHUB_TOKEN` antes de iniciar o servidor:

```bash
export GITHUB_TOKEN="ghp_your_token"
dotnet run --project src/Taskboard.Server
```

O board está disponível em `/github-board`. Sem um token, o board exibirá erros ou ficará vazio.

## Checklist de verificação

Após a instalação:

1. `dotnet build Taskboard.sln` termina sem warnings.
2. `dotnet test Taskboard.sln` passa.
3. `curl http://127.0.0.1:47823/health` retorna `{ "status": "ok" }`.
4. A UI web abre em `/github-board`.
5. `dotnet run --project src/Taskboard.Cli -- --help` imprime a lista de comandos.

## Resolução de problemas

### Porta já em uso

```bash
TASKBOARD_PORT=8080 dotnet run --project src/Taskboard.Server
```

### .NET 10 SDK ausente

Instale o [.NET 10 SDK](https://dotnet.microsoft.com/download) e verifique com `dotnet --version`.

### Banco SQLite bloqueado

Pare qualquer instância do servidor em execução. Apenas um processo do servidor pode usar o mesmo `TASKBOARD_DATA_DIR` por vez.

### `GITHUB_TOKEN` ausente

O board Kanban requer `GITHUB_TOKEN`. Gere um token em [GitHub Settings > Developer settings > Personal access tokens](https://github.com/settings/tokens) com os escopos `repo` e `read:org`.

### CLI não consegue conectar

Verifique se o servidor está executando e se `TASKBOARD_URL` corresponde à URL do servidor:

```bash
TASKBOARD_URL=http://127.0.0.1:47823 dotnet run --project src/Taskboard.Cli -- project list
```

### Permissões no Linux / macOS

Se usar o `install.sh`, garanta que ele seja executável:

```bash
chmod +x install.sh
```

## Próximos passos

- Leia a [documentação do sistema](./README.md).
- Explore a [documentação da API](./api.md).
- Confira os [diagramas de arquitetura](./architecture/architecture.html).
- Revise o [índice de SPECs](../.specs/README.md) para detalhes de implementação.
