# SPEC-20261004-chat-workspace-cli-registry: Workspace picker no Agent chat + registry de CLI enriquecido

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Agent-chat workspace picker + CLI registry enrichment |
| Product / System | agent-harness |
| Module / Bounded Context | Blazor + Server + Application + Integrations + Domain |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Status | `Done` — entregue via [PR #462](https://github.com/afonsoft/agent-harness/pull/462) (merged 2026-10-04) |
| Date | 2026-10-04 |
| Target agent | Devin |
| Related SPECs | SPEC-20261003-ai-code-agent-chat, SPEC-20260928-ai-code-generic-cli, SPEC-20260917-cli-agents-terminal, SPEC-20260918-cli-agents-expansion, SPEC-20260918-agent-execution-ux |
| Reference | análise comparativa stablyai/orca (sessão devin-bb183731c8ed4a61928f2fdccefe5121) |

---

## 1. Executive Summary

### Problem

1. **Sem workspace picker**: a conversa persiste `ChatAgentContext.WorkspacePath`
   e as tools executam nele, mas não há UI para escolhê-lo — o workdir é
   sempre `~/repos` (ou `<root>/<repo>`). Pedido do usuário: default
   `~/repos`, subpasta selecionável.
2. **`WorkspacePath` sem clamp**: PATCH de conversa grava qualquer string; as
   tools recebem um path arbitrário (fora de `$HOME` inclusive). O vscode
   mount já clampa para `$HOME` (SPEC-20260917 RF-006) — a conversa não.
3. **"Qualquer agent" incompleto**: `run_agent`/`run_cli` só aceitam os
   builtins `AgentType`; os `AgentCliDefinition` custom (o equivalente ao
   `CUSTOM_AGENT_ID` do orca) só servem para threads PTY/ACP — delegação exec
   não alcança defs. O registry também não tem aliases de detecção,
   required-commands, prompt delivery `stdin`, nem `--` separator.
4. **Model discovery existe** (`AgentModelCatalogService` + `ModelListProbe` +
   fonte `probe` no catálogo) — falta expor modelos para CLIs custom
   (`ModelListArgs` na def) e free-text de modelo no picker para defs sem
   lista.

### Objective

- Workspace picker no Agent bar: modal lista subpastas a partir de
  `~/repos` (navegável dentro de `$HOME`), seleção persiste no
  `agent.WorkspacePath` da conversa; default permanece `~/repos`.
- Clamp de `WorkspacePath` (create+PATCH) para `$HOME`, senão cai no root.
- Registry: `AgentCliSpec` ganha `Aliases`, `RequiredCommands`,
  `ExpectedProcess`, `ArgvPromptSeparator`, `PromptDelivery`;
  `AgentCliDefinition` ganha `PromptDelivery` (`argv`|`stdin`) e
  `ModelListArgs` (probe opcional). `run_agent`/`run_cli` alcançam defs
  custom; `run_agent` honra `stdin` delivery.

---

## 2. Requirements

### RF-001 — Endpoint de diretórios do workspace

`GET /api/local/workspace/dirs?path=` →
`{ path, parent, entries: [{ name, path }] }`.

- `path` omitido → workspace root (`WorkspaceService.EnsureRoot()`).
- `path` fora de `$HOME` → 400 `workspace_path_outside_home` (não clamp
  silencioso — o picker precisa saber).
- `parent` = pai quando ainda sob `$HOME`, senão `null`.
- `entries`: subdiretórios do path, ordenados, sem seguir symlinks, sem
  ocultos (`.`-prefixed) exceto quando explicitamente navegados.
- Max 200 entradas (truncated flag se exceder).

### RF-002 — Clamp do WorkspacePath

`ChatService.CreateConversationAsync`/`PatchConversationAsync` normalizam
`agent.WorkspacePath` via `workspace.ClampToHome`: fora de `$HOME` → root do
workspace (não `$HOME` — o default pedido é `~/repos`). Path existente não é
exigido (tools criam sob demanda) — só o clamp estrutural.

### RF-003 — Workspace picker no Agent bar

Toolbar do agent-chat ganha botão `workdir` (ícone `Folder2Open`) mostrando
o label `~/repos`, `repos/<repo>` ou o path relativo a `$HOME`. Abre modal
"Workspace": breadcrumb do path atual, lista de subpastas (click navega),
"↑ parent", "Use this folder" (seleciona o path exibido), "Reset to
~/repos". Seleção → `PATCH conversations/{id}` com
`agent.WorkspacePath` (mesma lógica dos outros pickers — `ProviderChat`
recebe `AgentContext` atualizado; remount não é necessário, o PATCH atualiza
a conversa em uso no próximo send).

`TaskboardClient` ganha `GetWorkspaceDirsAsync(path?)`.

### RF-004 — `AgentCliSpec` enriquecido

Novos campos (todos com default que preserva o comportamento atual):

```csharp
public sealed record AgentCliSpec(
    string DisplayName, string Binary, string ConfigDirDisplay,
    string? CredentialRelativePath, string LoginCommand, string InstallHint,
    AgentCliInstallSpec Install,
    IReadOnlyList<string> Aliases,           // binários alternativos no PATH
    IReadOnlyList<string> RequiredCommands,  // devem existir p/ detectar
    string? ExpectedProcess,                 // nome do processo esperado
    bool ArgvPromptSeparator,                // "--" antes do prompt posicional
    AgentCliPromptDelivery PromptDelivery);  // Argv | Stdin
```

`enum AgentCliPromptDelivery { Argv, Stdin }`.

Populados onde conhecido: `kiro-cli` alias `kiro`; `agy` alias
`antigravity`; `cn` alias `continue`; `claude` `RequiredCommands=["node"]`
quando instalado via npm é falso-positive risk — deixar `[]` onde duvidoso;
`ArgvPromptSeparator=true` apenas para CLIs que precisam (`aider` usa flag,
não posicional — manter false; deixar o campo para specs futuras).

### RF-005 — Detecção usa aliases + required commands

`AgentDiscoveryService`/`AgentCliStatusService` resolvem `Binary` **ou**
qualquer `Aliases` entry no PATH; `RequiredCommands` vazios não mudam nada.
`DockerCliDiscovery` aplica o mesmo.

### RF-006 — `AgentCliDefinition` com `PromptDelivery` + `ModelListArgs`

Novos campos persistidos (migration `AddAgentCliDefinitionDelivery`):
`PromptDelivery` (`"argv"` default | `"stdin"`) e `ModelListArgs`
(string nullable — argv extra para listar modelos, uma linha por modelo;
`null` = sem probe).

`BuildArgs` inalterado; a entrega stdin é resolvida pelo executor: quando
`PromptDelivery == "stdin"` e `ArgsTemplate` não contém `{prompt}`, o prompt
vai para stdin do processo.

### RF-007 — `run_agent`/`run_cli` alcançam defs custom

- `run_agent` aceita `agent` = id (`custom-foo`) ou display name de def
  habilitada → monta `AgentCommand` via `def.BuildArgs(prompt, model)` com
  `PromptDelivery` honrado; workdir = `context.WorkspacePath`.
- `run_cli` allowlist += `Executable` de defs habilitados (binary ou path
  absoluto declarado pelo usuário).
- `IAgentCliDefinitionRepository` injetado nas tools (lista habilitada,
  scoped — não a request inteira em cache).

### RF-008 — Model picker para defs custom

Modal de modelo no agent bar: para CLI custom com `ModelListArgs`, probe
(`ProcessRunner` bounded, timeout 10s, cache 120s via `CliProbeSnapshotService`
por def-id) preenche o select; sem args ou probe vazio → campo free-text
"model name (optional)" no lugar do select.

## 3. Arquitetura / Design

- Endpoint vive no grupo `local` junto aos endpoints de chat/workspace
  existentes em `Program.cs`.
- `IWorkspacePathResolver` ganha `ListSubdirs(string? path)` →
  `(string Path, string? Parent, IReadOnlyList<(string Name, string Path)>)`.
- Detecção alterada apenas internamente — sem mudança de contrato nos DTOs de
  status (o path encontrado continua sendo o resolvido).
- Custom-def exec segue o mesmo harness de processo dos builtins
  (`ProcessRunner` com timeout/limits) — nunca shell-interpretado.

## 4. Edge cases

- `path` symlink para fora de `$HOME`: resolve fullpath real antes do check.
- `WorkspacePath` relativo (`foo/bar`): resolve contra root.
- Def com `PromptDelivery=stdin` mas `ArgsTemplate` com `{prompt}`: argv
  ganha `{prompt}` renderizado e stdin NÃO recebe nada (argv wins).
- Probe de modelo que retorna lixo (binário que printa banner): limita a
  500 linhas, strip ANSI, descarta linhas vazias.

## 5. Fora de escopo

- Orquestração (mailbox/DAG/fan-out) → SPEC-20261004-p2.
- Dashboard/session-history/checkpoints → SPEC-20261004-p3.
- Prompt-injection PTY (orcax `draftPasteReadySignal`) — o agent-chat não usa
  PTY.

## 6. Testes

- `WorkspaceServiceTests`: dirs list, clamp, fora-de-home → root/400, symlink.
- `ChatServiceTests`: PATCH com WorkspacePath fora de home → persistido root.
- `AgentDiscoveryServiceTests`: alias resolve, required-command ausente → not installed.
- `AgentCliDefinitionTests`: PromptDelivery/ModelListArgs round-trip + validação.
- `DelegationToolsTests`: run_agent com def custom (argv e stdin), run_cli com def.
- `AiChat.razor` logic coberta por testes de estado quando possível (binding).
