# SPEC-20260918 — CLI Agents Expansion: 8 novos CLIs + instalação pela UI com popup de logs

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `cli-agents-expansion` |
| Type | `Feature` (Frontend + API + Infra) |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / Blazor WASM / SignalR` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260918-cli-agents-expansion` |
| Ticket | N/A |
| Status | `Done` — entregue via PR #94 (merged) |

## 1. User Story

Como administrador do Taskboard, quero suportar novos agentes CLI (Kimi
Code, Grok, Aider, Cline, Continue, GitHub Copilot CLI, Qwen Code e Kiro
CLI) na página **CLI Agents**, instalando qualquer um deles direto da UI —
com um popup exibindo os logs da instalação em tempo real — para que eu
possa executar tarefas do board com o agente de minha preferência sem sair
da aplicação.

Contexto: hoje `/agents` lista 5 CLIs (Devin, Claude, Codex, OpenCode,
Antigravity). O botão "Install" atual apenas *digita* um hint de comando no
`/terminal` — o usuário quer instalação real com feedback ao vivo. Após
instalado, a linha deve oferecer **Login**; antes, **Install**.

## 2. Scope

### In scope

- 8 novos membros em `AgentType` e `AgentCliKind`: `Kimi`, `Grok`, `Aider`,
  `Cline`, `Continue`, `Copilot`, `Qwen`, `Kiro`.
- Specs em `AgentCliMap` (binary, config dir, credential probe, login
  command, install command) para os 8 novos + comandos de install reais
  para os 5 existentes.
- Invocação não-interativa por CLI em `AgentCliInvocation` (orquestração).
- Probe de status (`AgentCliStatusService`) para os novos binários.
- `POST /api/agent-clis/{kind}/install` — instalação real em background
  com allowlist fixa de comandos server-side (nada arbitrário do client).
- `GET /api/agent-clis/{kind}/install/status` — estado + linhas de saída
  do último run (buffer em memória, mesmo padrão dos operation logs).
- `/agents`: botão **Install** quando não instalado (executa de verdade),
  **Login** quando instalado/não autenticado (link `/terminal?cmd=` atual),
  badges quando autenticado; pré-requisito ausente desabilita o Install
  com hint.
- Modal de instalação com console readonly (padrão `agent-log-console`)
  exibindo stdout/stderr ao vivo via polling (~1s); fecha sozinho em
  sucesso, permanece aberto com erro em falha.
- MCP provisioning: file-merge para kimi/grok/qwen/kiro/copilot; CLI
  nativo para cline; continue via YAML (com fallback `Skipped`); aider
  `Skipped`.
- Skills sync: mapeamento de diretórios por CLI (fallback
  `~/.<nome>/skills`; sem diretório conhecido → `Skipped`).
- Enabled-agents resolver inclui os novos agentes por padrão.

### Out of scope

- Desinstalação de CLIs.
- Login automatizado (OAuth flows interativos continuam manuais via
  terminal).
- Provisionamento de API keys (env vars como `XAI_API_KEY`,
  `CONTINUE_API_KEY`, `KIRO_API_KEY` são responsabilidade do admin).
- MCP via ACP/outros protocolos além dos targets documentados.
- Execução concorrente de múltiplas instalações do mesmo CLI.

## 3. Technical Context

### Ler antes de implementar

- `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` — specs por CLI
  (`AgentCliSpec`: DisplayName, Binary, ConfigDirDisplay,
  CredentialRelativePath, LoginCommand, InstallHint).
- `src/Taskboard.Domain.Shared/Agents/AgentType.cs` — enum de orquestração.
- `src/Taskboard.Application.Contracts/Agents/AgentCliInvocation.cs` —
  argv por CLI + `PreviewCommandLine`.
- `src/Taskboard.Integrations/Agents/AgentCliStatusService.cs` — probe
  PATH/version/auth.
- `src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs` — execução.
- `src/Taskboard.Integrations/Skills/ISkillsInstallRunner.cs` +
  `ProcessSkillsInstallRunner` — runner de processo reutilizável para
  install.
- `src/Taskboard.Application.Contracts/Operations/OperationLog.cs` —
  ring buffer (padrão a reusar para o log de install).
- `src/Taskboard.Server/Program.cs` — endpoints `agent-clis` (~linha 1464).
- `src/Taskboard.Blazor/Components/Pages/Agents.razor` — tabela + botões.
- `src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor` — console de
  log reutilizável como referência visual.
- `src/Taskboard.Domain.Shared/Mcp/AgentMcpConfigMap.cs` — targets MCP.
- `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` —
  provisioning file-merge + CLI nativo (padrão `agy` em
  `ProvisionAntigravityAsync`).
- `src/Taskboard.Domain.Shared/Skills/AgentSkillDirectoryMap.cs` — dirs
  de skills por agente.

### Criar

- `src/Taskboard.Application.Contracts/Agents/AgentCliInstallStatus.cs`
  (ou equivalente): `{ kind, state(Idle|Running|Succeeded|Failed),
  startedAtUtc, lines[] }`.
- `src/Taskboard.Integrations/Agents/AgentCliInstallService.cs` —
  allowlist de comandos + execução em background + buffer de linhas por
  kind (singleton; um run por vez por kind).
- `tests/.../AgentCliInstallServiceTests.cs`, testes de invocação e de
  MCP para os novos targets.

### Modificar

- `AgentType.cs`, `AgentCliMap.cs` (+`CliToType`), `AgentCliInvocation.cs`,
  `KnownCliAgentAdapter.cs` (se necessário), `AgentMcpConfigMap.cs`,
  `McpProvisioningService.cs`, `AgentSkillDirectoryMap.cs`,
  `Program.cs` (endpoints + DI), `Agents.razor`, `TaskboardClient.cs`,
  `site.css` (se preciso).

### Tabela de invocação (a validar via `--help` no host durante impl)

| CLI | Binary | Install | Headless |
|---|---|---|---|
| Kimi Code | `kimi` | `bash -c "curl -fsSL https://code.kimi.com/kimi-code/install.sh \| bash"` | `kimi -p <prompt>` |
| Grok | `grok` | `bash -c "curl -fsSL https://x.ai/cli/install.sh \| bash"` | `grok -p <prompt>` |
| Aider | `aider` | `pipx install aider-chat` | `aider --yes-always --message <prompt>` |
| Cline | `cline` | `npm install -g cline` | `cline <prompt>` (auto-approve é default) |
| Continue | `cn` | `npm install -g @continuedev/cli` | `cn --auto -p <prompt>` |
| Copilot | `copilot` | `npm install -g @github/copilot` | `copilot --allow-all-tools -p <prompt>` |
| Qwen | `qwen` | `npm install -g @qwen-code/qwen-code@latest` | `qwen -p <prompt>` |
| Kiro | `kiro-cli` | `bash -c "curl -fsSL https://cli.kiro.dev/install \| bash"` | `kiro-cli chat --no-interactive --trust-all-tools <prompt>` |

### Tabela MCP

| CLI | Target | Mecanismo |
|---|---|---|
| Kimi | `~/.kimi-code/mcp.json` → `mcpServers[n] = {url, headers}` | JsonConfigMerger |
| Grok | `~/.grok/config.toml` → `[mcp_servers.n] url+headers` | TomlConfigMerger |
| Qwen | `~/.qwen/settings.json` → `mcpServers[n] = {httpUrl, headers}` | JsonConfigMerger (`httpUrl`) |
| Kiro | `~/.kiro/settings/mcp.json` → `mcpServers[n] = {url, headers}` | JsonConfigMerger |
| Copilot | `~/.copilot/mcp-config.json` → `mcpServers[n] = {type:"http", url, headers}` | JsonConfigMerger |
| Cline | `~/.cline/data/settings/cline_mcp_settings.json` → `mcpServers[n].transport` | `cline mcp add/remove` |
| Continue | `~/.continue/mcpServers/<name>.json` (auto-pickup de JSON files) | file-drop JSON |
| Aider | — | `Skipped` (sem suporte MCP) |

### Credential probes (fallback `null` → `Unknown` quando incerto)

| CLI | Candidato |
|---|---|
| Qwen | `.qwen/oauth_creds.json` |
| Kimi | `.kimi-code/` (investigar) |
| Grok | `.grok/` (investigar; `XAI_API_KEY` é env) |
| Cline | `.cline/` (investigar) |
| Continue | `.continue/` (investigar) |
| Copilot | `.copilot/` (investigar; `GH_TOKEN` é env) |
| Kiro | `.kiro/` (investigar; `KIRO_API_KEY` é env) |
| Aider | `null` (auth via env de provider) |

## 4. Requirements

- **RF-001**: `AgentType` e `AgentCliKind` ganham os 8 membros; `AgentCliMap`
  registra spec completo de cada um (incluindo install command real para
  os 5 CLIs existentes).
- **RF-002**: `AgentCliInvocation.BuildArguments`/`PreviewCommandLine`
  cobrem os 8 novos CLIs com os templates da tabela de invocação.
- **RF-003**: `AgentCliStatusService` reporta os novos CLIs (installed,
  version, auth) sem quebrar os existentes; credential path incerto →
  `Unknown`.
- **RF-004**: `POST /api/agent-clis/{kind}/install` — 202, dispara run em
  background com o comando allowlisted do kind; kind desconhecido → 404;
  run já em andamento → 200 com status atual (idempotente); requer auth.
- **RF-005**: `GET /api/agent-clis/{kind}/install/status` — retorna
  `{state, startedAtUtc, exitCode?, lines[]}`; buffer em memória (últimas
  ~500 linhas); requer auth. Sem run anterior → `Idle`.
- **RF-006**: `AgentCliInstallService` executa o comando via
  `ISkillsInstallRunner` (ou equivalente) capturando stdout+stderr linha a
  linha no buffer; timeout ~10 min; falha não trava runs futuros.
- **RF-007**: `/agents` — por linha: `!installed` → botão **Install**
  primário (desabilitado se pré-requisito ausente, com tooltip indicando
  `npm`/`pipx`/`curl`); `installed && !authenticated` → **Login**
  (comportamento atual); `installed && authenticated` → badge. Pré-
  requisitos reportados pelo status endpoint (`prerequisites` map).
- **RF-008**: Ao clicar Install abre modal com console readonly mostrando
  as linhas em tempo real (polling 1s do status endpoint); sucesso →
  modal fecha sozinho e a tabela recarrega; falha → modal permanece com
  erro destacado + botão Fechar.
- **RF-009**: `AgentMcpConfigMap` + `McpProvisioningService` provisionam
  os novos CLIs conforme a tabela MCP; cline via processo (padrão
  `ProvisionAntigravityAsync` com runner injetável); continue YAML
  best-effort → `Skipped` se formato não confirmado; aider → `Skipped`.
- **RF-010**: `AgentSkillDirectoryMap` mapeia dirs de skills dos novos
  CLIs confirmados em docs/código-fonte; não confirmado → fallback
  `~/.<nome>/skills`; CLI sem conceito de skills (aider) → não mapeado.
- **RF-011**: Enabled-agents resolver trata os novos agentes como os
  atuais (habilitados por padrão quando não há preferência); eles aparecem
  na seleção de execução e no toggle do Settings.
- **RF-012**: Comandos de install são allowlist estática server-side — o
  client envia apenas `{kind}`; nenhum argumento livre é aceito.

## 5. API Contract

```text
POST /api/agent-clis/{kind}/install        → 202 {state:"Running"} | 200 {state:"Running"} | 404
GET  /api/agent-clis/{kind}/install/status → 200 {kind, state, startedAtUtc?, exitCode?, lines:[{atUtc, stream, content}]}
```

`{kind}` é o nome do `AgentCliKind` (case-insensitive). Ambos
`RequireAuthorization`. `state` ∈ `Idle | Running | Succeeded | Failed`.

## 6. Acceptance Criteria

- **AC-01**: Dado um CLI não instalado, quando o admin abre `/agents`,
  então a linha mostra botão **Install** (não Login).
- **AC-02**: Dado um click em Install, quando o install termina com
  sucesso, então o modal fecha sozinho e a linha passa a exibir Login ou
  badge autenticado.
- **AC-03**: Dado um install que falha, então o modal permanece aberto
  com o erro visível e o buffer contém as linhas de stderr.
- **AC-04**: Dado `POST .../install` com kind inexistente, então 404.
- **AC-05**: Dado os novos `AgentType`, quando `AgentCliInvocation`
  monta o argv, então cada CLI recebe seu template (tabela) com o prompt
  como último argumento.
- **AC-06**: Dado RAG URL configurada, quando sync MCP roda, então kimi/
  grok/qwen/kiro/copilot recebem a entry nos seus arquivos e cline via
  `cline mcp`; aider/continue aparecem `Skipped` se não provisionáveis.
- **AC-07**: Dado pré-requisito ausente (ex.: sem `npm`), então Install
  desabilitado com tooltip do pré-requisito faltante.
- **AC-08**: Nenhuma saída de install expõe secrets (sanitização de
  tokens em stderr/stdout se detectáveis).

## 7. Task Plan

1. **T1 — Enums + specs**: 8 membros em `AgentType`/`AgentCliKind`,
   `AgentCliMap` (install real para todos os 13), `CliToType`.
2. **T2 — Invocação**: templates em `AgentCliInvocation` + testes de
   argv/preview por CLI.
3. **T3 — Install service**: allowlist + runner + buffer + tests
   (runner fake: sucesso, falha, timeout, output capturado).
4. **T4 — Endpoints**: `POST .../install` + `GET .../install/status` +
   DI + testes de integração (202/404/auth).
5. **T5 — UI**: `Agents.razor` estados de botão + modal console +
   polling + `TaskboardClient` métodos.
6. **T6 — MCP**: targets file-merge (kimi/grok/qwen/kiro/copilot) +
   cline via processo + continue (investigar → merge ou Skipped) +
   testes.
7. **T7 — Skills/enabled**: dirs de skills confirmados + resolver.
8. **T8 — Validação**: build + suite completa; smoke install real de 1
   CLI npm no host; validar `--help` dos headless flags acessíveis.
9. **T9 — Docs/PR/deploy**: `docs/features*.md`, spec Done, PR, merge,
   publish + restart.

### Validação

`dotnet build` 0 warnings; unit + integration verdes; smoke de install e
de `--help` no host quando o CLI estiver disponível; manual pós-deploy:
install via popup, badges, sync MCP.

## 8. Organization Guardrails

- Branch `feature/devin-20260918-cli-agents-expansion`; nunca commit
  direto em `main`.
- Allowlist estática de comandos — sem input arbitrário do client
  (segurança: evita command injection via endpoint).
- Não logar nem persistir tokens/API keys; sanitizar output de install
  quando contiver padrões de credencial.
- Instalações modificam o host (npm -g, curl|bash): executar apenas por
  ação explícita do admin autenticado; um run por kind por vez.
- Specs de CLI não devem inventar paths de credencial — incerto →
  `null`/`Unknown` e investigação registrada.

## 9. Definition of Done

- [ ] 8 CLIs visíveis em `/agents` com status Install/Login/badge corretos.
- [ ] Install real funciona com popup de logs que fecha em sucesso.
- [ ] Novos agentes executáveis via orquestração com argv correto.
- [ ] MCP provisioning cobre os novos CLIs conforme tabela (ou Skipped
  justificado).
- [ ] Testes unit/integration novos verdes + suite completa.
- [ ] `dotnet build` sem warnings.
- [ ] Docs `features.md`/`features.pt-br.md` atualizados.
- [ ] Spec `Status: Done`; PR merged; `taskboard-server` redeployed e
  saudável.
