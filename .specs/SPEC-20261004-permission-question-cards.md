# SPEC-20261004-permission-question-cards: Cards interativos de permissão/pergunta do agente

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Permission/question cards — aprovações de tool e perguntas do agente como cards interativos |
| Product / System | agent-harness |
| Module / Bounded Context | Application.Contracts + Integrations + Server + Blazor |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Status | In progress |
| Date | 2026-10-04 |
| Target agent | Devin |
| Related SPECs | SPEC-20260921-ai-code-chat-ux (permission card original), SPEC-20261001-chat-agent-delegation |

---

## 1. Executive Summary

### Problem

`session/request_permission` (ACP) é achatado para ids de opção no parser e
toda resposta colapsa em allow/always/deny (`MapOutcomeToOption` por
substring). Consequências:

1. **Perguntas do agente respondem errado**: `AskUserQuestion`/opções
   arbitrárias (`optionId`s sem kind allow/reject) têm a seleção mapeada para
   um id de permissão inexistente — o agente recebe uma opção que o usuário
   nunca escolheu.
2. **Labels reais perdidos**: o card mostra Always/Allow/Deny genéricos, não o
   texto que o agente anunciou.
3. **Kinds ignorados**: `allow_always`/`reject_*` não afetam cor nem
   semântica do card — perguntas e aprovações têm a mesma cara.

### Objective

- Preservar `{optionId, name, kind}` ponta a ponta (ACP → payload normalizado
  → `PermissionRequestInfo` → SSE → card).
- Responder com o `optionId` literal da opção clicada (round-trip exato).
- Renderizar labels reais, cores por kind e visual diferente para "pergunta
  do agente" vs "solicitação de permissão".

---

## 2. Requirements

### RF-001 — Options estruturadas no payload

`AcpProtocolParser.NormalizePermissionOptions` emite objetos
`{optionId, name, kind}` no payload normalizado (dialetos v1 e v2). Elementos
string legados viram `{optionId:"legacy", name:null, kind:null}`; array
vazio/ausente cai no default `allow`/`deny` com kinds
`allow_once`/`reject_once`.

### RF-002 — Round-trip por optionId

`MapOutcomeToOption` (AcpSessionClient) tenta match exato (case-insensitive)
entre `outcome` e `OptionId` antes do fallback substring — um reply carregando
o id literal de uma opção arbitrária faz round-trip correto.

### RF-003 — OptionDetails no contrato

`PermissionOptionInfo(OptionId, Label, Kind)` novo record;
`PermissionRequestInfo` ganha `OptionDetails` (nullable, opcional) +
`EffectiveOptions` (`[JsonIgnore]`: OptionDetails quando preenchido, senão
derivado de `Options` com label=id). `AcpSessionMessageParser` aceita options
string **e** objeto e sempre preenche `OptionDetails`.
`PermissionGate.RequestPermissionAsync` ganha parâmetro opcional
`optionDetails` repassado ao evento SSE `ai_chat.permission`.
`AcpClientToolHandler` envia detalhes com kinds `allow_once`/`reject_once`.

### RF-004 — Card interativo

`PermissionPromptCard` renderiza `Request.EffectiveOptions`: label real por
botão e cor por kind (`*_always`→Primary, `reject*`/`deny*`→Danger,
`allow*`→Success, demais→Secondary). Clique → `OnReply` com o `optionId`
literal. `IsQuestion` (nenhuma opção tem kind de permissão canônico nem id
allow/deny/reject-prefixed) muda badge/ícone/cor para "agent question"
(`alert-info`, `IconName.QuestionCircle`, `chat.agentQuestion`); senão
"permission request" (`alert-warning`, `IconName.ShieldLock`,
`chat.permissionRequest`). Strings nos 3 dicionários `UiStrings`.

### RF-005 — Timeline coerente

`AgentRunTimeline.PermOption` carrega `kind`; cor do botão segue a mesma
regra do card (kind → fallback id).

### Non-goals

- Resposta free-text ("Other"): ACP `request_permission` só aceita seleção de
  opção; resposta livre exigiria canal fora de banda.
- Agrupamento multi-pergunta (várias perguntas num só card).
- Push de permissão para SSE do cockpit (fora do chat) — já coberto por
  `ai_chat.permission` no thread SSE.

---

## 3. Data flow

```
agent ACP session/request_permission {options:[{optionId,name,kind}]}
  → dialect.ParsePermission → normalized payload (RF-001)
  → AcpSessionMessageParser → PermissionRequestInfo{Options, OptionDetails}
  → PermissionGate → SSE ai_chat.permission
  → AiChat PermissionPromptCard (labels+colors, question-vs-permission)
  → click → optionId literal → gate.Reply → outcome
  → AcpSessionClient.MapOutcomeToOption (exact match) → agent {outcome:{outcome:"selected",optionId}}
```
