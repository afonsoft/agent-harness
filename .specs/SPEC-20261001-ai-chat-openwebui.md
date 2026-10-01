# SPEC-20261001-ai-chat-openwebui: ai-chat com paridade Open WebUI — inline tool markup, tools novas e layout

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | AI Chat Open WebUI parity (inline tool-call markup filter, tool result rendering, new builtin tools, layout) |
| Product / System | agent-harness |
| Module / Bounded Context | Chat (Application + Integrations + Blazor Presentation) |
| Change type | Feature + Bugfix |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/*-ai-chat-tools-ux` |
| Technical owner | afonsoft |
| Status | Implemented (pending human review) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Related SPECs | SPEC-20260929-ai-code-provider-chat, SPEC-20261001-chat-ux-compact, SPEC-20261001-chat-skills-slash-commands, SPEC-20261001-chat-agent-delegation |

---

## 1. Executive Summary

### Problem

Modelos que não implementam `tool_calls` estruturado (DeepSeek DSML,
Hermes/Qwen `<tool_call>`) emitem a chamada como **markup inline em
`delta.content`** — o texto `<｜DSML｜function_calls>…` vazava na UI e
persistia na conversa, e a tool nunca executava. Além disso, o collapse
das tools despejava o JSON bruto do resultado (ilegível), o conjunto de
tools era menor que o do open-webui, e o layout do ProviderChat
divergia do padrão de chat centrado.

### Objective

- **RF-001 — Filtro de markup inline**: stream filter que remove blocos
  `<｜DSML｜function_calls>` (pipes fullwidth `｜` e ASCII `|`) e
  `<tool_call>` de `delta.content` — nunca chegam à UI nem persistem; os
  blocos capturados são materializados como `OpenAiToolCall` e executam
  no tool loop normal. Mensagens antigas persistidas com markup são
  limpas na borda do DTO e no transcript reenviado ao provider.
- **RF-002 — Resultado de tool legível**: `ToolResultFormatter`
  renderiza o JSON do resultado no mini-terminal — campos de texto
  verbatim, listas como linhas, resultados de busca numerados, erro em
  primeiro lugar, ANSI removido.
- **RF-003 — Paridade de tools (open-webui)**: novas tools `fetch_url`,
  `current_datetime`, `calculator` e `memory` (store persistente em
  `<dataDir>/chat-memory.json`, scrubada). Melhorias nas existentes:
  `web_search` ganha `count`, `read_file` ganha `offset`/`limit`,
  `shell_exec` ganha `timeout_seconds`. `memory` entra em
  `MutatingTools`.
- **RF-004 — Layout estilo open-webui**: thread centralizada (~48rem),
  mensagens do assistente flat full-width, bolhas do usuário à direita,
  empty state central ("How can I help you today?"), composer flutuante
  arredondado com botão de envio circular, card de tool com chevron e
  ícone, ação de copiar (hover) em respostas do assistente.

### Out of scope

- Tools do open-webui que dependem de features inexistentes aqui:
  knowledge/RAG, channels, notes DB, notify, delegate multi-agente,
  image edit. `generate_image` já existe.
- Execução de código livre no estilo Jupyter do open-webui —
  `code_interpreter`/`shell_exec` já cobrem via sandbox.

---

## 2. Requirements

| Req | Description | Verification |
|---|---|---|
| RF-001 | DSML/`<tool_call>` inline filtrado do stream, materializado e executado; markup antigo limpo em DTO/transcript | `InlineToolCallMarkupTests`, `ChatServiceTests.Dado_ProviderEmiteDsml_*` |
| RF-002 | Tool result JSON renderizado legível no collapse | `ToolResultFormatterTests` |
| RF-003 | `fetch_url`, `current_datetime`, `calculator`, `memory` registradas; params novos em `web_search`/`read_file`/`shell_exec` | `ChatOpenWebuiToolTests`, `ChatToolTests` |
| RF-004 | Thread centrada, composer arredondado, tool card e copy action | revisão visual + build |

## 3. Arquitetura

```
OpenAiCompatibleClient.StreamChatAsync (SSE)
   └─► StreamOutcome.AccumulateChunk
         └─► InlineToolCallMarkup.Feed(delta) → visible text only
   └─► stream.Complete() → Flush() tail
   └─► MaterializeToolCalls() → structured tool_calls
        └─ fallback: _inlineMarkup.MaterializeCalls()
   └─► RunToolCallsAsync → ChatMessage.CreateTool (persistido)

ProviderChat.razor
   └─► <details> tool card → ToolResultFormatter.FormatForTerminal(content)
```

`ChatMemoryStore` segue o padrão de `ChatImageStore`: arquivo JSON em
`<dataDir>` (`environment.GetDataDir()`), lock interno, cap de 500 itens
e 2000 chars por item; `MemoryTool` scruba o conteúdo via
`ISecretRedactor` antes de persistir.

## 4. Acceptance Criteria

- [x] Mensagem contendo `<｜DSML｜function_calls>` (ambas variantes de
  pipe) não exibe markup; a tool executa e o resultado aparece no card.
- [x] `<tool_call>{json}</tool_call>` materializa chamada equivalente.
- [x] Markup quebrado em múltiplos deltas SSE não vaza nem dispara
  falso-positivo em texto normal.
- [x] Bloco sem fechamento é descartado (nunca renderiza parcial).
- [x] Collapse de `web_search` mostra lista numerada; `shell_exec`
  mostra `exitCode` + saída verbatim; erros aparecem primeiro.
- [x] `fetch_url` recusa não-http(s); HTML vira texto legível.
- [x] `memory` add/list/search/update/delete persiste em
  `chat-memory.json` sem segredos.
- [x] `calculator` avalia expressões com precedência/funções sem exec.
- [x] Layout: thread central, composer pill, user bubble à direita,
  copy button em resposta de assistente.

## 5. Tests

- `InlineToolCallMarkupTests` — feed/flush/materialize, variantes de
  pipe, envelope `<tool_call>`, bloco truncado, texto ao redor.
- `ToolResultFormatterTests` — texto livre, ANSI, output verbatim,
  erro primeiro, resultados numerados, entries, JSON aninhado.
- `ChatOpenWebuiToolTests` — calculator, current_datetime, fetch_url
  (handler fake), memory ciclo completo + scrub.
- `ChatServiceTests.Dado_ProviderEmiteDsml_*` — fim-a-fim do tool loop
  com DSML inline.
