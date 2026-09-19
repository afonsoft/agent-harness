# Deployment — VPS (host mode)

> Estado confirmado em 2026-09-17. O deploy de produção desta VPS é **no host**
> via systemd user service. Docker é o cenário alternativo (imagem definida no
> `Dockerfile` raiz); o container `taskboard` foi removido em 2026-09-17.

## Topologia

| Item | Valor |
|---|---|
| Serviço | `taskboard-server.service` (systemd **user**, `enabled`, `Restart=on-failure`) |
| Unit | `~/.config/systemd/user/taskboard-server.service` |
| Launcher | `~/.taskboard/bin/taskboard-server` (bash, sourceia `~/.taskboard/env`) |
| Env file | `~/.taskboard/env` (gerado pelo `install.sh`; editado manualmente — ver abaixo) |
| Publish | `~/.taskboard/publish` (`dotnet publish -c Release -o ~/.taskboard/publish`) |
| DataDir | `~/.taskboard/data` (`taskboard.sqlite`, `admin.json`, `skills-cache`, `dataprotection`) |
| Porta | `127.0.0.1:47823` (`ASPNETCORE_URLS` default no launcher) |

## Chaves em `~/.taskboard/env`

`TASKBOARD_DATA_DIR`, `Taskboard__DataDir`, `TASKBOARD_ADMIN_USERNAME`,
`TASKBOARD_ADMIN_PASSWORD`, `TASKBOARD_URL`, `GITHUB_TOKEN` (copiado do env do
container Docker em 2026-09-17), `PATH` customizado — **nunca commitar nem
imprimir valores**.

### PATH do serviço (armadilha)

O PATH default do systemd (`/usr/local/bin:/usr/bin:...`) **não alcança** os
agent CLIs — no host eles vivem em:

- `~/.nvm/versions/node/v24.16.0/bin` → `node`, `npx`, `claude`, `codex`, `opencode`
- `~/.local/bin` → `devin`, `agy`

A linha PATH do env file resolve o nvm dinamicamente (pega a versão mais nova):

```bash
export PATH="$HOME/.taskboard/bin:$HOME/.local/bin:$(ls -d $HOME/.nvm/versions/node/*/bin 2>/dev/null | sort -V | tail -1):$PATH"
```

Sem isso, `/api/agent-clis` reporta tudo `installed=false` e o skills-install
falha por falta de `npx`. Backup do env: `~/.taskboard/env.bak-*`.

## Deploy flow

```bash
cd ~/repos/taskboard-ai
git checkout main && git pull --ff-only
systemctl --user stop taskboard-server
# `dotnet publish -o` NÃO limpa o output dir — bundles fingerprinted
# (_framework/*.hash.wasm, dotnet.*.js) acumulam entre deploys e o runtime
# pode resolver um manifest antigo (sintoma 2026-09-19: Board → "Sorry,
# there's nothing at this address." porque o wasm carregado era anterior
# ao restore da rota `/`). Limpar antes de publicar:
rm -rf ~/.taskboard/publish/wwwroot/_framework
dotnet publish src/Taskboard.Server/Taskboard.Server.csproj -c Release -o ~/.taskboard/publish
systemctl --user daemon-reload
systemctl --user start taskboard-server
```

## Verificação pós-deploy

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:47823/api/meta   # 200
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:47823/           # 200 (UI)
# anônimo deve ser 401:
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:47823/api/agent-clis
curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://127.0.0.1:47823/terminal-hub/negotiate?negotiateVersion=1"
```

Autenticado (login via `TASKBOARD_ADMIN_*` do env): `GET /api/agent-clis`
retornou 5/5 CLIs `installed=true` + `authenticated` em 2026-09-17 —
credenciais já existem em `/home/ubuntu`.

## Modo Docker (alternativo)

`Dockerfile` raiz: runtime `dotnet/aspnet:10.0` + Node LTS 24.21.0
(multi-arch) + CLIs pré-instalados + `ENV HOME=/data/home` (credenciais
persistem no volume `/data`). Mounts usados: `~/.taskboard/data:/data` e
`~/.taskboard/data/dataprotection:/data/home/.aspnet/DataProtection-Keys`.
Imagem `taskboard-ai:latest` pode existir no daemon local.

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
  (contar `Taskboard.Blazor.*.wasm` — deve ser exatamente 1). Os manifests
  não-fingerprinted (`dotnet.js`, `blazor.webassembly.js`) são servidos com
  `no-cache`, mas uma aba já aberta continua com o bundle antigo.
