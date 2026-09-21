# SPEC-20260921-acp-v2-readiness

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `acp-v2-readiness` |
| Type | `Architecture` (versioning + forward-compat) |
| Stack | `.NET 10 / JSON-RPC 2.0 over stdio (NDJSON)` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260921-acp-v2-readiness` |
| Ticket | [#282](https://github.com/afonsoft/agent-harness/issues/282) |
| Status | `Approved` |
| Depends on | `SPEC-20260921-acp-v1-conformance` |
| Consumed by | futura migração v2 estável |
| References | <https://agentclientprotocol.com/protocol/v2/migration> · <https://agentclientprotocol.com/protocol/v2/overview> |

## 1. User Story

**As a** mantenedor do Harness
**I want** uma camada de dialeto por versão do ACP — **v1 como default**, v2 opt-in por flag — com negociação por conexão e modelo de eventos que já entende upsert/patch semantics
**So that** quando agentes migrarem para v2 (ou quando quisermos testar `protocolVersion:2`), o Harness negocie, selecione o dialeto certo e degrade com segurança — sem reescrever o pipeline de eventos nem perder compatibilidade com agentes v1-only.

**Por que agora (evidência da doc v2):**

- v2 é uma *consolidação* com breaking changes reais: `session/prompt` deixa de encerrar o turno (vira ack com `messageId`; o fim do turno é `state_update` `idle`+`stopReason`), `tool_call` some (primeiro `tool_call_update` cria), `plan` vira `plan_update` com `planId`, modes morrem em favor de `configOptions`, `fs/*`+`terminal/*` client-side são **removidos**, `authenticate`→`auth/login`, `session/load`→`session/resume`+`replayFrom`, `session/list`+`session/close` viram baseline.
- A própria spec recomenda dual-stack: *"v1-only Agents and Clients will remain common… keep your v1 support working, and add v2 behind feature flags until it stabilizes."*
- Nosso modelo normalizado (`AgentExecutionEvent` com `ToolCallId`/merge na timeline) já é quase upsert-ready — falta `MessageId`/`PlanId`/`EntityKind` e o TurnTracker por dialeto.

## 2. Scope

**In scope:**
- Abstração `IAcpDialect` (v1 default + v2 experimental) selecionada por `protocolVersion` negociado no `initialize`.
- Version negotiation: enviar o máximo configurado; armazenar o negociado por sessão; falhar limpo quando incompatível.
- Campos de correlação upsert no envelope (`MessageId`, `PlanId`) usados pelo dialeto v2 e ignorados pelo v1.
- `ITurnTracker` por dialeto: v1 = resposta do `session/prompt` encerra; v2 = `state_update` `idle`+`stopReason` encerra.
- Parser v2: `state_update`, `tool_call_update` upsert, `tool_call_content_chunk`, `plan_update`, `user_message`/`agent_message`/`agent_thought` whole-message, `terminal_update`/`terminal_output_chunk` (display-only), `config_option_update`, `session_info_update`, `usage_update`, diff v2 (`changes`+`patch`), permission `title`/`subject`.
- Batch arrays NDJSON (v2 permite JSON-RPC batch por linha).
- Flags: `Taskboard:Acp:MaxProtocolVersion` (default `1`) — v2 nunca é default enquanto a spec estiver draft.

**Out of scope:**
- Tornar v2 default (bloqueado até a spec sair de draft e termos fixtures de conformidade).
- Transporte streamable HTTP/WebSocket (RFD em draft).
- v2 `schema.unstable.json` surfaces — cada uma atrás de flag própria, fora desta SPEC.
- Mudanças de UI além de tolerar os novos kinds (a timeline já renderiza por `kind` normalizado).

## 3. Functional Requirements

### RF-201 — `IAcpDialect`

```csharp
public interface IAcpDialect
{
    int ProtocolVersion { get; }                          // 1 | 2
    object BuildInitializeParams(ClientCaps caps);         // v1: clientCapabilities/clientInfo; v2: capabilities/info
    object BuildSessionNewParams(SessionRequest req);      // v1: mcpServers required; v2: optional
    object BuildPromptParams(string sessionId, ContentBlock[] prompt);
    ParsedUpdate ParseSessionUpdate(JsonElement update);   // v1 discriminant → v2 discriminant
    PermissionRequest? ParsePermission(JsonElement p);     // v1: toolCall+options; v2: title/subject/options
    bool IsTurnEnd(JsonElement promptResponse);            // v1: response.stopReason; v2: nunca (vai no state_update)
    // ...auth/lifecycle/config/permission method names per version
}
```

- `AcpV1Dialect` encapsula o comportamento atual (pós SPEC-v1-conformance).
- `AcpV2Dialect` implementa o wire v2 estável: `initialize{protocolVersion:2,info,capabilities}`, `session/new` (mcpServers opcional), `session/prompt`→ack`{messageId}`, `session/resume{replayFrom}`, `session/list`, `session/close`, `session/set_config_option` (com `type` discriminator), `session/cancel`, `auth/login`/`auth/logout` quando `authMethods` não-vazio.
- Seleção: após `initialize`, `holder.Dialect = Dialects.For(negotiatedVersion)`; `null` → erro `VersionUnsupported` + close.

### RF-202 — Negociação de versão

- `Taskboard:Acp:MaxProtocolVersion` default `1` → hoje sempre negocia v1.
- Com flag `2`: enviamos `protocolVersion:2`; agente v1-only responde `1` → desce para `AcpV1Dialect` (a conexão fala uma só versão, por spec).
- Agente responde versão > max ou 0/indefinida → falha `initialize` com evento `error` tipado e a thread fica `unsupported_version`.
- `/api/agents/state` passa a expor `protocolVersion` negociado por sessão.

### RF-203 — Envelope upsert-ready

`AgentExecutionEvent` ganha (nullable, sem breaking change):

| Campo | v1 | v2 |
|---|---|---|
| `MessageId` | opcional nos chunks | **required** — chave de upsert de mensagem |
| `PlanId` | implícito único | chave de `plan_update` |
| `PatchOp` | sempre `append` | `append`|`replace`|`clear` — derivado de omit/null/value |

Timeline: merge passa a ser por `(Kind, ToolCallId|MessageId|PlanId)` com `PatchOp` resolvendo replace/clear — v1 continua funcionando pois nunca emite `PatchOp!=append` nem ids de mensagem.

### RF-204 — `ITurnTracker` por dialeto

- **v1**: turno abre no `session/prompt` enviado; fecha na response (`stopReason`) ou `cancelled`.
- **v2**: turno abre no prompt ack (`messageId` guardado para reconciliar `user_message`); `state_update` `running`/`requires_action`/`idle` dirigem o estado; `idle`+`stopReason` fecha; `session/cancel` → espera `idle`+`cancelled` (não a response do prompt, que já retornou).
- Permissão pendente em v2 ⇒ agente emite `requires_action` → nosso estado unificado já tem `waiting_permission` — mapeamento direto.

### RF-205 — Parser v2

`AcpProtocolParser` vira façade: `Parse(line, dialect)`. Dialeto v2 mapeia:

| v2 `sessionUpdate` | Kind normalizado |
|---|---|
| `user_message`/`agent_message`/`agent_thought` (+ `_chunk`) | `message`/`thought` com `MessageId`+`PatchOp` |
| `state_update` | `lifecycle` (`running`/`idle`/`requires_action`+`stopReason`) |
| `tool_call_update` (cria no primeiro-seen) | `tool_call` (first-seen) / `tool_output` (patch) |
| `tool_call_content_chunk` | `tool_output` append |
| `plan_update` | `plan` com `PlanId`+replace |
| `terminal_update`/`terminal_output_chunk` | `terminal` events (display-only; decode base64 por chunk independente) |
| `config_option_update`/`session_info_update`/`usage_update`/`available_commands_update` | como v1 |
| desconhecido/`_`-prefixed | `activity` + raw preservado (regra v2: preservar variants desconhecidos) |

Diff v2 (`type:"diff"` com `changes[]`+`patch.git_patch`) → kind `diff` com payload estruturado (renderiza `patch.text`, headers de `changes`).

### RF-206 — Permissões v2

Parse de `session/request_permission` v2: `title` (required) + `description` + `subject` (`tool_call` → ToolCallUpdate payload | `command` → command/cwd/toolCallId/terminalId) + `options`. Normalizado para o mesmo `PendingPermission`; resposta idêntica (`outcome.selected/cancelled`). Regra v2 respeitada: outcome desconhecido recebido **nunca** é tratado como aprovação (nós só enviamos, mas o guard é barato).

### RF-207 — Superfícies removidas ficam v1-only

`fs/*` e `terminal/*` client-side (SPEC v1 RF-008/009) são registrados **somente** quando `dialect.ProtocolVersion == 1`. Em conexão v2, o anúncio nem sai (`capabilities` v2 não tem fs/terminal) e o client-side de tools deve ser oferecido via `mcpServers` (já coberto pelo RF-012 da SPEC v1 — alinhado com a orientação oficial de usar MCP para client tools).

### RF-208 — Batch NDJSON

Reader aceita linha que é **array** JSON-RPC: processa cada entry (requests com `id` respondidos individualmente; notifications sem resposta; entries inválidos → `-32600` por entry). Nunca fazemos batch de mensagens lifecycle-sensitive (`initialize`, `auth/*`, `session/new`, `session/resume`, `session/prompt`).

### RF-209 — Forward-compat obrigatório

- Enum/union values desconhecidos → preservados no raw + fallback genérico (nunca exception de parse).
- `_`-prefixed em qualquer posição → round-trip intacto; `_meta` propagado (inclui `traceparent`/`tracestate`/`baggage` para OTel).
- `config_option` `category` desconhecida → renderiza como opção genérica.
- Parse estrito só para discriminantes **conhecidos** (malformed dentro de variant conhecido = erro de parse, não fallback silencioso) — regra "strict where it counts" da spec.

## 4. Non-Functional Requirements

- **NFR-1** Uma conexão = uma versão. Nada de mix v1/v2 na mesma stdio.
- **NFR-2** v2 nunca default: exige `Taskboard:Acp:MaxProtocolVersion=2` explícito; documentado como experimental enquanto a spec for draft.
- **NFR-3** Zero regressão v1: suite de contrato v1 existente deve passar intacta com `AcpV1Dialect`.
- **NFR-4** Fixtures separadas por versão (`tests/fixtures/acp/v1`, `…/v2`) — recomendação explícita da migration checklist para SDKs.
- **NFR-5** Três estados (omit/null/value) modelados explicitamente no parser v2 — `Optional<T>`/presence-tracking, não nullable simples.

## 5. Sequência de entrega

1. `IAcpDialect` + `AcpV1Dialect` (refactor puro — v1 default, zero mudança de comportamento).
2. Negociação + `protocolVersion` no estado exposto + `ITurnTracker` v1.
3. Envelope upsert (`MessageId`/`PlanId`/`PatchOp`) + merge na timeline.
4. `AcpV2Dialect` mínimo: initialize v2, session new/prompt/cancel/resume/close/list, state_update lifecycle, tool_call_update upsert, plan_update, permission v2, config options.
5. Batch + fixtures v2 + testes de conformidade lado-a-lado (mesmo cenário rodado nos dois dialetos).

Passos 1–3 podem mergear sozinhos (preparação invisível). Passo 4 fica atrás da flag.

## 6. Test Plan

- Fixture agent dual: responde `initialize` com a versão pedida e fala v1 ou v2 conforme negociado.
- Contrato: mesma conversa (prompt→tool_call→permission→plan→end) produz a mesma sequência de kinds normalizados nos dois dialetos.
- Negociação: max=2 + agente v1 → sessão v1; max=1 + agente v2-only → `unsupported_version`; batch array com entry inválida → `-32600` só na entry.
- Upsert: `tool_call_update` first-seen cria card; `plan_update` substitui entries por `planId`; `agent_message`+chunk compõe conteúdo; `null` limpa campo.
- Turno v2: cancel → `idle`+`cancelled` fecha turno (não a response do prompt).

## 7. Acceptance Criteria

1. Default permanece v1: sem flags, todo tráfego é `protocolVersion:1` (teste prova).
2. Com `MaxProtocolVersion=2` e agente v2: handshake v2, prompt ack `messageId`, turno fecha em `state_update idle`, tool calls via upsert — mesma timeline que v1.
3. Agente v1-only + flag 2 → fallback v1 transparente.
4. Nenhum método v1 removido vaza em conexão v2 (`fs/*`,`terminal/*`,`session/load`,`set_mode` ficam indisponíveis).
5. Envelope ganha `MessageId`/`PlanId`/`PatchOp` sem migration destrutiva nem breaking change na API de eventos.
6. Cobertura não regride; docs en/pt-br descrevem a negociação e a flag.

## 8. Riscos

- **Spec v2 é draft**: pode mudar → mitigado por flag, fixtures isoladas e dialeto fino (shared app logic não conhece wire format).
- **Agentes "v2" parciais**: negociação por conexão + fallback + log do `agentInfo`/versão negociada em `session_info` para diagnóstico.
- **fs/terminal removidos**: quem depender disso fica em v1 até mover tools para MCP — documentado no rollout.
