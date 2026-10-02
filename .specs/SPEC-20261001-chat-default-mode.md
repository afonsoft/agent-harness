# SPEC-20261001-chat-default-mode: modo padrão do AI Chat

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | AI Chat — Default Mode Policy |
| Product / System | agent-harness |
| Module / Bounded Context | Presentation + Application |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261001-chat-ux` |
| Technical owner | afonsoft |
| Status | Done — entregue via [PR #411](https://github.com/afonsoft/agent-harness/pull/411) (merged 2026-09-30) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Related SPECs | SPEC-20260929-ai-code-provider-chat, SPEC-20261001-chat-capability-registry, SPEC-20261001-chat-ux-compact |

---

## 1. Executive Summary

### Problem

`/ai-chat` abre hoje com `_cfgMode = "assistant"` e `_cfgView = "chat"`,
mas a seleção efetiva depende do último thread e de checks espalhados.
Não há uma política única e testável de "modo padrão": o usuário precisa
saber se deve usar Chat (provider), Chat de CLI ou Agent.

### Objective

Definir uma política determinística de modo padrão, com override por
configuração:

- **Chat** quando existe ao menos um canal de chat utilizável (provider
  habilitado com modelos OU CLI agente chat-capable configurado).
- **Agent** quando nenhum canal de chat está configurado — o usuário nunca
  cai numa tela vazia de chat.
- Override administrável via `Taskboard:AiChat:DefaultMode`
  (`auto|chat|agent`, default `auto`).

### Expected outcome

- Abrir `/ai-chat` sem estado prévio cai direto no modo mais útil
  disponível: Chat se configurado, senão Agent.
- Um usuário sem nenhum provider/CLI de chat nunca vê um composer
  desabilitado sem explicação — ele cai em Agent mode com a lista de
  agentes elegíveis.
- A escolha explícita do usuário no seletor de modo continua valendo
  durante a sessão e para threads existentes.

### Out of scope

- Persistência por usuário da última escolha (pode virar followup).
- Reordenação do seletor de modo ou remoção de opções (SPEC-20261001-chat-ux-compact cobre o seletor).
- Onboarding/setup wizard de providers.

---

## 2. Agent Role

> Blazor/Application engineer — pequena mudança de política de estado,
> com testes.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não alterar endpoints nem contratos de streaming.
- Não remover a checagem `AnyChatCapableCli` — ela continua governando a
  opção "chat de CLI".
- Não criar preferência por usuário nesta iteração.

---

## 4. Product Context

### Technical context

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — `_cfgMode`,
  `IsAgentMode`, `AnyChatCapableCli`, `ThreadMode`, seletor de modo.
- `src/Taskboard.Application/AiChat/AiChatService.cs` — catálogo de
  providers/modelos e CLIs.
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs`
  — registry de chaves editáveis (mesma família `Taskboard:Chat:*` já
  usada).

### Relevant files

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs`
- `tests/Taskboard.Tests.Unit/` (novo teste source-level ou de serviço)

---

## 5. Functional Requirements

### FR-001: Resolução do modo padrão

Política `ResolveDefaultMode(snapshot)`:

```
DefaultMode == "chat"  → chat se ChatAvailable, senão agent + warning badge
DefaultMode == "agent" → agent
DefaultMode == "auto"  → chat se ChatAvailable, senão agent
```

`ChatAvailable` = (`AnyChatCapableCli` OU existe provider de chat
habilitado com ≥1 modelo). A decisão usa o mesmo snapshot carregado no
`OnInitializedAsync` — sem requests extras.

### FR-002: Chave de configuração

Nova entrada no `RuntimeConfigurationService`:

```csharp
new("Taskboard:AiChat:DefaultMode", "auto", Editable: true,
    RequiresRestart: false, EnvAlias: "HARNESS_AICHAT_DEFAULT_MODE",
    Validate: ValidateDefaultMode) // auto|chat|agent
```

Editável na aba Chat de Settings (SPEC-20260930-settings-tabs +
SPEC-20261001-chat-capability-registry).

### FR-003: Precedência

1. Thread ativo existente → o modo do thread (`ThreadMode`) vence.
2. Seleção manual do usuário nesta sessão → vence sobre o default.
3. Caso contrário → política FR-001.

### FR-004: Sinalização de fallback

Quando o default resolvido é `agent` por falta de canal de chat, um
hint inline (texto pequeno junto ao seletor) informa:
"Chat indisponível — configure um provider ou CLI com suporte a chat em
Settings → Chat." O hint some quando o modo é trocado manualmente.

---

## 6. Business Rules

- Nenhum modo é removido; `auto` nunca produz modo inválido.
- Configuração inválida de `DefaultMode` cai em `auto` com warning log.

---

## 7. Expected Architecture

```
AiChat.OnInitializedAsync
  └─ snapshot = AiChatService.LoadAsync()
  └─ mode = DefaultModePolicy.Resolve(cfg, snapshot)
        ├─ "chat"  → ChatAvailable ? chat : agent(+hint)
        ├─ "agent" → agent
        └─ "auto"  → ChatAvailable ? chat : agent(+hint)
```

`DefaultModePolicy` — classe `internal static` em
`src/Taskboard.Application/AiChat/` para ser testável sem Blazor.

---

## 8. API Contracts

Nenhuma alteração de endpoint. Apenas nova chave de configuração
(FR-002), servida pelos endpoints de config existentes.

---

## 9. Edge Cases

- `DefaultMode=chat` mas `ChatAvailable=false` → agent + hint, nunca erro.
- CLI selecionado não é chat-capable (mas modo chat resolvido) → o fluxo
  existente de fallback para terminal view permanece.
- Provider habilitado mas sem modelos → não conta para `ChatAvailable`.
- Thread carregado via rail → precedência do thread (FR-003.1) ignora o
  default.

---

## 10. Non-Functional Requirements

- Zero requests adicionais no load (usa snapshot existente).
- Decisão determinística e coberta por testes de unidade puros.

---

## 11. Mandatory Guardrails

- Build com `TreatWarningsAsErrors` limpo.
- Testes novos em português (`Dado_Quando_Entao`).

---

## 12. Expected Tests

- `Dado_ModoAuto_Quando_ProviderChatConfigurado_Entao_DefaultChat`
- `Dado_ModoAuto_Quando_SemCanalDeChat_Entao_DefaultAgentComHint`
- `Dado_ModoChat_Quando_ChatIndisponivel_Entao_FallbackAgent`
- `Dado_ModoAgent_Quando_ChatDisponivel_Entao_DefaultAgent`
- `Dado_ThreadExistente_Quando_Abre_Entao_ModoDoThreadVence`

---

## 13. Acceptance Criteria

1. Sem `?` ou estado: `/ai-chat` abre em Chat quando há provider/CLI de
   chat configurado.
2. Sem nenhum canal de chat: abre em Agent com hint explicativo.
3. Chave `Taskboard:AiChat:DefaultMode` editável via Settings → Chat e
   env `HARNESS_AICHAT_DEFAULT_MODE`.
4. Suite de testes continua verde + novos testes.

---

## 14. Implementation Plan

1. RED: testes do `DefaultModePolicy`.
2. Implementar `DefaultModePolicy` + chave de config.
3. Aplicar resolução no `OnInitializedAsync` de `AiChat.razor` + hint.
4. GREEN + build limpo.

---

## 15. Rollback Strategy

- Reverter o commit; o comportamento volta ao literal `"assistant"`.

---

## Pending Questions

1. O default `auto` (chat-se-configurado) corresponde ao desejado, ou o
   pedido é `chat` fixo com fallback silencioso?
2. Persistir a última escolha por usuário em followup?

---

## Human Approval Checklist

- [ ] Política `auto`/`chat`/`agent` aprovada.
- [ ] Hint de fallback na UI aprovado.
- [ ] Nova chave de config aceita.
