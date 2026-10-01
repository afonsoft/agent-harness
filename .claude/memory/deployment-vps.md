# Deployment — VPS (host mode)

> Estado confirmado em 2026-10-01. O deploy de produção desta VPS é **no host**
> via systemd user service. Docker é o cenário alternativo (imagem definida no
> `Dockerfile` raiz). Nomes migrados para `harness` (SPEC-20260922-harness-home-rename).

## Topologia

| Item | Valor |
|---|---|
| Serviço | `harness-server.service` (systemd **user**, `enabled`, `Restart=on-failure`, drop-in `MemoryMax=4G`/`MemoryHigh=3G`) |
| Unit | `~/.config/systemd/user/harness-server.service` (+ `harness-server.service.d/override.conf`) |
| Launcher | `~/.agent-harness/bin/harness-server` (bash, sourceia `~/.agent-harness/env`) |
| MCP launcher | `~/.agent-harness/bin/harness-mcp` → `~/.agent-harness/publish-mcp/Taskboard.Mcp.dll` |
| Env file | `~/.agent-harness/env` (gerado pelo `install.sh`; editado manualmente — ver abaixo) |
| Publish | `~/.agent-harness/publish` (`dotnet publish -c Release`); rotação para `publish.prev` em cada deploy (rollback) |
| Publish MCP | `~/.agent-harness/publish-mcp` (rotação para `publish-mcp.prev`) |
| DataDir | `~/.agent-harness/data` (`harness.sqlite`, `admin.json`, `skills-cache`, `dataprotection`) |
| Porta | `127.0.0.1:47823` (`ASPNETCORE_URLS` default no launcher) |

## Chaves em `~/.agent-harness/env`

`PATH`, `HARNESS_DATA_DIR`, `Taskboard__DataDir`, `HARNESS_ADMIN_USERNAME`,
`HARNESS_ADMIN_PASSWORD`, `HARNESS_URL`, `GITHUB_TOKEN`,
`Taskboard__ApiKey`, `HARNESS_API_KEY` — **nunca commitar nem imprimir valores**.

### PATH do serviço (armadilha)

O PATH default do systemd (`/usr/local/bin:/usr/bin:...`) **não alcança** os
agent CLIs — no host eles vivem em:

- `~/.nvm/versions/node/v24.16.0/bin` → `node`, `npx`, `claude`, `codex`, `opencode`
- `~/.local/bin` → `devin`, `agy`

A linha PATH do env file resolve o nvm dinamicamente (pega a versão mais nova):

```bash
export PATH="$HOME/.agent-harness/bin:$HOME/.local/bin:$(ls -d $HOME/.nvm/versions/node/*/bin 2>/dev/null | sort -V | tail -1):$PATH"
```

Sem isso, `/api/agent-clis` reporta tudo `installed=false` e o skills-install
falha por falta de `npx`. Backup do env: `~/.agent-harness/env.bak-*`.

## Deploy flow

```bash
cd ~/repos/agent-harness
git checkout main && git pull --ff-only
systemctl --user stop harness-server
# Rotacionar publish (rollback grátis + evita bundles fingerprinted stale —
# `dotnet publish -o` NÃO limpa o output dir e _framework/*.hash.wasm acumula
# entre deploys; sintoma 2026-09-19: Board → "Sorry, there's nothing at this
# address." por manifest antigo):
rm -rf ~/.agent-harness/publish.prev && mv ~/.agent-harness/publish ~/.agent-harness/publish.prev
rm -rf ~/.agent-harness/publish-mcp.prev && mv ~/.agent-harness/publish-mcp ~/.agent-harness/publish-mcp.prev
dotnet publish src/Taskboard.Server/Taskboard.Server.csproj -c Release -o ~/.agent-harness/publish
dotnet publish src/Taskboard.Mcp/Taskboard.Mcp.csproj -c Release -o ~/.agent-harness/publish-mcp
systemctl --user daemon-reload
systemctl --user start harness-server
```

Rollback: parar o serviço, `mv publish.prev publish`, start.

## Verificação pós-deploy

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:47823/health     # 200
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:47823/api/meta   # 200
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:47823/           # 200 (UI)
# anônimo deve ser 401:
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:47823/api/agent-clis
curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://127.0.0.1:47823/terminal-hub/negotiate?negotiateVersion=1"
```

Autenticado (login via `HARNESS_ADMIN_*` do env): `GET /api/agent-clis`
retornou 5/5 CLIs `installed=true` + `authenticated` em 2026-09-17 —
credenciais já existem em `/home/ubuntu`.

## Modo Docker (alternativo)

`Dockerfile` raiz: runtime `dotnet/aspnet:10.0` + Node LTS 24.21.0
(multi-arch) + CLIs pré-instalados + `ENV HOME=/data/home` (credenciais
persistem no volume `/data`). Mounts usados: `~/.agent-harness/data:/data` e
`~/.agent-harness/data/dataprotection:/data/home/.aspnet/DataProtection-Keys`.
Imagem `agent-harness:latest` pode existir no daemon local.

## Notas operacionais

- O serviço é user-level: requer `systemctl --user` (lingering já habilitado na VPS).
- Se a porta 47823 estiver ocupada, verificar `ss -ltnp | grep 47823` — um
  processo antigo (ex.: container Docker root) já bloqueou o bind antes.
- Skills/MCP/agent-clis escrevem em `HOME` — no serviço `HOME=/home/ubuntu`
  (setado na unit), então as credenciais reais do usuário são detectadas.
- **NotFound do router Blazor após deploy**: se a UI mostrar "Sorry, there's
  nothing at this address." numa rota que existe no código, primeiro
  verificar se a aba do browser está com um WASM antigo em memória (hard
  refresh `Ctrl+F5` resolve) e se `_framework/` não tem manifests stale
  (contar `Taskboard.Blazor.*.wasm` e `Taskboard.Client.*.wasm` — deve ser
  exatamente 1 de cada). Os manifests não-fingerprinted (`dotnet.js`,
  `blazor.webassembly.js`) são servidos com `no-cache`, mas uma aba já aberta
  continua com o bundle antigo.
