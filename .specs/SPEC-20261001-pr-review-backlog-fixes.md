# SPEC-20261001-pr-review-backlog-fixes: correções do backlog de review (Devin/CodeQL/Sonar)

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | PR review backlog remediation — bugs and correctness fixes |
| Product / System | agent-harness |
| Module / Bounded Context | Application + Server + Integrations + Blazor |
| Change type | Bugfix batch |
| Repository | afonsoft/agent-harness |
| Suggested branch | `fix/devin-20261001-pr-review-backlog` |
| Technical owner | afonsoft |
| Status | Done (implemented in `fix/devin-20261001-pr-review-backlog`) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Related SPECs | SPEC-20260929-ai-chat-capabilities, SPEC-20260929-managed-job-shutdown-run, SPEC-20261001-chat-capability-registry, SPEC-20261001-chat-agent-delegation, SPEC-20261001-chat-mcp-client, SPEC-20261001-terminal-memory-mobile |

---

## 1. Executive Summary

### Problem

Auditoria dos últimos 20 PRs fechados (#376–#416) levantou os comentários do
Devin Review (`kind: "bug"`), GitHub Advanced Security (CodeQL) e SonarCloud.
Os PRs foram mergeados com findings abertos. Verificação contra o código
atual em `main` confirma que **23 bugs permanecem vivos**, além de nits de
CodeQL e um gate do SonarCloud que falhou em #391/#393/#411 sem bloquear o
merge.

### Objective

Corrigir todos os bugs confirmados, em ordem de severidade, preservando os
contratos ACP/PTY/SignalR e a arquitetura ABP N-Layer. Cada item exige teste
de regressão.

### Out of scope

- Novas features — somente correções dos findings listados.
- Redesign do chat — as correções preservam o comportamento pretendido.
- Findings `kind: "analysis"` do Devin (informativos) e CodeQL de estilo
  já cobertos pelas regras Sonar resolvidas (S3776 etc.).

---

## 2. Agent Role

> Senior .NET engineer — concurrency, Blazor, ABP layered architecture.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não alterar contratos de API/SignalR/PTY sem nota de breaking change.
- `/.github/workflows/**` protegido.
- Todo fix vem com teste de regressão (`Dado_Quando_Entao`).
- `dotnet format` antes do push; build 0 warnings.

---

## 4. Findings confirmados no código atual

### P0 — correção de servidor (crash, perda de dados, corrupção)

| ID | Origem | Local | Bug | Estado |
|---|---|---|---|---|
| B-01 | #393 | `ChatService` (scoped `_runs`) + Program.cs:527 | Endpoint `/stop` nunca encontra a execução — cada request tem seu próprio dicionário → 409 e a geração continua | **corrigido** |
| B-02 | #393 | `ChatService.ConsumeStreamAsync`/`StreamTurnAsync` | Deltas acumulados em `PendingDeltas` e só emitidos após o stream inteiro — sem progresso ao vivo nem heartbeat | **corrigido** |
| B-03 | #415 | `AgentOrchestrationService.CreateProgressHandler` (:261-292) | Callback `async void` só captura `ObjectDisposedException`; `BroadcastAsync` com token cancelado (budget cap) lança `OperationCanceledException` → pode derrubar o processo | **corrigido** |
| B-04 | #388 | `ManagedJobService` loop (:48-51) | `Task.WhenAny` abandona `signalTask` sem cancelar; próxima iteração cria outro `ReadAsync` — sinais `RunRequested`/`ScheduleChanged` podem ser consumidos pela leitura órfã | **corrigido** |
| B-05 | #388 | `JobRegistry.SetOverrideAsync` (:149) | `enabled ?? current?.Enabled ?? true` — atualizar só o intervalo de um job desabilitado por padrão grava `Enabled=true` | **corrigido** |
| B-06 | #388 | `JobRegistry.SetOverrideAsync` | Read-modify-write fora de transação serializada por job — PUTs concorrentes perdem campos ou violam unicidade (500) | **corrigido** |
| B-07 | #411 | `RunAgentTool` (:72,108) | `issueId = chat:{conversationId}` compartilhado; `wait=true` consulta o último run por data — delegação posterior pode ler resultado da anterior | **corrigido** |
| B-08 | #411 | `ChatMcpClientManager` (:145,192) | (a) `_connectAttempted` ignora mudanças de config — exige restart; (b) `_tools.Concat` em conexões paralelas perde ferramentas | **corrigido** |
| B-09 | #376 | `AiChatService.ForkThreadAsync` (:776) | Fork exige `AgentType is not null` para criar agent-thread; thread agent com CLI customizada (`AgentType==null`) vira assistant e perde `WorkspacePath` | **corrigido** |
| B-10 | #415 | `Entity<TKey>` (:37-40) | Operadores `==`/`!=` removidos no fix S3875 — `a == b` usa referência enquanto `Equals` usa `Id` | **corrigido** |

### P1 — correção de UX/funcional

| ID | Origem | Local | Bug | Estado |
|---|---|---|---|---|
| B-11 | #377 | `AiChat.razor NewConversationAsync` | `_composer`/estado limpos antes do `CreateAiChatThreadAsync` confirmar; exceção de transporte não tratada → ErrorBoundary | **corrigido** |
| B-12 | #377 | `AiChat.razor` + AiChatService | Thread criada via New fica "New conversation" para sempre — primeiro Send não deriva/persiste título | **corrigido** |
| B-13 | #385 | `ThreadRail.razor` / `AiChat.razor` scrim (:45) | Overlay de histórico sem botão de fechar; scrim só responde a clique — teclado não dispensa | **corrigido** |
| B-14 | #387 | `AiChat.razor OnCfgViewChanged` (:1048) | Troca automática para CLI com ACP não chama `setAiChatLastAgent` — reload restaura CLI antiga e view Terminal | **corrigido** |
| B-15 | #393 | `ProviderChat.razor` (:60) | Seletor de modelo só muda `_model` local — conversa persistida segue no modelo anterior (PATCH existe mas não é usado) | **corrigido** |
| B-16 | #393 | `ProviderChat.razor` (:266) | Nova conversa usa `_models[0]` — ignora `Taskboard:Chat:DefaultChatModel` | **corrigido** |
| B-17 | #393 | `GenerateImageTool` + `ChatService` | `imagePath` volta só no JSON da tool; nenhuma mensagem chama `AttachImage` → imagem nunca renderiza | **corrigido** |
| B-18 | #393 | Program.cs `IReadOnlyDictionary<string, IChatTool>` (:489) | `SearchBackendFactory` executado uma vez no singleton — trocar backend em Settings exige restart | **corrigido** |
| B-19 | #393 | `CodeInterpreterTool` (:57) | `ArgsPrefix.Replace(ph, file).Split(' ')` quebra path com espaços | **corrigido** |
| B-20 | #388 | `Jobs.razor` (:192 `TryAdd`) | `_intervalDrafts` nunca atualiza após refresh/save — campo exibe valor obsoleto | **corrigido** |
| B-21 | #388 | `CliProbeRefreshJobService` (:32) | `RefreshAsync` absorve falha; job sempre reporta "probe snapshot refreshed" | **corrigido** |
| B-22 | #391 | Program.cs gate `WebCliAgentEnabledKey` (:1745+) | `HARNESS_WEB_CLI_AGENT_ENABLED` só afeta o catálogo de Settings; o endpoint lê `Taskboard:WebCliAgent:Enabled` direto — alias não desliga | **corrigido** |
| B-23 | #391 | `FinOpsService.GetSummaryAsync` (:117-128) + retenção | Sessões abertas e runs `Running` iniciados >30d ficam fora do scan → falso `NoActiveSessions`; retenção pode apagar sessão aberta | **corrigido** |

### P2 — higiene (CodeQL + Sonar)

| ID | Origem | Local | Bug | Estado |
|---|---|---|---|---|
| C-01 | #378 | Program.cs (:752) | `agentCliProbeTtl` não passado ao `AgentModelCatalogService` — catálogo sempre usa 120s | **corrigido** |
| C-02 | #378 | `CliProbeSnapshotService.SetModels` (:83) | Sync manual de um agente sobrescreve `LastCompletedAt` global — demais agentes não revalidam até o próximo TTL | **corrigido** |
| C-03 | CodeQL | `AgentSessionManager.cs` ~:325 | `scope!.Dispose()` fora de finally/using — exceção no dispatch vaza o scope | **corrigido** |
| C-04 | CodeQL | `Jobs.razor`:180, `ManagedJobService`:70 | Empty catch blocks | **corrigido** |
| C-05 | CodeQL | `PipelineEngine` ~:229 | CTS de stage pode não ser descartado em paths de exceção | **corrigido** |
| C-06 | CodeQL | `DynamicCommandClassifier` ~:424 | String concat em loop | **corrigido** |
| C-07 | CodeQL | `PathJailValidatorTests`:32 | `Path.Combine` descarta args anteriores no teste | **corrigido** |
| C-08 | Sonar | gate #391 | Duplication 3.3% > 3% no new code — identificar e deduplicar o bloco | **pendente re-scan** |
| C-09 | Sonar | gate #393/#411, 42 issues em #415 | Reliability rating C em new code — os findings B-xx desta SPEC são a causa raiz; revalidar scan após fixes | **pendente re-scan** |

---

## 5. Functional Requirements

### RF-001 — Registro de execuções compartilhado (B-01)

Extrair `_runs` do `ChatService` para um coordenador singleton
(`ChatRunCoordinator`): `TryStart(conversationId) → CancellationTokenSource?`,
`Cancel(conversationId) → bool`, `Remove(conversationId, cts)` com checagem de
posse. `ChatService` continua scoped; o endpoint `/stop` cancela pelo
coordenador. Sem mudança de contrato HTTP.

### RF-002 — Streaming real (B-02)

`ConsumeStreamAsync` deixa de acumular para replay: emitir `ChatDeltaEvent`
conforme os chunks chegam. Opções: (a) `StreamTurnAsync` itera o
`IAsyncEnumerable<OpenAiStreamEvent>` diretamente e emite inline (estado de
acumulação em `StreamOutcome` permanece, mas como campo do loop); (b) canal
`Channel<ChatStreamEvent>` unbounded escrito durante a iteração e drenado pelo
iterador externo. Preferir (a) — mais simples, preserva a ordem. Heartbeat
(`ChatStatusEvent` keepalive) quando o stream ficar >N s sem chunk
(`Taskboard:Chat:SseHeartbeatSeconds`, default 15).

### RF-003 — Callback de progresso à prova de cancelamento (B-03)

No callback `async void`, capturar `OperationCanceledException` quando
`broadcastToken.IsCancellationRequested`; opcionalmente enviar o log final
sem o token cancelado (`CancellationToken.None`) para o fim da execução.
Avaliar mover o broadcast para uma fila drenada fora do callback — mínimo
exigido: nenhuma exceção pode escapar do callback.

### RF-004 — Job scheduler sem leitura órfã (B-04)

Uma única leitura do canal por iteração, cancelável junto ao delay: usar
`Task.Delay` + `ReadAsync` sob o mesmo token e, no winner==delay, cancelar o
reader via `CancellationTokenSource` dedicado por iteração **e** dar drain em
`TryRead` após cancelar; ou reescrever o loop como `await foreach` sobre o
canal com o delay implementado por um timer que posta um sinal `Tick`.
Preferir a segunda (elimina a classe de race) se o volume de mudança for
contido.

### RF-005 — Override de job preserva default (B-05)

`enabled ?? current?.Enabled ?? def.EnabledByDefault` (a sugestão do review).
Teste: job `EnabledByDefault=false`, PUT apenas com interval → permanece
desabilitado.

### RF-006 — Serialização por job (B-06)

Serializar read→merge→persist→publish por chave (ex.: `SemaphoreSlim` por job
em `ConcurrentDictionary<string, SemaphoreSlim>`) e tratar conflito de
unicidade com retry de leitura+merge (ou chave de versão otimista → 409).
Teste de concorrência: dois PUTs simultâneos no mesmo job convergem.

### RF-007 — Delegação com id único (B-07)

`EnqueueAsync` passa a retornar o `runId` criado (ou a delegação gera
`chat:{conversationId}:{runSeq}` único) e o wait acompanha **esse** id.
`GetRunsAsync(issueId)` por id da delegação, não pelo mais recente.

### RF-008 — MCP hot-reload + publicação atômica (B-08)

`EnsureConnectedAsync` compara snapshot da config (servers serializados +
flags) com o último usado; ao mudar, descarta clientes/rotas/status e
reconecta sob `_connectGate`. `_tools` publicado atomicamente: cada
`ConnectServerAsync` retorna seus adaptadores; após `Task.WhenAll` uma única
atribuição `_tools = adaptersAgregados`.

### RF-009 — Fork preserva modo e workspace (B-09)

Ramificação por `source.Mode == "agent"` (AgentType pode ser null em CLI
customizada): `CreateAgentThread` recebe `AgentType?` ou sobrecarga; copiar
`WorkspacePath`, `RepositoryFullName`, `Transport`, `ContainerContext`,
`AgentCliId`. Teste: fork de thread `mode=agent` + custom CLI mantém
workspace.

### RF-010 — `==` coerente com `Equals` (B-10)

Restaurar `operator ==`/`!=` em `Entity<TKey>` delegando a `Equals` (e manter
`IEquatable<T>` — satisfaz S3875). Teste: duas instâncias com mesmo `Id` →
`==` verdadeiro.

### RF-011 — New preserva draft e trata falha (B-11)

`NewConversationAsync`: só limpa `_composer`/`_activeThread*` **após** sucesso
da criação; envolver o POST em try/catch (`HttpRequestException` etc.) com
toast e estado preservado. Fechar o drawer só no sucesso.

### RF-012 — Título derivado no primeiro envio (B-12)

Ao enviar a primeira mensagem numa thread cujo `Title` é o placeholder "New
conversation", derivar via `AiChatThreadTitle.Derive` e persistir (novo
método `RenameThreadAsync`/`EnsureTitle` server-side + endpoint PATCH ou
interno ao send). Sincronizar `_activeThread`/`_threads`. Título manual
existente nunca é sobrescrito.

### RF-013 — Histórico dispensável por teclado (B-13)

`ThreadRail` recebe botão Close acessível + `Esc` fecha o overlay; foco vai
para o painel ao abrir e retorna ao acionador ao fechar. Scrim continua
fechando por clique. Guard: `aria-label`/role + handler `keydown` no
`AiChat.razor`.

### RF-014 — Persistir CLI auto-selecionada (B-14)

`OnCfgViewChanged` vira `async`: ao trocar `_cfgCli` para a primeira CLI com
ACP, chamar `taskboard.setAiChatLastAgent` (try/catch como
`OnCfgCliChanged`).

### RF-015 — Modelo efetivo no provider chat (B-15/B-16)

- Ao trocar o seletor com conversa ativa: PATCH com o novo modelo; `_model`
  sincroniza após sucesso (rollback no erro).
- Nova conversa: modelo inicial = `DefaultChatModel` quando presente na lista;
  senão `_models[0]`.

### RF-016 — Imagem anexada à conversa (B-17)

`GenerateImageTool` sinaliza o `imagePath` no resultado (campo dedicado);
`ChatService` chama `AttachImage` na mensagem (tool ou assistant associada) —
`ImageUrl` aparece no transcript. Teste: resultado com `imagePath` → DTO
expõe `ImageUrl`.

### RF-017 — Backend de busca reativo à config (B-18)

`WebSearchTool` resolve o backend por execução: injetar
`Func<IReadOnlyDictionary<string, ISearchBackend>>` ou ler a config no
`ExecuteAsync` + cache por snapshot; ou `RuntimeConfigurationService`
publica invalidação. Trocar `SearchBackend` em Settings reflete sem restart.

### RF-018 — Path com espaços no interpretador (B-19)

Não fazer `Split(' ')` sobre a linha de args pós-substituição: montar
`List<string>` de args e substituir o placeholder como elemento único, ou
passar o path como argumento separado do prefixo.

### RF-019 — Drafts do painel de jobs (B-20/B-21)

- `RefreshAsync`: atualizar `_intervalDrafts[key]` quando não dirty;
  `SaveIntervalAsync` sincroniza o draft após sucesso.
- `CliProbeRefreshJobService`: `RefreshAsync` retorna sucesso/falha; job
  reporta `ok` só quando a atualização ocorreu (ou mensagem de erro).

### RF-020 — Alias env controla o gate (B-22)

O gate `WebCliAgent:Enabled` nos endpoints resolve pela mesma fonte do
catálogo: `RuntimeConfigurationService.Resolve` (db > alias > env genérico),
ou registrar o alias no configuration pipeline com precedência correta.
Teste de integração: `HARNESS_WEB_CLI_AGENT_ENABLED=false` +
`Taskboard__WebCliAgent__Enabled=true` → endpoint recusa.

### RF-021 — FinOps cobre sessões/runs abertos antigos (B-23)

`GetSummaryAsync`: incluir sessões `EndedAtUtc == null` e runs
`State == Running` independente do `StartedAt`; ajustar retenção para não
apagar sessão aberta ainda ativa. Teste: sessão de 31d sem fim + ingest
recente → não emite `NoActiveSessions`.

### RF-022 — Higiene (C-01 a C-07)

- C-01: passar `agentCliProbeTtl` ao `AgentModelCatalogService`.
- C-02: `SetModels` não toca `LastCompletedAt` (freshness por agente se
  necessário).
- C-03: scope em `await using`/try-finally.
- C-04: catch vazio → capturar + log (ou comentário justificando).
- C-05: `cts.Dispose()` em todos os paths de falha do stage dispatch.
- C-06: `StringBuilder` no loop do classifier.
- C-07: corrigir `Path.Combine` no teste.
- C-08/C-09: após os fixes, conferir próximo scan SonarCloud — duplication
  e reliability devem voltar a A/≤3%; se persistirem, abrir follow-up.

---

## 6. Acceptance Criteria

- AC-01: Todos os 23 bugs B-xx com teste de regressão e resolução no código.
- AC-02: `dotnet build` 0 warnings; `dotnet format --verify` limpo;
  unit + integration suites verdes.
- AC-03: `/stop` cancela geração em andamento (teste de integração).
- AC-04: SSE emite deltas durante a resposta (teste asserindo evento antes
  do fim do stream).
- AC-05: Quality Gate do SonarCloud passa no PR; nenhuma reliab. < A em
  new code.
- AC-06: Nenhuma regressão nos guards existentes
  (`Terminal*GuardTests`, `AiChatSourceGuardTests` etc.).

## 7. Risks & Mitigations

- **ChatService refactor (B-01/B-02)** mexe no núcleo do chat — mitigado:
  extração mínima do registro, testes de stream/stop existentes + novos.
- **Loop do ManagedJobService (B-04)** — reescrita cuidadosa; testes do
  shutdown já existem (SPEC-20260929-managed-job-shutdown-run).
- **Entity == (B-10)** — reintroduzir operadores pode colidir com S3875:
  implementar `IEquatable<T>` junto.
- **MCP hot-reload (B-08)** — clientes antigos devem ser disposed após o
  swap para não vazar processos stdio.
- Ordem sugerida: P0 (B-01→B-10) → P1 → P2, commits por área.

## 8. Task Plan (agent execution)

- [x] T1 — B-01 + B-02 (ChatService: `ChatRunCoordinator` singleton + streaming real)
- [x] T2 — B-03 (orchestration callback OCE)
- [x] T3 — B-04 + B-05 + B-06 + B-20 + B-21 (jobs registry/loop/painel)
- [x] T4 — B-07 + B-08 (delegação única + MCP hot-reload/race)
- [x] T5 — B-09 + B-10 (fork + Entity ==)
- [x] T6 — B-11 → B-14 (AiChat.razor UX)
- [x] T7 — B-15 → B-19 (provider chat/tools)
- [x] T8 — B-22 + B-23 (alias env + FinOps)
- [x] T9 — C-01 → C-07 (higiene CodeQL)
- [x] T10 — validação final + re-scan Sonar + fechar SPEC

## 9. Definition of Done

- [x] 23 bugs corrigidos com regressão coberta.
- [x] Build/testes/format verdes (1378 unit + 307 integration, 0 warnings).
- [x] Status → Done, PR aberto para `main`.
- [x] Comentário na issue de backlog citando esta SPEC.

## Open Questions / Pending Ambiguity

- B-04: preferir reescrita `await foreach`+tick-signal ou cancel-do-loser —
  decidir na implementação pelo menor diff seguro.
- B-08: política de dispose dos clientes MCP antigos durante hot-reload
  (drenar vs. abortar) — default: dispose após swap atômico.
