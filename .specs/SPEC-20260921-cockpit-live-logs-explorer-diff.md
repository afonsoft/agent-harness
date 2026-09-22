# SPEC-20260921-cockpit-live-logs-explorer-diff

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cockpit-live-logs-explorer-diff` |
| Type | `Feature` (Frontend & UI / Cockpit UX) |
| Stack | `Blazor WebAssembly / Blazor.Bootstrap / SignalR / xterm.js / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/20260921-cockpit-live-logs-explorer` |
| Status | `Implemented` |
| Follows | `SPEC-20260919-ade-cockpit-hitl`, `SPEC-20260920-board-cockpit-unified-runs`, `SPEC-20260921-board-cockpit-agent-observability` |

---

## 1. User Story

**As a** usuário do Cockpit (`/cockpit/runs/{id}`)
**I want** acompanhar os logs do agente em tempo real com um terminal legível e controlável, navegar pelos arquivos do worktree como num explorer, e revisar o diff agrupado por arquivo com seções colapsáveis
**So that** eu consiga inspecionar a execução inteira — saída, código-fonte e alterações — sem sair da página nem abrir ferramentas externas.

### Problem Context

1. **Logs**: `agent_output` já chega ao vivo via SignalR (`HarnessCockpitHub`), mas o `RunTerminal` não dá controle ao usuário: sem auto-scroll follow, sem filtros (stage/stream), sem copiar/baixar a saída, e sem indicação clara de que o stream está ativo.
2. **Explorer**: não existe forma de ver o conteúdo do worktree — só o diff de arquivos alterados. Para ler um arquivo qualquer do repositório executado, o usuário precisa abrir o VS Code Web.
3. **Diff**: o `GitDiffViewer` despeja o patch inteiro num único `<pre>` contíguo — diffs com muitos arquivos ficam ilegíveis; não há lista de arquivos com contadores nem navegação por arquivo.

---

## 2. Scope

### In scope

- **Logs em tempo real (aba "Terminal" → "Logs")**: follow-scroll inteligente (auto-scroll só quando no fim + pill "↓ novos logs" quando o usuário sobe), toolbar com filtro por stage e por stream (stdout/stderr), copiar saída, baixar `.log`, limpar visão, e indicador live/stream.
- **Explorer de arquivos**: nova aba "Arquivos" no Cockpit run — árvore lazy do worktree (diretórios expandem sob demanda), viewer read-only do arquivo selecionado, `.git` sempre oculto, cap de tamanho para leitura e listagem.
- **Diff por arquivo**: lista de arquivos alterados, cada um colapsável (`+`/`-`), header com path + status + contadores por arquivo, expand-all/collapse-all, seções de patch agrupadas por path, e placeholder para arquivos sem patch (untracked).
- **Backend**: `IWorkspaceIsolationService` ganha `ListFilesAsync`/`ReadFileAsync` com guarda anti-traversal; `WorkspaceDiffFileDto` ganha contadores por arquivo (`--numstat` por path).

### Out of scope

- Edição de arquivos (explorer é read-only; edição fica no `/vscode/`).
- Diff side-by-side (mantém unified; side-by-side é follow-up).
- Syntax highlighting por linguagem no viewer (monospace simples nesta fase).
- Terminal interativo (stdin) — a aba continua read-only.

---

## 3. Technical Context

- `CockpitRun.razor`: abas `timeline | events | terminal | diff`; eventos `_events` alimentados por `GET /api/harness/runs/{id}/events` (replay do buffer, cap 500) + SignalR `ReceiveCockpitEvent` — já real-time.
- `RunTerminal.razor`: xterm.js read-only (`taskboardTerminal.initReadOnly`); dedup via `_written`; stderr em `\x1b[31m`, headers de stage em `\x1b[36m`.
- `terminal.js`: wrapper xterm compartilhado — ganha `isAtBottom`, `scrollToBottom`, `getText`.
- `GitWorktreeManager` (`IWorkspaceIsolationService`): dono dos paths de worktree (`WorktreePaths.IsUnder` para confinamento).
- `WorkspaceDiffDto`: `Files` (path+status de `git status --porcelain`) + `Patch` (`git diff <base>`) + totais de `--numstat`.

---

## 4. Functional Requirements

### RF-001: Logs ao vivo com follow-scroll

- **Description:** A aba Terminal do Cockpit exibe o stream `agent_output` em tempo real (SignalR já entrega por chunk; manter) com controle de scroll: quando o usuário está no fim, novas linhas auto-scrollam; quando sobe, o scroll fica parado e um pill "↓ novos logs" aparece — clique volta ao fim e reativa o follow.
- **Rules:** follow é reativado ao clicar no pill ou ao rolar manualmente até o fim; remount da aba reescreve o buffer e posiciona no fim.
- **Input → Output:** chunks `agent_output` do hub → linhas no xterm + badge `live` enquanto a execução está ativa.

### RF-002: Toolbar do terminal

- **Description:** Barra sobre o xterm com: filtro de stage (dropdown populado pelos `title`/`stageKey` distintos dos eventos `agent_output`), filtro de stream (`Todos / stdout / stderr`), botões Copiar, Baixar `.log` (`run-{id}.log`) e Limpar (reseta xterm + índice de dedup).
- **Rules:** filtros afetam apenas a renderização — o índice de dedup continua consumindo todos os eventos para que trocar o filtro não perca chunks; "Limpar" esvazia ambos.
- **Input → Output:** seleção de filtro → re-render do buffer filtrado.

### RF-003: Explorer de arquivos do worktree

- **Description:** Nova aba "Arquivos" (`WorktreeExplorer.razor`) com painel de árvore à esquerda e viewer read-only à direita. Diretórios carregam filhos sob demanda (lazy) via `GET /api/harness/worktrees/{runId}/files?path=`; clicar num arquivo busca `GET .../files/content?path=` e renderiza `<pre>` monospace.
- **Rules:**
  - `.git` nunca listado; entradas por diretório limitadas a 500 (com flag `truncated` no DTO).
  - Conteúdo de arquivo limitado a 512KB — acima disso retorna `truncated=true` com o prefixo; arquivos binários (NUL nos primeiros 8KB) retornam `binary=true` sem conteúdo.
  - Todo path é confinado ao worktree da sessão (`Path.GetFullPath` + prefix check) — `..`, absolutos e symlinks que escapam → `400`.
- **Input → Output:** clique em pasta → filhos; clique em arquivo → conteúdo no viewer com breadcrumb.

### RF-004: Diff agrupado por arquivo com collapse

- **Description:** `GitDiffViewer` passa a renderizar a lista `Diff.Files` — cada item é um card colapsável: header = `+`/`-` toggle, path, badge de status, contadores `+ins`/`-del` por arquivo (de `--numstat`, agora por path no `WorkspaceDiffFileDto`); corpo = o trecho do `Patch` daquele arquivo com realce por linha (mantém classes `diff-add/del/hunk/ctx/file`).
- **Rules:**
  - Seções de patch são casadas por path (`diff --git a/<path> b/<path>`); trechos sem arquivo correspondente agrupam num card "outros".
  - Arquivo sem patch (ex.: `Untracked`) mostra corpo informativo "arquivo novo não rastreado — sem diff".
  - Botões "Expandir todos"/"Recolher todos" no header do resumo; primeiro arquivo abre expandido por default.
  - Patch > 2MB mantém o comportamento atual (lista de arquivos, sem corpos).

---

## 5. API Contract

```http
GET /api/harness/worktrees/{runId}/files?path=src/
→ 200 { "path": "src/", "entries": [ { "name": "App", "path": "src/App", "directory": true },
                                    { "name": "Program.cs", "path": "src/Program.cs", "directory": false, "sizeBytes": 4210 } ],
        "truncated": false }
→ 400 path inválido/escapa do worktree · 404 run sem worktree

GET /api/harness/worktrees/{runId}/files/content?path=src/Program.cs
→ 200 { "path": "src/Program.cs", "content": "...", "sizeBytes": 4210, "truncated": false, "binary": false }
→ 400 path inválido · 404 run sem worktree ou arquivo inexistente · 413 (não usado — truncated cobre)
```

DTOs:

```csharp
public sealed record WorktreeEntryDto(string Name, string Path, bool Directory, long? SizeBytes);
public sealed record WorktreeListDto(string Path, IReadOnlyList<WorktreeEntryDto> Entries, bool Truncated);
public sealed record WorktreeFileContentDto(string Path, string? Content, long SizeBytes, bool Truncated, bool Binary);
public sealed record WorkspaceDiffFileDto(string Path, string Status, int Insertions, int Deletions);
```

`IWorkspaceIsolationService` +=

```csharp
Task<WorktreeListDto> ListFilesAsync(string runId, string? subdir, CancellationToken ct = default);
Task<WorktreeFileContentDto> ReadFileAsync(string runId, string path, CancellationToken ct = default);
```

---

## 6. Acceptance Criteria

- [x] **Given** um run em execução, **when** novos `agent_output` chegam e o usuário está no fim do terminal, **then** a visão auto-scrolla; quando o usuário sobe, o scroll trava e o pill "↓ novos logs" aparece até clicar/voltar ao fim. *(xterm `onScroll` → pill; badge "ao vivo" enquanto `IsRunLive`)*
- [x] **Given** eventos de múltiplos stages/streams, **when** o usuário escolhe um stage ou `stderr`, **then** só as linhas correspondentes renderizam; voltar a "Todos" restaura tudo sem perda. *(filtros reescrevem o buffer — dedup consome todos os eventos)*
- [x] **Given** um run com worktree, **when** abre a aba Arquivos, **then** a raiz lista diretórios/arquivos (sem `.git`), expandir uma pasta carrega filhos, e clicar num arquivo mostra o conteúdo read-only.
- [x] **Given** um path com `..` ou absoluto, **when** `files`/`files/content` é chamado, **then** responde `400` sem acessar o disco fora do worktree. *(+ symlinks que escapam)*
- [x] **Given** um diff com 3 arquivos, **when** abre a aba Diff, **then** lista os 3 cards colapsáveis com status e `+/-` por arquivo; expandir mostra apenas o patch daquele arquivo; "Expandir todos"/"Recolher todos" funcionam.

---

## 7. Testing Strategy

- `GitWorktreeManagerTests` (git real): `ListFilesAsync` lista filhos, omite `.git`, respeita cap; `ReadFileAsync` lê conteúdo, marca binário/truncado; ambos rejeitam `..`/absoluto com `DomainException`; `--numstat` por path preenche `Insertions`/`Deletions` por arquivo.
- `UnifiedDiffParser` (helper puro em Contracts): split do patch por `diff --git`, casamento por path, bucket "outros" para trechos sem match.
- Integração (`CockpitEndpointsTests` ou novo): `files`/`files/content` → `404` sem worktree; `400` traversal.
- Build: `dotnet build` (TreatWarningsAsErrors), `dotnet format --verify-no-changes`, suites completas.

---

## 8. Implementation Plan

1. **Backend**: `WorktreeEntryDto`/`WorktreeListDto`/`WorktreeFileContentDto` + `ListFilesAsync`/`ReadFileAsync` no `GitWorktreeManager` (guarda de path + caps) → endpoints no grupo `worktrees` → `numstat` por arquivo no `WorkspaceDiffFileDto`.
2. **Contratos puros**: `UnifiedDiffParser.SplitByFile(patch)` em Application.Contracts (testável sem bUnit).
3. **Client**: `TaskboardClient.GetWorktreeFilesAsync`/`GetWorktreeFileContentAsync`.
4. **Terminal**: `terminal.js` += `isAtBottom`/`scrollToBottom`/`getText`; `RunTerminal` += toolbar (stage/stream/copiar/baixar/limpar) + pill follow.
5. **Explorer**: `WorktreeExplorer.razor` (árvore lazy + viewer) + aba "Arquivos" no `CockpitRun`.
6. **Diff**: `GitDiffViewer` → cards por arquivo com collapse via `UnifiedDiffParser` + contadores.
7. Docs (`api.md`/`api.pt-br.md`) + SPEC status.

---

## 9. Traceability

- `SPEC-20260919-ade-cockpit-hitl` RF-001/RF-002 — estende timeline e diff viewer.
- `SPEC-20260920-board-cockpit-unified-runs` R6 — estende `RunTerminal` read-only.
- `SPEC-20260921-board-cockpit-agent-observability` — compartilha o stream `agent_output`; nenhuma mudança no pipeline de eventos.

## 10. Knowledge Sources

- `RunTerminal.razor`, `GitDiffViewer.razor`, `CockpitRun.razor`, `terminal.js` — estado atual do cockpit.
- `GitWorktreeManager.cs`, `WorktreePaths` — confinamento e paths de worktree.
- `CockpitEventStream` — buffer 500 + broadcast `ReceiveCockpitEvent`.
