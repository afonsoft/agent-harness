# SPEC-20260923-terminal-tabs-keyed-render

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `terminal-tabs-keyed-render` |
| Type | `Bugfix` |
| Stack | `.NET 10 / Blazor WASM / SignalR / xterm.js` |
| Repository | `afonsoft/agent-harness` |
| Branch | `fix/devin-20260923-terminal-tabs-keyed-render` |
| Ticket | [#336](https://github.com/afonsoft/agent-harness/issues/336) |
| Status | `Approved` |
| Related | `SPEC-20260917-terminal-tabs`, `SPEC-20260920-terminal-pty-resize`, `SPEC-20260921-terminal-paste-dedup` |

## 1. User Story

**As a** usuário do Harness com várias abas de terminal abertas em `/terminal`,
**I want** que fechar uma aba à esquerda (ou do meio) remova apenas aquela aba,
**So that** as demais continuam renderizando a sessão correta, na ordem correta, sem telas pretas nem abas travadas.

**Problem context (reprodução):**

1. Abrir `/terminal` e criar 3+ abas (ex.: `Terminal 1`, `Terminal 2`, `Terminal 3`), cada uma com um PTY ativo.
2. Fechar a aba mais à esquerda (`Terminal 1`).
3. **Observado:** as abas restantes ficam pretas/vazias, a ordem aparente muda ou o conteúdo aparece "invertido" (a aba `Terminal 2` mostra a sessão de outra aba), algumas abas param de responder ao input e outras não atualizam mais a tela.

**Root cause (evidência):**

- `Terminal.razor` renderiza a tabstrip (`<li class="nav-item">`, linha ~24) e os painéis (`<div class="terminal-pane">` → `<div id="@tab.ElementId" class="terminal-host">`, linhas ~52–74) com `@foreach (var tab in _tabs)` **sem `@key`**.
- O diff do Blazor sem `@key` casa elementos **por posição**: ao remover um item do meio, os nós DOM existentes são reutilizados em ordem e apenas seus atributos (`id`, `class`, `aria-selected`) são remendados; o **último** nó da região do loop é o que é de fato removido do documento.
- O estado do terminal não vive no DOM gerenciado pelo Blazor: `terminal.js` mantém `terms = Map<elementId, {term, fit, observer, el, …}>` e cada instância xterm cria sua própria sub-árvore DOM dentro do host — que o Blazor desconhece e preserva ao reutilizar o nó.
- Resultado após `CloseTabAsync(tab esquerda)` com abas `[A,B,C]`:
  - `taskboardTerminal.dispose("terminal-host-A")` dispõe corretamente o xterm A e limpa o host A.
  - No re-render posicional: pane[0] é remendado `A→B` (`id` vira `terminal-host-B`, mas o host está vazio — o xterm A foi disposto); pane[1] é remendado `B→C` (`id` vira `terminal-host-C`, mas **continua contendo o DOM do xterm B**); o nó do pane[2] — que contém o xterm C — é removido do documento.
  - `terms["terminal-host-C"]` ainda aponta para o xterm C (agora desanexado): `output(sessionId_C)` → `write` → renderiza em lugar nenhum → **aba C preta/travada**.
  - `terms["terminal-host-B"]` → xterm B renderiza dentro do nó agora rotulado `host-C` → **conteúdo da sessão B aparece sob a aba C** (sensação de ordem invertida/trocada).
  - Os `ResizeObserver`s de B e C seguem observando os elementos errados → `OnTerminalResize` envia `cols/rows` para a sessão errada (`tabKey` lookup por `Key` permanece correto, mas o host medido pertence a outra aba).
- Fechamentos subsequentes repetem o remendo sobre um DOM já inconsistente, agravando a corrupção (mais abas pretas/travadas).
- O servidor (`TerminalHub`/`TerminalSessionManager`) não tem participação: `Close(sessionId)` e o roteamento por `sessionId` estão corretos; o bug é exclusivamente de reconciliação de DOM no cliente.

## 2. Scope

**In scope:**

- Adicionar `@key` estável aos `@foreach` de `_tabs` em `Terminal.razor`: `@key="tab"` no `<li class="nav-item">` da tabstrip e no `<div class="terminal-pane">` (o nó que ancora o host xterm).
- Hardening de `terminal.js`: `init`/`initReadOnly` devem dispor uma entrada existente para o mesmo `elementId` antes de recriar (idempotência contra double-init em qualquer re-render futuro).
- Teste de regressão de renderização (bUnit) provando que, ao fechar a aba do meio, os nós DOM dos painéis sobreviventes são **as mesmas instâncias** e mantêm `id`/`elementId` originais.
- Smoke manual documentado (3+ abas, fechar esquerda/meio/direita, alternar, reopen).

**Out of scope:**

- Reordenação de abas por drag (permanece fora de escopo, como em `SPEC-20260917-terminal-tabs`).
- Refactor do transporte SignalR, `TerminalSessionManager`, `PtySession` — sem mudança server-side.
- Persistência/reattach de scrollback.
- Reuso de `elementId` entre abas (cada aba continua com `Key`/`ElementId` únicos por `Guid`).

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Blazor/Components/Pages/Terminal.razor` — dois `@foreach` sobre `_tabs` precisam de `@key="tab"`.
- `src/Taskboard.Client/wwwroot/js/terminal.js` — guarda idempotente em `init`/`initReadOnly`.
- `tests/Taskboard.Tests.Unit/` — novo teste bUnit do componente `Terminal` (ou projeto de testes de componentes, se o harness preferir separar).

**Files to read before implementing:**

- `src/Taskboard.Blazor/Components/Pages/Terminal.razor` (loops em ~L21–40 e ~L51–76; `CloseTabAsync` ~L291–322)
- `src/Taskboard.Client/wwwroot/js/terminal.js` (`init` L65, `initReadOnly` L104, `dispose` L249, `terms` Map L5)
- `src/Taskboard.Server/Hubs/TerminalHub.cs` (confirma que o server é agnóstico — apenas contexto)
- `.specs/SPEC-20260917-terminal-tabs.md` (RF-003 — UI de abas original)
- `tests/Taskboard.Tests.Unit/` (convenções de teste existentes)
- Referência do framework: Blazor `@key` — diffing de listas por identidade vs. posição.

**Files to create or modify:**

```text
src/Taskboard.Blazor/Components/Pages/Terminal.razor        # MOD — @key nos dois foreach
src/Taskboard.Client/wwwroot/js/terminal.js               # MOD — dispose-on-reinit guard
tests/Taskboard.Tests.Unit/Components/TerminalTabsTests.cs # NEW — bUnit node-identity regression
tests/Taskboard.Tests.Unit/Taskboard.Tests.Unit.csproj    # MOD — pacote bunit (justificar no PR)
.specs/SPEC-20260923-terminal-tabs-keyed-render.md        # NEW
```

## 4. Requirements

### RF-001: `@key` na tabstrip

- **Description:** O `<li class="nav-item">` renderizado por `@foreach (var tab in _tabs)` na tabstrip deve declarar `@key="tab"` para que o diff remova exatamente o item fechado.
- **Input → Output:** `CloseTabAsync(tab[i])` → DOM remove somente o `<li>` daquela aba; os demais `<li>` mantêm nó, ordem e handlers.

### RF-002: `@key` no painel xterm

- **Description:** O `<div class="terminal-pane">` renderizado por `@foreach (var tab in _tabs)` deve declarar `@key="tab"` de modo que o `.terminal-host` (e a sub-árvore interna criada pelo xterm via `term.open(el)`) seja preservado byte-a-byte para as abas sobreviventes e removido somente para a aba fechada.
- **Input → Output:** fechar `tab[i]` → `document.getElementById(tab[j].ElementId)` continua sendo o mesmo `IElement`/nó que hospeda o xterm da aba `j` para todo `j ≠ i`.

### RF-003: `init` idempotente no JS

- **Description:** `taskboardTerminal.init` e `initReadOnly` devem, antes de criar uma nova instância, executar `dispose(elementId)` quando `terms.has(elementId)` — evitando duas instâncias xterm/observers sobre o mesmo host em qualquer re-inicialização futura.
- **Input → Output:** `init(id)` chamado 2× → exatamente 1 entrada no `Map`, 1 `ResizeObserver` ativo, host contendo 1 único `.xterm`.

### RF-004: Seleção da aba ativa ao fechar (preservar comportamento)

- **Description:** Manter a heurística atual: ao fechar a aba ativa, ativa `min(index, count-1)` pós-remoção (a aba que a seguia, ou a última); ao fechar aba inativa, `_activeTab` não muda. Após a troca, `fitNow` + `focus` na aba ativa.
- **Input → Output:** fechar aba ativa → vizinha ativada, refit + focus; fechar inativa → apenas remoção.

### RF-005: Teste de regressão (bUnit + source guard)

- **Description:** Cobertura em duas camadas, em pt-BR no padrão do repo:
  1. **Funcional (bUnit):** `TerminalTabsTests` renderiza `Terminal` com `JSRuntimeMode.Loose` + `SelectedRepositoryService` stub + `AddBlazorBootstrap`; abre 3 abas via `+` e valida: fechar aba do meio/esquerda preserva `elementId`s e títulos das sobreviventes na ordem; fechar a ativa promove a seguinte; fechar a última exibe o empty state.
  2. **Source guard (`TerminalRazorSourceGuardTests`):** asserta que `Terminal.razor` declara `@key="tab"` no `<li class="nav-item">` da tabstrip e no `<div class="terminal-pane">`, e que `init`/`initReadOnly` do `terminal.js` chamam `dispose(elementId)` antes de recriar a instância. É o tripwire real da regressão — **descoberta durante a execução: o bUnit recria os nós DOM ao aplicar diffs estruturais, logo identidade de nó não é observável em teste de componente**; a manifestação do bug depende da sub-árvore criada pelo xterm.js, que não existe no DOM do bUnit.
- **Rules:** novo pacote `bunit` 2.11.3 (publicado 2026-09-13, ≥7 dias) justificado na descrição do PR — única forma automatizada de exercitar o componente; o repo não possuía infraestrutura de teste de componentes.
- **Input → Output:** `dotnet test` → verde com `@key`; removendo `@key`, os guards de source falham (RED verificado).

### RF-006: Sem regressão funcional

- **Description:** `?cmd=`, `Reopen`, reconnect (`Reattached` → `fitNow`/`focus`), `Ctrl+C/Ctrl+V` e limite de 8 abas permanecem inalterados.
- **Input → Output:** smoke manual + suite existente verde.

**Business rules / invariants:**

- Identidade DOM estável = identidade de aba: `Key`/`ElementId` nunca são reatribuídos a outra aba durante a vida do componente.
- O `terms` Map JS e a lista `_tabs` devem permanecer 1:1 por `elementId` enquanto a aba existir.
- Nenhuma mudança de contrato no hub (`output`/`closed`/`Input`/`Resize`/`Close`/`Reattach`).

## 5. API Contract

N/A — sem mudança de API/hub. O bug e o fix são confinados à reconciliação de DOM do cliente Blazor e ao wrapper JS.

## 6. Acceptance Criteria

- [ ] **Dado** 3 abas abertas com sessões distintas (ex.: `echo A`, `echo B`, `echo C`) **quando** fecho a aba da esquerda **então** as abas restantes exibem cada uma o conteúdo da própria sessão, na ordem original, sem tela preta.
- [ ] **Dado** 4 abas abertas **quando** fecho a aba do meio **então** somente ela é removida; as demais respondem a input e recebem output da sessão correta.
- [ ] **Dado** 3 abas **quando** fecho a aba ativa do meio **então** a aba seguinte vira ativa, recebe `fitNow`+`focus`, e redimensiona corretamente.
- [ ] **Dado** abas `[1..N]` **quando** fecho todas da esquerda para a direita **então** cada fechamento remove só aquela aba e o estado vazio aparece ao final.
- [ ] **Dado** o teste bUnit da RF-005 **quando** executado contra o código sem `@key` **então** falha (prova de regressão); com `@key` passa.
- [ ] **Dado** `init` chamado duas vezes para o mesmo `elementId` **quando** a segunda chamada ocorre **então** a entrada anterior é disposta e só uma instância permanece.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Fechar a última aba | `CloseTabAsync` em `_tabs.Count == 1` | Lista vazia → empty state "No terminal sessions" |
| Fechar aba em estado `Closed`/`Error` | sessão já morta | Remove aba; `Close` no hub é no-op protegido por try/catch |
| Fechar enquanto hub reconectando | `_hub.State != Connected` | Não invoca `Close`; dispose JS + remoção ocorrem normalmente |
| Resize durante remendo | `ResizeObserver` dispara no meio do patch | Com `@key`, observer sempre mede o elemento da própria aba |
| Double-init (HMR/reattach futuro) | `init` com `elementId` já presente | Entrada antiga disposta (RF-003) |

## 7. Task Plan (agent execution)

- [ ] **T1 — Fix:** `@key="tab"` nos dois `@foreach` de `Terminal.razor`.
- [ ] **T2 — JS guard:** `dispose` idempotente no início de `init`/`initReadOnly` em `terminal.js`.
- [ ] **T3 — Test:** adicionar `bunit` ao `Taskboard.Tests.Unit` + `TerminalTabsTests.cs` (RF-005); confirmar RED sem `@key`, GREEN com `@key`.
- [ ] **T4 — Validation:** `dotnet build -c Release` (TreatWarningsAsErrors) + `dotnet test`; smoke manual descrito na seção 6.
- [ ] **T5 — Done + PR:** `Status = Done`, PR da branch `fix/devin-20260923-terminal-tabs-keyed-render`.

**7.1 Validation:** bUnit para RF-001/002/005; unit/JS manual para RF-003; smoke manual para RF-004/006.

## 8. Organization Guardrails

- Branch `fix/devin-20260923-terminal-tabs-keyed-render`; nunca commit em `main`/`develop`.
- Sem alteração em `.github/workflows/**`.
- Não logar conteúdo de terminal (pode conter tokens).
- Novo pacote NuGet (`bunit`) requer justificativa na descrição do PR.
- Nenhuma mudança server-side; escopo estrito ao diff de DOM + guarda JS + teste.

## 9. Definition of Done

- [ ] RF-001…RF-006 implementados.
- [ ] Critérios da seção 6 cobertos por teste/evidência (bUnit verde; smoke manual registrado).
- [ ] Edge cases tratados.
- [ ] `dotnet build` limpo e `dotnet test` verde; cobertura não regredir abaixo do gate (77%).
- [ ] Guardrails da seção 8 respeitados.

## Open Questions / Pending Ambiguity

- bUnit vs. alternativa de teste (ex.: extrair o bloco de abas para um componente filho testável) — recomendação: bUnit direto no `Terminal`, sem refactor estrutural neste bugfix.
- Se o PR não puder adicionar NuGet, o fallback é smoke manual + nota `ponytail:` para introduzir bUnit depois — indicação do usuário necessária.
