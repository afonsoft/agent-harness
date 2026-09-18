# SPEC-20260918-ai-chat-threads

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-chat-threads` |
| Type | `Feature` (UI completa sobre backend existente) |
| Stack | `.NET 10 / Blazor / SSE` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260918-ai-chat-threads` |
| Ticket | — |
| Status | `Draft` |

## 1. User Story

**As a** usuário do Harness
**I want** uma interface de chat completa — criar threads, conversar com o assistente e acionar um agent CLI como sub-tarefa contra um repositório
**So that** eu tire dúvidas sobre os repos, peça análises e dispare trabalho real sem sair da tela.

**Problem context:**
A tela `/ai-chat` hoje é **somente leitura** — lista threads sem criar, abrir ou conversar. O backend já existe quase completo: `AiChatService` (threads CRUD, eventos, runs via `ILLMProvider` com streaming), endpoints `GET/POST /api/local/ai/threads`, SSE `GET .../events`, `POST .../runs`. Falta: UI de chat (sidebar de threads + área de mensagens + composer), botão "Nova thread", e o caminho **agent CLI como sub-tarefa** — botão que abre picker (repo + CLI + tier) e enfileira `POST /api/agents/executions` com o contexto da thread como prompt, registrando o andamento como eventos na thread.

## 2. Scope

**In scope:**
- Layout de chat em 2 colunas: sidebar de threads (título, modelo, status, data; botão **+ Nova thread**) e área principal (mensagens por role, composer, estado de execução).
- **Nova thread**: modal com título + modelo (do `GET /api/local/ai/catalog`) + sandbox; cria via `POST /api/local/ai/threads` e abre.
- Composer: `POST .../events` (role=user) seguido de `POST .../runs`; resposta do assistente chega por SSE `GET .../events` (stream já implementado) — render incremental.
- **Run agent (sub-tarefa)**: botão na thread abre picker (repo configurado + CLI elegível via `GET /api/agents` + tier Lite/Normal/Ultra) → monta prompt com o contexto da thread (últimas N mensagens, cap ~8k chars) → `POST /api/agents/executions` → o run vira evento `system` na thread ("Antigravity queued para owner/repo — run #id") e o status final é postado quando terminar (polling de `GET /api/agents/runs`).
- `DELETE /api/local/ai/threads/{id}` + botão excluir na sidebar (a API não tem delete hoje).
- Mensagens: markdown renderizado (`MarkdownRenderer` existente), auto-scroll, indicador "digitando…" durante o run, retry do evento de SSE.

**Out of scope:**
- Streaming token-a-token com diffs (o SSE já emite chunks — renderizar como chegam basta).
- Tool calls/function calling dentro do chat — o agent CLI é a via de ação.
- Threads multi-usuário, compartilhamento, export.
- Migração do `OriginProjectId` (entidade `Project` sai em SPEC-20260918-projects-removal — o campo vira opcional/legado sem uso na UI).

## 3. Technical Context

**Files to read:**
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` (stub atual)
- `src/Taskboard.Application/AiChat/AiChatService.cs` (CreateThread/AddEvent/StartRun/ExecuteRun)
- `src/Taskboard.Server/Program.cs` — linhas ~517-625 (endpoints ai + SSE)
- `src/Taskboard.Application.Contracts/Dtos/AiChat*Dto.cs`, `Requests/*AiChat*`
- `src/Taskboard.Blazor/Services/TaskboardClient.cs` (métodos `GetAiChatThreadsAsync` etc.)
- `AgentConfigTab` + `POST /api/agents/executions` (orquestração — reuso para sub-tarefa)
- `MarkdownRenderer`, padrão `AgentRunState` polling

**Files to create/modify:**
- `AiChat.razor` — rewrite completo (sidebar + thread view + composer + pickers).
- `TaskboardClient` — `CreateThreadAsync`, `GetThreadEventsAsync`, `PostEventAsync`, `StartRunAsync`, `DeleteThreadAsync`.
- `Program.cs` — `DELETE /api/local/ai/threads/{id}`; `AiChatService.DeleteThreadAsync`.
- `ExecuteRunAsync` — hoje injeta `"Continue the conversation."` como user msg fixa: remover esse hack (a última mensagem do usuário já está nos eventos).
- Testes: integration (create/delete thread, post event, run), unit (prompt-builder da sub-tarefa).

## 4. Functional Requirements

- **RF-001** Sidebar lista threads (`GET .../threads`) com título/modelo/status/updatedAt; **+ Nova thread** abre modal (título, modelo do catálogo, sandbox) → `POST .../threads` → navega para a thread.
- **RF-002** Ao abrir uma thread, eventos existentes carregam (REST snapshot — o SSE endpoint já devolve backlog + live); composer envia `POST .../events` (user) + `POST .../runs`; resposta assistant renderiza incrementalmente via SSE; thread volta a `Idle` ao fim.
- **RF-003** Botão **Run agent** abre modal: repo (dos configurados), agente (`GET /api/agents` elegíveis), tier → `POST /api/agents/executions` com prompt = resumo da thread (system header + últimas 20 msgs, cap 8k); evento `system` na thread registra a fila; polling de `/api/agents/runs` posta o resultado final (succeeded/failed) como evento `system`.
- **RF-004** `DELETE /api/local/ai/threads/{id}` remove thread + eventos + runs (404 quando inexistente); botão 🗑 na sidebar com confirmação.
- **RF-005** `ExecuteRunAsync` para de injetar o user-msg "Continue the conversation." fixa; o run usa apenas o histórico real.
- **RF-006** Thread com run em andamento mostra estado `Running` (badge pulsante) e desabilita o composer até concluir (ou retry manual).
- **RF-007** Mensagens markdown sanitizadas (`MarkdownRenderer`); role system em cinza compacto; user à direita.

## 5. API Contract

```
POST   /api/local/ai/threads            { title, model, reasoningEffort, sandbox } → 201 { thread }
DELETE /api/local/ai/threads/{id}       → 204 | 404
GET    /api/local/ai/threads/{id}/events → SSE (snapshot + live)          [existe]
POST   /api/local/ai/threads/{id}/events { role, content } → 201          [existe]
POST   /api/local/ai/threads/{id}/runs   → 201 { run }                    [existe]
POST   /api/agents/executions            { repositoryFullName, agentType, modelTier, prompt, scope? } → 202 | 400 invalid-repository | 422 agent-not-eligible  [existe]
```

## 6. Acceptance Criteria

- **AC1** Crio uma thread nova pelo botão, envio mensagem e vejo a resposta do assistente chegando por SSE.
- **AC2** Botão Run agent enfileira execução num repo configurado; a thread ganha evento de fila e evento final com o resultado.
- **AC3** Excluir thread remove da sidebar e `GET` seguinte não a retorna.
- **AC4** Nenhum run injeta "Continue the conversation." — o assistente responde à última mensagem real.
- **AC5** Erro de provider/SSE mostra estado de erro na thread sem travar a página.

## 7. Task Plan

- T1: `DELETE` endpoint + `DeleteThreadAsync` + client methods + integration tests.
- T2: `ExecuteRunAsync` fix (remover msg fixa) + unit test.
- T3: `AiChat.razor` — sidebar + nova thread + área de mensagens + composer + SSE.
- T4: Run-agent picker + prompt builder + post-back de status; unit test do builder.
- T5: docs en/pt-br + suites + deploy.

## 8. Organization Guardrails

- Branch dedicada; sem push em main.
- SSE e execuções seguem auth existente; prompt do agente sanitiza segredos (não incluir API keys).
- Reusar `MarkdownRenderer`/`Spinner`/toasts — sem lib nova.

## 9. Definition of Done

- [ ] Fluxo completo criar→conversar→sub-tarefa agente funciona fim-a-fim.
- [ ] Unit + integration verdes; build limpo.
- [ ] docs + SPEC → Done; deploy verificado.
