# SPEC-20261021-worktree-setup-scripts: setup/archive hooks + startup policy em .harness/worktree.yaml

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Worktree setup/archive scripts — hooks de lifecycle no provisionamento |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Integrations (worktree provisioning) + Application + Blazor (worktree detail) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/worktree-setup-scripts` |
| Status | `Draft` |
| Depends on | `SPEC-20261020-worktree-shared-context` (P1+P2 shipped — `.harness/worktree.yaml`, `SharedPathProvisioner`) |
| Reference | Orca `docs/model/orca-yaml` (`scripts.setup`/`scripts.archive`, `setupAgentStartupPolicy`, `defaultTabs`, `issueCommand`, `environmentRecipes`); README "Compared to Orca" gap ① |

## 1. Executive Summary

### Problem

`.harness/worktree.yaml` hoje só declara shared paths/excludes — a worktree nasce
com `node_modules` copiado mas **nada executa** os comandos que tornam a
worktree utilizável (`pnpm install`, `dotnet restore`, codegen). O worker
perde os primeiros minutos rodando setup que já é conhecido. Além disso,
`worktree delete` não oferece gancho para arquivar logs/dumps antes de
destruir o diretório.

### Solution

Estender o schema `.harness/worktree.yaml` com:

1. **`setup:`** — lista de comandos shell executados na worktree nova, após
   shared paths, em um terminal "Setup" dedicado visível na UI. Falha com
   exit≠0 marca a worktree `setup_failed` mas não a destrói.
2. **`archive:`** — lista de comandos executados antes de `worktree delete`
   (ex.: copiar `coverage/` para o repo principal). Falha → warning, delete
   prossegue.
3. **`agentStartupPolicy:`** — `wait-for-setup` (default: worker dispatch só
   após setup terminar) ou `start-immediately` (worker e setup em paralelo).
4. **`defaultTabs:`** — lista de `{name, command}` aberta como tabs PTY
   extras ao abrir a worktree na UI.
5. **`issueCommand:`** — template de prompt (`{issueUrl}`, `{issueTitle}`)
   usado por `delegate_task` quando a task referencia issue GitHub/Jira.

### Scope

In scope: schema novo + executor de setup/archive + política de startup +
defaultTabs + issueCommand template. Out of scope: `environmentRecipes`
(VM provisioning — pertence a remote-hosts), approval flow para comandos
não-confiáveis (assume-se que o yaml no repo já é trusted como o código).

## 2. Requisitos

### Funcionais

- RF-001 Schema `.harness/worktree.yaml` v2:
  `shared:`/`exclude:` (existente) + `setup: [cmd]` + `archive: [cmd]` +
  `agentStartupPolicy: wait-for-setup|start-immediately` +
  `defaultTabs: [{name, command}]` + `issueCommand: "template"`.
  Config global `Taskboard:Worktree:Setup`/`SetupPolicy`/`DefaultTabs`/
  `IssueCommand` espelha os campos; precedência config > yaml.
- RF-002 `WorktreeSetupRunner` (Integrations): executa `setup[]` sequencial
  via `IProcessRunner` na worktree (cwd=worktree), env sanitizado pelo
  `SecurityEnvironment` existente, timeout por comando
  (`Taskboard:Worktree:SetupTimeoutSeconds`, default 600, clamp 30–3600),
  stdout/stderr capturados em `WorktreeCard.SetupLog` (tail ≤64KB).
- RF-003 Estado: `WorktreeCard.SetupState(pending|running|ok|failed|skipped)`
  + `SetupStartedAtUtc`/`SetupFinishedAtUtc`/`SetupExitCode`; persistido via
  EF + patch SignalR `worktree.card` (reuso do canal P2).
- RF-004 `agentStartupPolicy=wait-for-setup`: `delegate_task` marca o dispatch
  `provisioning` até `SetupState ∈ {ok, failed, skipped}` — worker recebe
  `setupState` + tail do log no preamble. `start-immediately` não bloqueia.
- RF-005 `archive[]` executa antes de `DELETE /api/worktrees/{runId}` e de
  qualquer cleanup automático; exit≠0 loga warning, não aborta o delete.
- RF-006 `defaultTabs[]`: `POST /api/worktrees`/`GET worktree detail` retorna
  `defaultTabs`; a UI oferece "Open setup tabs" que cria uma PTY session por
  entrada (`name` = título da tab, `command` executado na worktree).
- RF-007 `issueCommand`: quando `delegate_task` recebe `issueUrl`/`issueKey`,
  o preamble é construído do template (variáveis `{issueUrl}`, `{issueKey}`,
  `{issueTitle}`, `{repo}`); fallback = preamble atual quando ausente.
- RF-008 `worktree_status` tool enriquecido: `setupState`, `setupLogTail`,
  `defaultTabs[]` além do git status + sharedPaths + card (consolida RF-010
  da spec anterior).

### Não-funcionais

- Setup roda em background — `worktree add` retorna imediato; card mostra
  spinner `running`. Sem limite de concorrência além do semaphore global de
  processos (`MaxConcurrentProcesses`).
- Comandos executados via `bash -lc` (login shell para PATH de
  nvm/sdkmanager); `set -e` implícito por exit code — não concatenar `&&`.
- Secrets: `SecretScrubber` existente mascara env no log capturado.
- YAML validado: `setup`/`archive` ≤20 comandos, cada comando ≤2000 chars,
  `defaultTabs` ≤10 entradas, `issueCommand` ≤4000 chars — violação →
  `invalid_worktree_yaml` + campo ignorado.

## 3. Arquitetura

```mermaid
flowchart TB
  DT[delegate_task use_worktree] --> Add[git worktree add]
  Add --> Prov[SharedPathProvisioner]
  Prov --> Setup[WorktreeSetupRunner]
  Setup -->|wait-for-setup| Gate{SetupState ok?}
  Gate -->|yes| Disp[dispatch worker]
  Gate -->|failed| DispF[dispatch c/ setupState=failed no preamble]
  Setup -->|start-immediately| Disp
  Del[DELETE worktree] --> Arch[run archive[]] --> Rm[git worktree remove]
```

- `WorktreeSetupRunner` em Integrations (usa `IProcessRunner` +
  `ISecretScrubber`); `WorktreeCard` ganha campos Setup*.
- `defaultTabs`/`issueCommand` são dados — resolvidos por um novo
  `IWorktreeYamlReader` (extrai leitura do yaml do provisioner para reuso).
- UI: worktree detail ganha seção "Setup" (estado, log tail, botão Re-run)
  + botão "Open setup tabs".

## 4. Fases

- **P1** — schema v2 + `IWorktreeYamlReader` + `WorktreeSetupRunner` +
  `SetupState` + wait-for-setup gate + preamble `setupState`.
- **P2** — `archive[]` no delete + `defaultTabs` (API + UI "Open setup tabs")
  + UI seção Setup com Re-run.
- **P3** — `issueCommand` template no `delegate_task` + `worktree_status`
  enriquecido.

## 5. Testes

- Unit: schema v2 parse/validação (limites, campo inválido não derruba o
  arquivo); precedência config>yaml; template `issueCommand` substitui
  variáveis e rejeita `{{`/`}}` e `$(...)`.
- Unit: `wait-for-setup` bloqueia dispatch até `ok`; `start-immediately`
  despacha com `running`.
- Unit: archive roda antes do `worktree remove`; falha de archive não aborta.
- Integration: worktree criada com `setup: ["touch .setup-ran"]` → arquivo
  existe e `SetupState=ok` no card; `DELETE` executa archive.
- Integration: yaml inválido → `SetupState=skipped` + campo loga razão.

## 6. Open questions

1. Re-run de setup deve resetar `SetupState` para `running`? Proposta: sim,
   e sobrescreve `SetupLog` (run anterior vira `PreviousSetupRuns[1]` cap 1).
2. Setup commands rodam como o usuário do server — precisa opt-in por
   `Taskboard:Worktree:SetupEnabled` (default true)? Proposta: default true
   para repos confiáveis já registrados; repos adicionados via
   `attach`-only exigem `Trusted=true`.
3. `defaultTabs` deve auto-abrir ou ficar atrás do botão? Proposta: atrás do
   botão "Open setup tabs" — auto-spawn de processos é surpresa.
